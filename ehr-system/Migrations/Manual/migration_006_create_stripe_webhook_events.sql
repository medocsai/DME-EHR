-- ============================================
-- STRIPE CONNECT MIGRATION 006
-- Creates StripeWebhookEvents table
-- Idempotency log + audit trail for all Stripe webhook events
-- Prevents duplicate processing on Stripe webhook retries
-- ============================================

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('StripeWebhookEvents') AND type = 'U')
BEGIN
    CREATE TABLE StripeWebhookEvents (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        StripeEventId       NVARCHAR(255) NOT NULL,
        EventType           NVARCHAR(100) NOT NULL,
        StripeAccountId     NVARCHAR(255) NULL,  -- null = platform event
        Payload             NVARCHAR(MAX) NOT NULL,
        Status              INT NOT NULL DEFAULT 0,  -- 0=Received, 1=Processed, 2=Failed, 3=Ignored
        ErrorMessage        NVARCHAR(MAX) NULL,
        ReceivedAt          DATETIME NOT NULL DEFAULT GETUTCDATE(),
        ProcessedAt         DATETIME NULL,

        CONSTRAINT UQ_StripeWebhookEvents_StripeEventId UNIQUE (StripeEventId)
    );

    CREATE INDEX IX_StripeWebhookEvents_EventType
        ON StripeWebhookEvents(EventType);

    CREATE INDEX IX_StripeWebhookEvents_Status
        ON StripeWebhookEvents(Status);

    CREATE INDEX IX_StripeWebhookEvents_ReceivedAt
        ON StripeWebhookEvents(ReceivedAt DESC);

    CREATE INDEX IX_StripeWebhookEvents_StripeAccountId
        ON StripeWebhookEvents(StripeAccountId);

    PRINT 'Created StripeWebhookEvents table';
END
ELSE
BEGIN
    PRINT 'StripeWebhookEvents table already exists';
END
