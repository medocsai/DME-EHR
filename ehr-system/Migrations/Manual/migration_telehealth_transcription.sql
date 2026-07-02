-- ============================================
-- TELEHEALTH TRANSCRIPTION PERSISTENCE
-- Stores transcription chunks from telehealth AI Scribe
-- so they survive page navigations and browser restarts
-- ============================================

-- 1. Create TelehealthTranscriptionChunks table
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('TelehealthTranscriptionChunks') AND type = 'U')
BEGIN
    CREATE TABLE TelehealthTranscriptionChunks (
        ChunkId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId INT NOT NULL,
        EncounterId INT NOT NULL,
        SequenceNumber INT NOT NULL,
        TranscriptionText NVARCHAR(MAX) NOT NULL,  -- Encrypted PHI
        DurationSeconds DECIMAL(10, 2) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT FK_TelehealthTranscriptionChunks_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants(TenantId),
        CONSTRAINT FK_TelehealthTranscriptionChunks_Encounters FOREIGN KEY (EncounterId)
            REFERENCES Encounters(EncounterId)
    );

    CREATE INDEX IX_TelehealthTranscriptionChunks_EncounterId
        ON TelehealthTranscriptionChunks(EncounterId);

    CREATE INDEX IX_TelehealthTranscriptionChunks_TenantId_EncounterId
        ON TelehealthTranscriptionChunks(TenantId, EncounterId);

    PRINT 'Created TelehealthTranscriptionChunks table';
END

PRINT 'Telehealth transcription migration complete';
