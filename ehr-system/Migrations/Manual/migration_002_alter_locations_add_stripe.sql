-- ============================================
-- STRIPE CONNECT MIGRATION 002
-- Adds Stripe Connect FK + per-location fee columns to Locations
-- Per-location fee storage: clinic-facing rate (e.g., 3.9% + $0.50 total)
-- Split by payment type (online vs card-present for Phase 2)
-- ============================================

-- 1. StripeConnectAccountId FK
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'StripeConnectAccountId')
BEGIN
    ALTER TABLE Locations ADD StripeConnectAccountId INT NULL;
    PRINT 'Added Locations.StripeConnectAccountId';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Locations_StripeConnectAccounts')
BEGIN
    ALTER TABLE Locations ADD CONSTRAINT FK_Locations_StripeConnectAccounts
        FOREIGN KEY (StripeConnectAccountId) REFERENCES StripeConnectAccounts(StripeConnectAccountId);
    PRINT 'Added FK_Locations_StripeConnectAccounts';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_StripeConnectAccountId' AND object_id = OBJECT_ID('Locations'))
BEGIN
    CREATE INDEX IX_Locations_StripeConnectAccountId
        ON Locations(StripeConnectAccountId);
    PRINT 'Added IX_Locations_StripeConnectAccountId';
END

-- 2. Online fee columns (clinic-facing rate; null = use global default)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'OnlineFeePercent')
BEGIN
    ALTER TABLE Locations ADD OnlineFeePercent DECIMAL(5,2) NULL;
    PRINT 'Added Locations.OnlineFeePercent';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'OnlineFeeFlatCents')
BEGIN
    ALTER TABLE Locations ADD OnlineFeeFlatCents INT NULL;
    PRINT 'Added Locations.OnlineFeeFlatCents';
END

-- 3. Card-present fee columns (Phase 2; null = use global default)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'CardPresentFeePercent')
BEGIN
    ALTER TABLE Locations ADD CardPresentFeePercent DECIMAL(5,2) NULL;
    PRINT 'Added Locations.CardPresentFeePercent';
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'CardPresentFeeFlatCents')
BEGIN
    ALTER TABLE Locations ADD CardPresentFeeFlatCents INT NULL;
    PRINT 'Added Locations.CardPresentFeeFlatCents';
END

PRINT 'Migration 002 complete';
