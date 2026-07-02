-- ============================================
-- Patient-Provider Messaging System
-- Run this BEFORE deploying the new code
-- ============================================
SET QUOTED_IDENTIFIER ON;
GO

-- Patient Conversations table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PatientConversations')
BEGIN
    CREATE TABLE [dbo].[PatientConversations] (
        [PatientConversationId] INT IDENTITY(1,1) NOT NULL,
        [TenantId] INT NOT NULL,
        [PatientId] INT NOT NULL,
        [ProviderId] INT NOT NULL,
        [LastMessageText] NVARCHAR(500) NULL,
        [LastMessageAt] DATETIME2 NULL,
        [LastMessageSenderType] NVARCHAR(20) NULL,
        [PatientUnreadCount] INT NOT NULL DEFAULT 0,
        [ProviderUnreadCount] INT NOT NULL DEFAULT 0,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [PK_PatientConversations] PRIMARY KEY CLUSTERED ([PatientConversationId] ASC),
        CONSTRAINT [FK_PatientConversations_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
        CONSTRAINT [FK_PatientConversations_PatientId] FOREIGN KEY ([PatientId]) REFERENCES [dbo].[Patients]([PatientId]),
        CONSTRAINT [FK_PatientConversations_ProviderId] FOREIGN KEY ([ProviderId]) REFERENCES [dbo].[Providers]([ProviderId])
    );

    -- One conversation per patient-provider pair per tenant
    CREATE UNIQUE INDEX [IX_PatientConversations_UniqueParticipants]
        ON [dbo].[PatientConversations] ([TenantId], [PatientId], [ProviderId]);

    -- Index for finding conversations by patient
    CREATE INDEX [IX_PatientConversations_TenantId_PatientId]
        ON [dbo].[PatientConversations] ([TenantId], [PatientId]);

    -- Index for finding conversations by provider
    CREATE INDEX [IX_PatientConversations_TenantId_ProviderId]
        ON [dbo].[PatientConversations] ([TenantId], [ProviderId]);

    PRINT 'Created PatientConversations table';
END
GO

-- Patient Messages table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PatientMessages')
BEGIN
    CREATE TABLE [dbo].[PatientMessages] (
        [PatientMessageId] INT IDENTITY(1,1) NOT NULL,
        [TenantId] INT NOT NULL,
        [PatientConversationId] INT NOT NULL,
        [SenderType] NVARCHAR(20) NOT NULL,
        [SenderPatientId] INT NULL,
        [SenderProviderId] INT NULL,
        [MessageText] NVARCHAR(MAX) NOT NULL,
        [IsReadByPatient] BIT NOT NULL DEFAULT 0,
        [IsReadByProvider] BIT NOT NULL DEFAULT 0,
        [ReadByPatientAt] DATETIME2 NULL,
        [ReadByProviderAt] DATETIME2 NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT (GETUTCDATE()),
        CONSTRAINT [PK_PatientMessages] PRIMARY KEY CLUSTERED ([PatientMessageId] ASC),
        CONSTRAINT [FK_PatientMessages_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([TenantId]),
        CONSTRAINT [FK_PatientMessages_ConversationId] FOREIGN KEY ([PatientConversationId]) REFERENCES [dbo].[PatientConversations]([PatientConversationId]) ON DELETE CASCADE,
        CONSTRAINT [FK_PatientMessages_SenderPatientId] FOREIGN KEY ([SenderPatientId]) REFERENCES [dbo].[Patients]([PatientId]),
        CONSTRAINT [FK_PatientMessages_SenderProviderId] FOREIGN KEY ([SenderProviderId]) REFERENCES [dbo].[Providers]([ProviderId])
    );

    -- Index for fetching messages in a conversation (newest first)
    CREATE INDEX [IX_PatientMessages_ConversationId_CreatedAt]
        ON [dbo].[PatientMessages] ([PatientConversationId], [CreatedAt] DESC);

    -- Index for unread messages by provider
    CREATE INDEX [IX_PatientMessages_UnreadByProvider]
        ON [dbo].[PatientMessages] ([TenantId], [IsReadByProvider])
        WHERE [IsReadByProvider] = 0 AND [SenderType] = 'Patient';

    -- Index for unread messages by patient
    CREATE INDEX [IX_PatientMessages_UnreadByPatient]
        ON [dbo].[PatientMessages] ([TenantId], [IsReadByPatient])
        WHERE [IsReadByPatient] = 0 AND [SenderType] = 'Provider';

    PRINT 'Created PatientMessages table';
END
GO
