-- ============================================
-- STRIPE CONNECT MIGRATION 004
-- Adds Connect-related fields to Payments table
-- Captures: which location, which connected account, payment method type, fee breakdown
-- All new fields are nullable (cash/check payments don't have Stripe data)
-- ============================================

-- 1. LocationId
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'LocationId')
BEGIN
    ALTER TABLE Payments ADD LocationId INT NULL;
    PRINT 'Added Payments.LocationId';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Payments_Locations')
BEGIN
    ALTER TABLE Payments ADD CONSTRAINT FK_Payments_Locations
        FOREIGN KEY (LocationId) REFERENCES Locations(LocationId);
    PRINT 'Added FK_Payments_Locations';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_LocationId' AND object_id = OBJECT_ID('Payments'))
BEGIN
    CREATE INDEX IX_Payments_LocationId ON Payments(LocationId);
    PRINT 'Added IX_Payments_LocationId';
END

-- 2. StripeConnectAccountId
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'StripeConnectAccountId')
BEGIN
    ALTER TABLE Payments ADD StripeConnectAccountId INT NULL;
    PRINT 'Added Payments.StripeConnectAccountId';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Payments_StripeConnectAccounts')
BEGIN
    ALTER TABLE Payments ADD CONSTRAINT FK_Payments_StripeConnectAccounts
        FOREIGN KEY (StripeConnectAccountId) REFERENCES StripeConnectAccounts(StripeConnectAccountId);
    PRINT 'Added FK_Payments_StripeConnectAccounts';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_StripeConnectAccountId' AND object_id = OBJECT_ID('Payments'))
BEGIN
    CREATE INDEX IX_Payments_StripeConnectAccountId ON Payments(StripeConnectAccountId);
    PRINT 'Added IX_Payments_StripeConnectAccountId';
END

-- 3. PaymentMethodType (0=Online, 1=CardPresent, 2=ACH future)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'PaymentMethodType')
BEGIN
    ALTER TABLE Payments ADD PaymentMethodType INT NULL;
    PRINT 'Added Payments.PaymentMethodType';
END

-- 4. Fee breakdown columns (in cents)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'ClinicTotalFeeCents')
BEGIN
    ALTER TABLE Payments ADD ClinicTotalFeeCents INT NULL;
    PRINT 'Added Payments.ClinicTotalFeeCents';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'StripeProcessingFeeCents')
BEGIN
    ALTER TABLE Payments ADD StripeProcessingFeeCents INT NULL;
    PRINT 'Added Payments.StripeProcessingFeeCents';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'ApplicationFeeCents')
BEGIN
    ALTER TABLE Payments ADD ApplicationFeeCents INT NULL;
    PRINT 'Added Payments.ApplicationFeeCents';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'NetToClinicCents')
BEGIN
    ALTER TABLE Payments ADD NetToClinicCents INT NULL;
    PRINT 'Added Payments.NetToClinicCents';
END

-- 5. StripeChargeId (in addition to existing StripePaymentIntentId)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'StripeChargeId')
BEGIN
    ALTER TABLE Payments ADD StripeChargeId NVARCHAR(255) NULL;
    PRINT 'Added Payments.StripeChargeId';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payments_StripeChargeId' AND object_id = OBJECT_ID('Payments'))
BEGIN
    CREATE INDEX IX_Payments_StripeChargeId ON Payments(StripeChargeId);
    PRINT 'Added IX_Payments_StripeChargeId';
END

-- 6. HasOpenDispute flag
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Payments') AND name = 'HasOpenDispute')
BEGIN
    ALTER TABLE Payments ADD HasOpenDispute BIT NOT NULL DEFAULT 0;
    PRINT 'Added Payments.HasOpenDispute';
END

PRINT 'Migration 004 complete';
