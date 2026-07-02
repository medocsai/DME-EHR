-- ============================================
-- STRIPE CONNECT MIGRATION 005
-- Adds Connect-related fields to InstallmentPlans
-- - LocationId: which location the plan was created at
-- - StripeConnectAccountId: snapshot of which connected account to charge against
-- - ProcessingLockId / ProcessingLockedAt: prevents concurrent background processing
-- ============================================

-- 1. LocationId
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'LocationId')
BEGIN
    ALTER TABLE InstallmentPlans ADD LocationId INT NULL;
    PRINT 'Added InstallmentPlans.LocationId';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InstallmentPlans_Locations')
BEGIN
    ALTER TABLE InstallmentPlans ADD CONSTRAINT FK_InstallmentPlans_Locations
        FOREIGN KEY (LocationId) REFERENCES Locations(LocationId);
    PRINT 'Added FK_InstallmentPlans_Locations';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InstallmentPlans_LocationId' AND object_id = OBJECT_ID('InstallmentPlans'))
BEGIN
    CREATE INDEX IX_InstallmentPlans_LocationId ON InstallmentPlans(LocationId);
    PRINT 'Added IX_InstallmentPlans_LocationId';
END

-- 2. StripeConnectAccountId (snapshot at plan creation)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'StripeConnectAccountId')
BEGIN
    ALTER TABLE InstallmentPlans ADD StripeConnectAccountId INT NULL;
    PRINT 'Added InstallmentPlans.StripeConnectAccountId';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_InstallmentPlans_StripeConnectAccounts')
BEGIN
    ALTER TABLE InstallmentPlans ADD CONSTRAINT FK_InstallmentPlans_StripeConnectAccounts
        FOREIGN KEY (StripeConnectAccountId) REFERENCES StripeConnectAccounts(StripeConnectAccountId);
    PRINT 'Added FK_InstallmentPlans_StripeConnectAccounts';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InstallmentPlans_StripeConnectAccountId' AND object_id = OBJECT_ID('InstallmentPlans'))
BEGIN
    CREATE INDEX IX_InstallmentPlans_StripeConnectAccountId
        ON InstallmentPlans(StripeConnectAccountId);
    PRINT 'Added IX_InstallmentPlans_StripeConnectAccountId';
END

-- 3. ProcessingLockId / ProcessingLockedAt (concurrency control for background processor)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'ProcessingLockId')
BEGIN
    ALTER TABLE InstallmentPlans ADD ProcessingLockId UNIQUEIDENTIFIER NULL;
    PRINT 'Added InstallmentPlans.ProcessingLockId';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('InstallmentPlans') AND name = 'ProcessingLockedAt')
BEGIN
    ALTER TABLE InstallmentPlans ADD ProcessingLockedAt DATETIME NULL;
    PRINT 'Added InstallmentPlans.ProcessingLockedAt';
END

PRINT 'Migration 005 complete';
