-- Migration 016: Clinical Note Amendment & Addendum
-- Creates two new tables for versioned note amendments and appended addendums.
-- No changes to existing tables; original ClinicalNote row remains the immutable v1.
--
-- Run with:
--   sqlcmd -S localhost -d IMEHR -E -Q "SET QUOTED_IDENTIFIER ON" -i migration_016_amendment_addendum.sql

SET QUOTED_IDENTIFIER ON;
GO

-- ============================================
-- Step 1: Create ClinicalNoteAmendments
-- Each row is a full-content revision of an existing signed ClinicalNote.
-- The original note stays untouched (v1); amendments are v2, v3, ...
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ClinicalNoteAmendments')
BEGIN
    CREATE TABLE ClinicalNoteAmendments
    (
        AmendmentId      INT IDENTITY(1,1) NOT NULL,
        ClinicalNoteId   INT NOT NULL,
        TenantId         INT NOT NULL,
        VersionNumber    INT NOT NULL,
        HtmlContent      NVARCHAR(MAX) NOT NULL,       -- encrypted at rest (same helper as ClinicalNote.HtmlContent)
        Reason           NVARCHAR(500) NOT NULL,
        SignedAt         DATETIME2 NOT NULL,
        SignedByUserId   INT NOT NULL,
        SignatureData    NVARCHAR(MAX) NULL,           -- base64 signature image
        CreatedAt        DATETIME2 NOT NULL CONSTRAINT DF_ClinicalNoteAmendments_CreatedAt DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_ClinicalNoteAmendments PRIMARY KEY CLUSTERED (AmendmentId),
        CONSTRAINT FK_ClinicalNoteAmendments_ClinicalNotes FOREIGN KEY (ClinicalNoteId)
            REFERENCES ClinicalNotes (ClinicalNoteId) ON DELETE CASCADE,
        CONSTRAINT FK_ClinicalNoteAmendments_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (TenantId),
        CONSTRAINT FK_ClinicalNoteAmendments_Users FOREIGN KEY (SignedByUserId)
            REFERENCES Users (UserId),
        CONSTRAINT UX_ClinicalNoteAmendments_NoteVersion UNIQUE (ClinicalNoteId, VersionNumber)
    );

    CREATE INDEX IX_ClinicalNoteAmendments_ClinicalNoteId
        ON ClinicalNoteAmendments (ClinicalNoteId, VersionNumber);
    CREATE INDEX IX_ClinicalNoteAmendments_TenantId
        ON ClinicalNoteAmendments (TenantId);

    PRINT 'Created table ClinicalNoteAmendments.';
END
ELSE
BEGIN
    PRINT 'Table ClinicalNoteAmendments already exists — skipped.';
END
GO

-- ============================================
-- Step 2: Create ClinicalNoteAddendums
-- Addendum = supplementary text appended to a signed note. Append-only.
-- Always allowed (no window restriction). Never affects codes or claim.
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ClinicalNoteAddendums')
BEGIN
    CREATE TABLE ClinicalNoteAddendums
    (
        AddendumId       INT IDENTITY(1,1) NOT NULL,
        ClinicalNoteId   INT NOT NULL,
        TenantId         INT NOT NULL,
        Content          NVARCHAR(MAX) NOT NULL,       -- encrypted at rest
        Reason           NVARCHAR(500) NOT NULL,
        SignedAt         DATETIME2 NOT NULL,
        SignedByUserId   INT NOT NULL,
        SignatureData    NVARCHAR(MAX) NULL,
        CreatedAt        DATETIME2 NOT NULL CONSTRAINT DF_ClinicalNoteAddendums_CreatedAt DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_ClinicalNoteAddendums PRIMARY KEY CLUSTERED (AddendumId),
        CONSTRAINT FK_ClinicalNoteAddendums_ClinicalNotes FOREIGN KEY (ClinicalNoteId)
            REFERENCES ClinicalNotes (ClinicalNoteId) ON DELETE CASCADE,
        CONSTRAINT FK_ClinicalNoteAddendums_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (TenantId),
        CONSTRAINT FK_ClinicalNoteAddendums_Users FOREIGN KEY (SignedByUserId)
            REFERENCES Users (UserId)
    );

    CREATE INDEX IX_ClinicalNoteAddendums_ClinicalNoteId
        ON ClinicalNoteAddendums (ClinicalNoteId, CreatedAt);
    CREATE INDEX IX_ClinicalNoteAddendums_TenantId
        ON ClinicalNoteAddendums (TenantId);

    PRINT 'Created table ClinicalNoteAddendums.';
END
ELSE
BEGIN
    PRINT 'Table ClinicalNoteAddendums already exists — skipped.';
END
GO

PRINT 'Migration 016 complete: ClinicalNoteAmendments + ClinicalNoteAddendums created.';
GO
