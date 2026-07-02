-- ============================================================
-- STRIPE CONNECT — COMBINED MIGRATION FOR PRODUCTION
-- Phase 1 — Online payments via Stripe Connect Standard accounts
-- ============================================================
-- SAFE TO RE-RUN: all statements are idempotent (IF NOT EXISTS guards)
-- EXCLUDES test data cleanup (migration 010) — run that manually only if needed
--
-- REQUIRED SETUP BEFORE RUNNING:
--   sqlcmd -S <server> -d <database> -E -N -C -I -i migration_stripe_connect_ALL.sql
--   The -I flag (SET QUOTED_IDENTIFIER ON) is REQUIRED for the CREATE INDEX statements
--
-- CONTENTS (in order):
--   001. StripeConnectAccounts table
--   002. Locations: StripeConnectAccountId FK + 4 fee columns
--   003. Charges: LocationId FK
--   004. Payments: LocationId, StripeConnectAccountId, fee breakdown, etc.
--   005. InstallmentPlans: LocationId, StripeConnectAccountId, concurrency lock
--   006. StripeWebhookEvents table (idempotency)
--   007. PaymentRefunds table
--   008. InstallmentPlanAuditLog table
--   009. Backfill Charges.LocationId from existing Appointments
-- ============================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
GO

PRINT '=======================================================';
PRINT 'Stripe Connect Phase 1 — Combined Migration Starting';
PRINT '=======================================================';
GO

-- ============================================================
-- 001. Create StripeConnectAccounts table
-- ============================================================
PRINT '';
PRINT '--- 001: StripeConnectAccounts table ---';

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('StripeConnectAccounts') AND type = 'U')
BEGIN
    CREATE TABLE StripeConnectAccounts (
        StripeConnectAccountId  INT IDENTITY(1,1) PRIMARY KEY,
        TenantId                INT NOT NULL,
        StripeAccountId         NVARCHAR(255) NOT NULL,
        DisplayName             NVARCHAR(255) NOT NULL,
        BusinessEmail           NVARCHAR(255) NULL,
        Country                 NVARCHAR(2) NOT NULL DEFAULT 'US',
        DefaultCurrency         NVARCHAR(3) NOT NULL DEFAULT 'usd',
        Status                  INT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Active, 2=Restricted, 3=Disconnected
        ChargesEnabled          BIT NOT NULL DEFAULT 0,
        PayoutsEnabled          BIT NOT NULL DEFAULT 0,
        DetailsSubmitted        BIT NOT NULL DEFAULT 0,
        ConnectedAt             DATETIME NOT NULL DEFAULT GETUTCDATE(),
        ConnectedByUserId       INT NULL,
        DisconnectedAt          DATETIME NULL,
        LastWebhookAt           DATETIME NULL,
        CreatedAt               DATETIME NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt               DATETIME NULL,

        CONSTRAINT FK_StripeConnectAccounts_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId),
        CONSTRAINT FK_StripeConnectAccounts_Users FOREIGN KEY (ConnectedByUserId)
            REFERENCES Users(UserId),
        CONSTRAINT UQ_StripeConnectAccounts_StripeAccountId UNIQUE (StripeAccountId)
    );

    CREATE INDEX IX_StripeConnectAccounts_TenantId ON StripeConnectAccounts(TenantId);
    CREATE INDEX IX_StripeConnectAccounts_Status ON StripeConnectAccounts(Status);

    PRINT '  Created StripeConnectAccounts table + indexes';
END
ELSE
    PRINT '  StripeConnectAccounts already exists — skipped';
GO

-- ============================================================
-- 002. Alter Locations: add Stripe FK + fee columns
-- ============================================================
PRINT '';
PRINT '--- 002: Locations (Stripe FK + fees) ---';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'StripeConnectAccountId')
BEGIN
    ALTER TABLE Locations ADD StripeConnectAccountId INT NULL;
    PRINT '  Added Locations.StripeConnectAccountId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Locations_StripeConnectAccounts')
BEGIN
    ALTER TABLE Locations ADD CONSTRAINT FK_Locations_StripeConnectAccounts
        FOREIGN KEY (StripeConnectAccountId) REFERENCES StripeConnectAccounts(StripeConnectAccountId);
    PRINT '  Added FK_Locations_StripeConnectAccounts';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_StripeConnectAccountId' AND object_id = OBJECT_ID('Locations'))
BEGIN
    CREATE INDEX IX_Locations_StripeConnectAccountId ON Locations(StripeConnectAccountId);
    PRINT '  Added IX_Locations_StripeConnectAccountId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'OnlineFeePercent')
BEGIN
    ALTER TABLE Locations ADD OnlineFeePercent DECIMAL(5,2) NULL;
    PRINT '  Added Locations.OnlineFeePercent';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'OnlineFeeFlatCents')
BEGIN
    ALTER TABLE Locations ADD OnlineFeeFlatCents INT NULL;
    PRINT '  Added Locations.OnlineFeeFlatCents';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'CardPresentFeePercent')
BEGIN
    ALTER TABLE Locations ADD CardPresentFeePercent DECIMAL(5,2) NULL;
    PRINT '  Added Locations.CardPresentFeePercent';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'CardPresentFeeFlatCents')
BEGIN
    ALTER TABLE Locations ADD CardPresentFeeFlatCents INT NULL;
    PRINT '  Added Locations.CardPresentFeeFlatCents';
END
GO

-- ============================================================
-- 003. Alter Charges: add LocationId
-- ============================================================
PRINT '';
PRINT '--- 003: Charges.LocationId ---';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Charges') AND name = 'LocationId')
BEGIN
    ALTER TABLE Charges ADD LocationId INT NULL;
    PRINT '  Added Charges.LocationId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Charges_Locations')
BEGIN
    ALTER TABLE Charges ADD CONSTRAINT FK_Charges_Locations
        FOREIGN KEY (LocationId) REFERENCES Locations(LocationId);
    PRINT '  Added FK_Charges_Locations';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Charges_LocationId' AND object_id = OBJECT_ID('Charges'))
BEGIN
    CREATE INDEX IX_Charges_LocationId ON Charges(LocationId);
    PRINT '  Added IX_Charges_LocationId';
END
GO

-- ============================================================
-- 004. Alter Payments: add Connect fields
-- ============================================================
PRINT '';
PRINT '--- 004: Payments (Connect fields) ---';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'LocationId')
BEGIN
    ALTER TABLE Payments ADD LocationId INT NULL;
    PRINT '  Added Payments.LocationId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Payments_Locations')
BEGIN
    ALTER TABLE Payments ADD CONSTRAINT FK_Payments_Locations
        FOREIGN KEY (LocationId) REFERENCES Locations(LocationId);
    PRINT '  Added FK_Payments_Locations';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_LocationId' AND object_id = OBJECT_ID('Payments'))
BEGIN
    CREATE INDEX IX_Payments_LocationId ON Payments(LocationId);
    PRINT '  Added IX_Payments_LocationId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'StripeConnectAccountId')
BEGIN
    ALTER TABLE Payments ADD StripeConnectAccountId INT NULL;
    PRINT '  Added Payments.StripeConnectAccountId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Payments_StripeConnectAccounts')
BEGIN
    ALTER TABLE Payments ADD CONSTRAINT FK_Payments_StripeConnectAccounts
        FOREIGN KEY (StripeConnectAccountId) REFERENCES StripeConnectAccounts(StripeConnectAccountId);
    PRINT '  Added FK_Payments_StripeConnectAccounts';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_StripeConnectAccountId' AND object_id = OBJECT_ID('Payments'))
BEGIN
    CREATE INDEX IX_Payments_StripeConnectAccountId ON Payments(StripeConnectAccountId);
    PRINT '  Added IX_Payments_StripeConnectAccountId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'PaymentMethodType')
BEGIN
    ALTER TABLE Payments ADD PaymentMethodType INT NULL;
    PRINT '  Added Payments.PaymentMethodType';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'ClinicTotalFeeCents')
BEGIN
    ALTER TABLE Payments ADD ClinicTotalFeeCents INT NULL;
    PRINT '  Added Payments.ClinicTotalFeeCents';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'StripeProcessingFeeCents')
BEGIN
    ALTER TABLE Payments ADD StripeProcessingFeeCents INT NULL;
    PRINT '  Added Payments.StripeProcessingFeeCents';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'ApplicationFeeCents')
BEGIN
    ALTER TABLE Payments ADD ApplicationFeeCents INT NULL;
    PRINT '  Added Payments.ApplicationFeeCents';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'NetToClinicCents')
BEGIN
    ALTER TABLE Payments ADD NetToClinicCents INT NULL;
    PRINT '  Added Payments.NetToClinicCents';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'StripeChargeId')
BEGIN
    ALTER TABLE Payments ADD StripeChargeId NVARCHAR(255) NULL;
    PRINT '  Added Payments.StripeChargeId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_StripeChargeId' AND object_id = OBJECT_ID('Payments'))
BEGIN
    CREATE INDEX IX_Payments_StripeChargeId ON Payments(StripeChargeId);
    PRINT '  Added IX_Payments_StripeChargeId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'HasOpenDispute')
BEGIN
    ALTER TABLE Payments ADD HasOpenDispute BIT NOT NULL DEFAULT 0;
    PRINT '  Added Payments.HasOpenDispute';
END
GO

-- ============================================================
-- 005. Alter InstallmentPlans: add Connect fields + concurrency lock
-- ============================================================
PRINT '';
PRINT '--- 005: InstallmentPlans (Connect + lock) ---';

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'LocationId')
BEGIN
    ALTER TABLE InstallmentPlans ADD LocationId INT NULL;
    PRINT '  Added InstallmentPlans.LocationId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InstallmentPlans_Locations')
BEGIN
    ALTER TABLE InstallmentPlans ADD CONSTRAINT FK_InstallmentPlans_Locations
        FOREIGN KEY (LocationId) REFERENCES Locations(LocationId);
    PRINT '  Added FK_InstallmentPlans_Locations';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InstallmentPlans_LocationId' AND object_id = OBJECT_ID('InstallmentPlans'))
BEGIN
    CREATE INDEX IX_InstallmentPlans_LocationId ON InstallmentPlans(LocationId);
    PRINT '  Added IX_InstallmentPlans_LocationId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'StripeConnectAccountId')
BEGIN
    ALTER TABLE InstallmentPlans ADD StripeConnectAccountId INT NULL;
    PRINT '  Added InstallmentPlans.StripeConnectAccountId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InstallmentPlans_StripeConnectAccounts')
BEGIN
    ALTER TABLE InstallmentPlans ADD CONSTRAINT FK_InstallmentPlans_StripeConnectAccounts
        FOREIGN KEY (StripeConnectAccountId) REFERENCES StripeConnectAccounts(StripeConnectAccountId);
    PRINT '  Added FK_InstallmentPlans_StripeConnectAccounts';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InstallmentPlans_StripeConnectAccountId' AND object_id = OBJECT_ID('InstallmentPlans'))
BEGIN
    CREATE INDEX IX_InstallmentPlans_StripeConnectAccountId ON InstallmentPlans(StripeConnectAccountId);
    PRINT '  Added IX_InstallmentPlans_StripeConnectAccountId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'ProcessingLockId')
BEGIN
    ALTER TABLE InstallmentPlans ADD ProcessingLockId UNIQUEIDENTIFIER NULL;
    PRINT '  Added InstallmentPlans.ProcessingLockId';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'ProcessingLockedAt')
BEGIN
    ALTER TABLE InstallmentPlans ADD ProcessingLockedAt DATETIME NULL;
    PRINT '  Added InstallmentPlans.ProcessingLockedAt';
END
GO

-- ============================================================
-- 006. Create StripeWebhookEvents table (idempotency log)
-- ============================================================
PRINT '';
PRINT '--- 006: StripeWebhookEvents table ---';

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('StripeWebhookEvents') AND type = 'U')
BEGIN
    CREATE TABLE StripeWebhookEvents (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        StripeEventId       NVARCHAR(255) NOT NULL,
        EventType           NVARCHAR(100) NOT NULL,
        StripeAccountId     NVARCHAR(255) NULL,
        Payload             NVARCHAR(MAX) NOT NULL,
        Status              INT NOT NULL DEFAULT 0,  -- 0=Received, 1=Processed, 2=Failed, 3=Ignored
        ErrorMessage        NVARCHAR(MAX) NULL,
        ReceivedAt          DATETIME NOT NULL DEFAULT GETUTCDATE(),
        ProcessedAt         DATETIME NULL,

        CONSTRAINT UQ_StripeWebhookEvents_StripeEventId UNIQUE (StripeEventId)
    );

    CREATE INDEX IX_StripeWebhookEvents_EventType ON StripeWebhookEvents(EventType);
    CREATE INDEX IX_StripeWebhookEvents_Status ON StripeWebhookEvents(Status);
    CREATE INDEX IX_StripeWebhookEvents_ReceivedAt ON StripeWebhookEvents(ReceivedAt DESC);
    CREATE INDEX IX_StripeWebhookEvents_StripeAccountId ON StripeWebhookEvents(StripeAccountId);

    PRINT '  Created StripeWebhookEvents table + indexes';
END
ELSE
    PRINT '  StripeWebhookEvents already exists — skipped';
GO

-- ============================================================
-- 007. Create PaymentRefunds table
-- ============================================================
PRINT '';
PRINT '--- 007: PaymentRefunds table ---';

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('PaymentRefunds') AND type = 'U')
BEGIN
    CREATE TABLE PaymentRefunds (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        PaymentId           INT NOT NULL,
        TenantId            INT NOT NULL,
        StripeRefundId      NVARCHAR(255) NOT NULL,
        StripeChargeId      NVARCHAR(255) NULL,
        AmountCents         INT NOT NULL,
        Reason              NVARCHAR(100) NULL,
        Status              INT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Succeeded, 2=Failed, 3=Canceled
        RefundedAt          DATETIME NOT NULL DEFAULT GETUTCDATE(),
        CreatedByExternal   BIT NOT NULL DEFAULT 1,

        CONSTRAINT FK_PaymentRefunds_Payments FOREIGN KEY (PaymentId)
            REFERENCES Payments(PaymentId),
        CONSTRAINT FK_PaymentRefunds_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId),
        CONSTRAINT UQ_PaymentRefunds_StripeRefundId UNIQUE (StripeRefundId)
    );

    CREATE INDEX IX_PaymentRefunds_PaymentId ON PaymentRefunds(PaymentId);
    CREATE INDEX IX_PaymentRefunds_TenantId ON PaymentRefunds(TenantId);

    PRINT '  Created PaymentRefunds table + indexes';
END
ELSE
    PRINT '  PaymentRefunds already exists — skipped';
GO

-- ============================================================
-- 008. Create InstallmentPlanAuditLog table
-- ============================================================
PRINT '';
PRINT '--- 008: InstallmentPlanAuditLog table ---';

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('InstallmentPlanAuditLog') AND type = 'U')
BEGIN
    CREATE TABLE InstallmentPlanAuditLog (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        PlanId              INT NOT NULL,
        DetailId            INT NULL,
        TenantId            INT NOT NULL,
        Action              NVARCHAR(50) NOT NULL,
        OldStatus           INT NULL,
        NewStatus           INT NULL,
        Details             NVARCHAR(MAX) NULL,
        ActorType           NVARCHAR(20) NOT NULL,
        ActorId             INT NULL,
        CreatedAt           DATETIME NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT FK_InstallmentPlanAuditLog_Plans FOREIGN KEY (PlanId)
            REFERENCES InstallmentPlans(PlanId),
        CONSTRAINT FK_InstallmentPlanAuditLog_Details FOREIGN KEY (DetailId)
            REFERENCES InstallmentDetails(DetailId),
        CONSTRAINT FK_InstallmentPlanAuditLog_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId)
    );

    CREATE INDEX IX_InstallmentPlanAuditLog_PlanId ON InstallmentPlanAuditLog(PlanId);
    CREATE INDEX IX_InstallmentPlanAuditLog_DetailId ON InstallmentPlanAuditLog(DetailId);
    CREATE INDEX IX_InstallmentPlanAuditLog_CreatedAt ON InstallmentPlanAuditLog(CreatedAt DESC);
    CREATE INDEX IX_InstallmentPlanAuditLog_Action ON InstallmentPlanAuditLog(Action);

    PRINT '  Created InstallmentPlanAuditLog table + indexes';
END
ELSE
    PRINT '  InstallmentPlanAuditLog already exists — skipped';
GO

-- ============================================================
-- 009. Backfill Charges.LocationId from existing data
-- ============================================================
PRINT '';
PRINT '--- 009: Backfill Charges.LocationId ---';

-- From Appointments first
UPDATE c
SET c.LocationId = a.LocationId
FROM Charges c
INNER JOIN Appointments a ON c.AppointmentId = a.AppointmentId
WHERE c.LocationId IS NULL
  AND c.AppointmentId IS NOT NULL
  AND a.LocationId IS NOT NULL;

PRINT CONCAT('  Backfilled ', @@ROWCOUNT, ' Charges.LocationId from Appointments');

-- Then fallback from Patient.PreferredLocationId
UPDATE c
SET c.LocationId = p.PreferredLocationId
FROM Charges c
INNER JOIN Patients p ON c.PatientId = p.PatientId
WHERE c.LocationId IS NULL
  AND p.PreferredLocationId IS NOT NULL;

PRINT CONCAT('  Backfilled ', @@ROWCOUNT, ' Charges.LocationId from Patient.PreferredLocationId');
GO

-- ============================================================
-- VERIFICATION — list all new/modified objects
-- ============================================================
PRINT '';
PRINT '=======================================================';
PRINT 'Migration complete — verification summary:';
PRINT '=======================================================';

SELECT 'NEW TABLES' AS Category, name AS ObjectName
FROM sys.tables
WHERE name IN ('StripeConnectAccounts', 'StripeWebhookEvents', 'PaymentRefunds', 'InstallmentPlanAuditLog')

UNION ALL

SELECT 'LOCATION COLUMNS', name
FROM sys.columns
WHERE object_id = OBJECT_ID('Locations')
  AND name IN ('StripeConnectAccountId', 'OnlineFeePercent', 'OnlineFeeFlatCents', 'CardPresentFeePercent', 'CardPresentFeeFlatCents')

UNION ALL

SELECT 'CHARGE COLUMNS', name
FROM sys.columns
WHERE object_id = OBJECT_ID('Charges') AND name = 'LocationId'

UNION ALL

SELECT 'PAYMENT COLUMNS', name
FROM sys.columns
WHERE object_id = OBJECT_ID('Payments')
  AND name IN ('LocationId', 'StripeConnectAccountId', 'PaymentMethodType', 'ClinicTotalFeeCents',
               'StripeProcessingFeeCents', 'ApplicationFeeCents', 'NetToClinicCents', 'StripeChargeId', 'HasOpenDispute')

UNION ALL

SELECT 'INSTALLMENTPLAN COLUMNS', name
FROM sys.columns
WHERE object_id = OBJECT_ID('InstallmentPlans')
  AND name IN ('LocationId', 'StripeConnectAccountId', 'ProcessingLockId', 'ProcessingLockedAt')

ORDER BY Category, ObjectName;

PRINT '';
PRINT 'If you see 22 rows above, everything is in place.';
PRINT 'Stripe Connect Phase 1 migration COMPLETE';
GO
