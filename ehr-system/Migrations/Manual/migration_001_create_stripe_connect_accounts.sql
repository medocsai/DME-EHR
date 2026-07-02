-- ============================================
-- STRIPE CONNECT MIGRATION 001
-- Creates StripeConnectAccounts table
-- One row per connected Stripe account; can be linked to multiple Locations within the same Tenant
-- ============================================

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

    CREATE INDEX IX_StripeConnectAccounts_TenantId
        ON StripeConnectAccounts(TenantId);

    CREATE INDEX IX_StripeConnectAccounts_Status
        ON StripeConnectAccounts(Status);

    PRINT 'Created StripeConnectAccounts table';
END
ELSE
BEGIN
    PRINT 'StripeConnectAccounts table already exists';
END
