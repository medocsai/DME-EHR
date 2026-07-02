-- ============================================
-- COPAY INTEGRATION MIGRATION
-- Adds InstallmentPlans, InstallmentDetails tables
-- Adds Stripe fields to Patients and Payments
-- Removes redundant RunningBalance from PatientLedgers
-- ============================================

-- 1. Add StripeCustomerId to Patients
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Patients') AND name = 'StripeCustomerId')
BEGIN
    ALTER TABLE Patients ADD StripeCustomerId NVARCHAR(255) NULL;
    PRINT 'Added StripeCustomerId to Patients';
END

-- 2. Add Stripe/Installment fields to Payments
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'StripePaymentIntentId')
BEGIN
    ALTER TABLE Payments ADD StripePaymentIntentId NVARCHAR(255) NULL;
    PRINT 'Added StripePaymentIntentId to Payments';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'InstallmentDetailId')
BEGIN
    ALTER TABLE Payments ADD InstallmentDetailId INT NULL;
    PRINT 'Added InstallmentDetailId to Payments';
END

-- 3. Create InstallmentPlans table
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('InstallmentPlans') AND type = 'U')
BEGIN
    CREATE TABLE InstallmentPlans (
        PlanId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId INT NOT NULL,
        PatientId INT NOT NULL,
        TotalAmount DECIMAL(10, 2) NOT NULL,
        NumberOfInstallments INT NOT NULL,
        Status INT NOT NULL DEFAULT 0,  -- 0=Active, 1=Completed, 2=Cancelled, 3=Defaulted
        StripeCustomerId NVARCHAR(255) NULL,
        StripePaymentMethodId NVARCHAR(255) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy INT NULL,

        CONSTRAINT FK_InstallmentPlans_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId),
        CONSTRAINT FK_InstallmentPlans_Patients FOREIGN KEY (PatientId)
            REFERENCES Patients(PatientId)
    );

    CREATE INDEX IX_InstallmentPlans_TenantId_PatientId
        ON InstallmentPlans(TenantId, PatientId);

    PRINT 'Created InstallmentPlans table';
END

-- 4. Create InstallmentDetails table
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('InstallmentDetails') AND type = 'U')
BEGIN
    CREATE TABLE InstallmentDetails (
        DetailId INT IDENTITY(1,1) PRIMARY KEY,
        PlanId INT NOT NULL,
        TenantId INT NOT NULL,
        InstallmentNumber INT NOT NULL,
        DueDate DATE NOT NULL,
        Amount DECIMAL(10, 2) NOT NULL,
        Status INT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Paid, 2=Failed, 3=Delinquent
        RetryCount INT NOT NULL DEFAULT 0,
        PaymentId INT NULL,
        StripePaymentIntentId NVARCHAR(255) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
        PaidAt DATETIME NULL,

        CONSTRAINT FK_InstallmentDetails_Plans FOREIGN KEY (PlanId)
            REFERENCES InstallmentPlans(PlanId),
        CONSTRAINT FK_InstallmentDetails_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId),
        CONSTRAINT FK_InstallmentDetails_Payments FOREIGN KEY (PaymentId)
            REFERENCES Payments(PaymentId)
    );

    CREATE INDEX IX_InstallmentDetails_TenantId_Status_DueDate
        ON InstallmentDetails(TenantId, Status, DueDate);

    PRINT 'Created InstallmentDetails table';
END

-- 5. Add FK from Payments.InstallmentDetailId to InstallmentDetails
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Payments_InstallmentDetails')
BEGIN
    ALTER TABLE Payments ADD CONSTRAINT FK_Payments_InstallmentDetails
        FOREIGN KEY (InstallmentDetailId) REFERENCES InstallmentDetails(DetailId);
    PRINT 'Added FK_Payments_InstallmentDetails';
END

-- 6. Remove RunningBalance from PatientLedgers (redundant — balance calculated real-time)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientLedgers') AND name = 'RunningBalance')
BEGIN
    -- Check if there's a default constraint on RunningBalance
    DECLARE @constraintName NVARCHAR(256);
    SELECT @constraintName = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
    WHERE c.object_id = OBJECT_ID('PatientLedgers') AND c.name = 'RunningBalance';

    IF @constraintName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE PatientLedgers DROP CONSTRAINT ' + @constraintName);
    END

    ALTER TABLE PatientLedgers DROP COLUMN RunningBalance;
    PRINT 'Removed RunningBalance from PatientLedgers';
END

-- 7. Create CopayPaymentTokens table (for email payment links)
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('CopayPaymentTokens') AND type = 'U')
BEGIN
    CREATE TABLE CopayPaymentTokens (
        TokenId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId INT NOT NULL,
        PatientId INT NOT NULL,
        Token NVARCHAR(100) NOT NULL,
        ExpiresAt DATETIME NOT NULL,
        IsUsed BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT FK_CopayPaymentTokens_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId),
        CONSTRAINT FK_CopayPaymentTokens_Patients FOREIGN KEY (PatientId)
            REFERENCES Patients(PatientId)
    );

    CREATE INDEX IX_CopayPaymentTokens_Token ON CopayPaymentTokens(Token);
    PRINT 'Created CopayPaymentTokens table';
END

PRINT 'Copay integration migration complete';
