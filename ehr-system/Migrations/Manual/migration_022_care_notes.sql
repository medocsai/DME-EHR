-- ============================================================
-- Migration 022: Care Notes
-- ============================================================
-- Adds the CareNotes table. Care Notes are structured nurse->provider
-- communication (e.g. "Patient called about BP", "Pharmacy needs PA").
--
-- Distinct from:
--   PatientStickyNotes  = quick yellow post-its (informal reminders)
--   ClinicalNotes       = formal medical documentation
--
-- Rules (locked 2026-05-01):
--   - Any staff role (Clinician, Nurse, MA, FrontDesk, Admin) can create.
--   - Only the creator (CreatedByUserId) can edit or soft-delete a note.
--   - "For Provider" is optional. When set, that provider gets a top-nav
--     notification (Clinician role only sees the bell).
--   - "Seen" = the assigned provider opened the patient's Care Notes tab.
--     Once seen, SeenByProviderId + SeenAt are stamped and the note no
--     longer appears in the unseen-count / unseen-list endpoints.
--   - Edit history NOT stored (v1). IsEdited + last EditedBy/At only.
--   - Soft delete: IsDeleted flips to 1, row is preserved.
--
-- Content is PHI and is encrypted at rest by EncryptionHelper using
-- EncryptionConfiguration. Do NOT query Content in plaintext from SQL.
-- ============================================================

SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CareNotes' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.CareNotes (
        CareNoteId          int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId            int               NOT NULL,
        PatientId           int               NOT NULL,

        -- Content (PHI, encrypted at rest by EncryptionHelper)
        Content             nvarchar(max)     NOT NULL,

        -- Optional routing target. NULL = general note (no notification).
        ForProviderId       int               NULL,

        -- Authorship
        CreatedByUserId     int               NOT NULL,
        CreatedByName       nvarchar(200)     NOT NULL,
        CreatedAt           datetime2(7)      NOT NULL CONSTRAINT DF_CareNotes_CreatedAt DEFAULT (GETUTCDATE()),

        -- Edit metadata (no full history table in v1)
        IsEdited            bit               NOT NULL CONSTRAINT DF_CareNotes_IsEdited DEFAULT (0),
        EditedByUserId      int               NULL,
        EditedByName        nvarchar(200)     NULL,
        EditedAt            datetime2(7)      NULL,

        -- Seen tracking (provider auto-marks when opening the patient's Care Notes tab)
        SeenByProviderId    int               NULL,
        SeenAt              datetime2(7)      NULL,

        -- Soft delete (creator only)
        IsDeleted           bit               NOT NULL CONSTRAINT DF_CareNotes_IsDeleted DEFAULT (0),
        DeletedByUserId     int               NULL,
        DeletedByName       nvarchar(200)     NULL,
        DeletedAt           datetime2(7)      NULL,

        CONSTRAINT FK_CareNotes_Tenant   FOREIGN KEY (TenantId)  REFERENCES dbo.Tenants(TenantId),
        CONSTRAINT FK_CareNotes_Patient  FOREIGN KEY (PatientId) REFERENCES dbo.Patients(PatientId)
    );
END
GO

-- Patient lookup (Care Notes tab in patient profile)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CareNotes_TenantId_PatientId' AND object_id = OBJECT_ID('dbo.CareNotes'))
BEGIN
    CREATE INDEX IX_CareNotes_TenantId_PatientId
        ON dbo.CareNotes (TenantId, PatientId)
        INCLUDE (CreatedAt, IsDeleted);
END
GO

-- Unseen lookup for provider top-nav (filtered: only unseen, non-deleted, with a target)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CareNotes_ForProvider_Unseen' AND object_id = OBJECT_ID('dbo.CareNotes'))
BEGIN
    CREATE INDEX IX_CareNotes_ForProvider_Unseen
        ON dbo.CareNotes (TenantId, ForProviderId)
        INCLUDE (PatientId, CreatedAt, CreatedByName)
        WHERE IsDeleted = 0 AND SeenByProviderId IS NULL AND ForProviderId IS NOT NULL;
END
GO
