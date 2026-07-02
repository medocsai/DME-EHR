-- ============================================
-- STRIPE CONNECT MIGRATION 007
-- Creates PaymentRefunds table
-- Tracks refunds initiated by clinics from their Stripe dashboard
-- Synced via charge.refunded webhook (clinic refunds, we just sync state)
-- ============================================

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('PaymentRefunds') AND type = 'U')
BEGIN
    CREATE TABLE PaymentRefunds (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        PaymentId           INT NOT NULL,
        TenantId            INT NOT NULL,
        StripeRefundId      NVARCHAR(255) NOT NULL,
        StripeChargeId      NVARCHAR(255) NULL,
        AmountCents         INT NOT NULL,
        Reason              NVARCHAR(100) NULL,  -- duplicate, fraudulent, requested_by_customer
        Status              INT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Succeeded, 2=Failed, 3=Canceled
        RefundedAt          DATETIME NOT NULL DEFAULT GETUTCDATE(),
        CreatedByExternal   BIT NOT NULL DEFAULT 1,  -- true = clinic did it in Stripe dashboard

        CONSTRAINT FK_PaymentRefunds_Payments FOREIGN KEY (PaymentId)
            REFERENCES Payments(PaymentId),
        CONSTRAINT FK_PaymentRefunds_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId),
        CONSTRAINT UQ_PaymentRefunds_StripeRefundId UNIQUE (StripeRefundId)
    );

    CREATE INDEX IX_PaymentRefunds_PaymentId
        ON PaymentRefunds(PaymentId);

    CREATE INDEX IX_PaymentRefunds_TenantId
        ON PaymentRefunds(TenantId);

    PRINT 'Created PaymentRefunds table';
END
ELSE
BEGIN
    PRINT 'PaymentRefunds table already exists';
END
