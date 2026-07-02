-- =====================================================================
-- 2026-04-25: Add soft-delete columns to History Review tables
-- Spec: rules/technical/history-review-soft-delete.md
--
-- Adds IsDeleted + audit columns (DeletedAt, DeletedByUserId, DeletedReason)
-- to the 7 patient-history tables shown in the encounter History Review step.
--
-- PatientSupplements already has IsDeleted, so it gets only the 3 audit fields.
--
-- Run with:
--   sqlcmd -S localhost -d IMEHR -E -i "<this file>"
-- (sqlcmd -i applies SET QUOTED_IDENTIFIER ON automatically; the explicit SET
-- below is defensive and harmless.)
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
GO

------------------------------------------------------------------
-- 1. PatientAllergies
------------------------------------------------------------------
IF COL_LENGTH('dbo.PatientAllergies', 'IsDeleted') IS NULL
BEGIN
    ALTER TABLE [dbo].[PatientAllergies] ADD
        [IsDeleted]       BIT           NOT NULL CONSTRAINT DF_PatientAllergies_IsDeleted DEFAULT 0,
        [DeletedAt]       DATETIME2     NULL,
        [DeletedByUserId] INT           NULL,
        [DeletedReason]   NVARCHAR(500) NULL;
END
GO

------------------------------------------------------------------
-- 2. PatientMedications
------------------------------------------------------------------
IF COL_LENGTH('dbo.PatientMedications', 'IsDeleted') IS NULL
BEGIN
    ALTER TABLE [dbo].[PatientMedications] ADD
        [IsDeleted]       BIT           NOT NULL CONSTRAINT DF_PatientMedications_IsDeleted DEFAULT 0,
        [DeletedAt]       DATETIME2     NULL,
        [DeletedByUserId] INT           NULL,
        [DeletedReason]   NVARCHAR(500) NULL;
END
GO

------------------------------------------------------------------
-- 3. PatientProblems
------------------------------------------------------------------
IF COL_LENGTH('dbo.PatientProblems', 'IsDeleted') IS NULL
BEGIN
    ALTER TABLE [dbo].[PatientProblems] ADD
        [IsDeleted]       BIT           NOT NULL CONSTRAINT DF_PatientProblems_IsDeleted DEFAULT 0,
        [DeletedAt]       DATETIME2     NULL,
        [DeletedByUserId] INT           NULL,
        [DeletedReason]   NVARCHAR(500) NULL;
END
GO

------------------------------------------------------------------
-- 4. PatientFamilyHistories
------------------------------------------------------------------
IF COL_LENGTH('dbo.PatientFamilyHistories', 'IsDeleted') IS NULL
BEGIN
    ALTER TABLE [dbo].[PatientFamilyHistories] ADD
        [IsDeleted]       BIT           NOT NULL CONSTRAINT DF_PatientFamilyHistories_IsDeleted DEFAULT 0,
        [DeletedAt]       DATETIME2     NULL,
        [DeletedByUserId] INT           NULL,
        [DeletedReason]   NVARCHAR(500) NULL;
END
GO

------------------------------------------------------------------
-- 5. PatientSocialHistories
------------------------------------------------------------------
IF COL_LENGTH('dbo.PatientSocialHistories', 'IsDeleted') IS NULL
BEGIN
    ALTER TABLE [dbo].[PatientSocialHistories] ADD
        [IsDeleted]       BIT           NOT NULL CONSTRAINT DF_PatientSocialHistories_IsDeleted DEFAULT 0,
        [DeletedAt]       DATETIME2     NULL,
        [DeletedByUserId] INT           NULL,
        [DeletedReason]   NVARCHAR(500) NULL;
END
GO

------------------------------------------------------------------
-- 6. PatientImmunizations
------------------------------------------------------------------
IF COL_LENGTH('dbo.PatientImmunizations', 'IsDeleted') IS NULL
BEGIN
    ALTER TABLE [dbo].[PatientImmunizations] ADD
        [IsDeleted]       BIT           NOT NULL CONSTRAINT DF_PatientImmunizations_IsDeleted DEFAULT 0,
        [DeletedAt]       DATETIME2     NULL,
        [DeletedByUserId] INT           NULL,
        [DeletedReason]   NVARCHAR(500) NULL;
END
GO

------------------------------------------------------------------
-- 7. PatientSupplements (already has IsDeleted, just add 3 audit fields)
------------------------------------------------------------------
IF COL_LENGTH('dbo.PatientSupplements', 'DeletedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[PatientSupplements] ADD
        [DeletedAt]       DATETIME2     NULL,
        [DeletedByUserId] INT           NULL,
        [DeletedReason]   NVARCHAR(500) NULL;
END
GO

------------------------------------------------------------------
-- Foreign keys for DeletedByUserId (Users.UserId)
------------------------------------------------------------------
IF OBJECT_ID('FK_PatientAllergies_DeletedByUser', 'F') IS NULL
    ALTER TABLE [dbo].[PatientAllergies] ADD CONSTRAINT FK_PatientAllergies_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES [dbo].[Users](UserId);
GO
IF OBJECT_ID('FK_PatientMedications_DeletedByUser', 'F') IS NULL
    ALTER TABLE [dbo].[PatientMedications] ADD CONSTRAINT FK_PatientMedications_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES [dbo].[Users](UserId);
GO
IF OBJECT_ID('FK_PatientProblems_DeletedByUser', 'F') IS NULL
    ALTER TABLE [dbo].[PatientProblems] ADD CONSTRAINT FK_PatientProblems_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES [dbo].[Users](UserId);
GO
IF OBJECT_ID('FK_PatientFamilyHistories_DeletedByUser', 'F') IS NULL
    ALTER TABLE [dbo].[PatientFamilyHistories] ADD CONSTRAINT FK_PatientFamilyHistories_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES [dbo].[Users](UserId);
GO
IF OBJECT_ID('FK_PatientSocialHistories_DeletedByUser', 'F') IS NULL
    ALTER TABLE [dbo].[PatientSocialHistories] ADD CONSTRAINT FK_PatientSocialHistories_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES [dbo].[Users](UserId);
GO
IF OBJECT_ID('FK_PatientImmunizations_DeletedByUser', 'F') IS NULL
    ALTER TABLE [dbo].[PatientImmunizations] ADD CONSTRAINT FK_PatientImmunizations_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES [dbo].[Users](UserId);
GO
IF OBJECT_ID('FK_PatientSupplements_DeletedByUser', 'F') IS NULL
    ALTER TABLE [dbo].[PatientSupplements] ADD CONSTRAINT FK_PatientSupplements_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES [dbo].[Users](UserId);
GO

------------------------------------------------------------------
-- Filtered indexes for the most common read path (WHERE IsDeleted = 0)
------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientAllergies_NotDeleted' AND object_id = OBJECT_ID('dbo.PatientAllergies'))
    CREATE INDEX IX_PatientAllergies_NotDeleted ON [dbo].[PatientAllergies](PatientId) WHERE IsDeleted = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientMedications_NotDeleted' AND object_id = OBJECT_ID('dbo.PatientMedications'))
    CREATE INDEX IX_PatientMedications_NotDeleted ON [dbo].[PatientMedications](PatientId) WHERE IsDeleted = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientProblems_NotDeleted' AND object_id = OBJECT_ID('dbo.PatientProblems'))
    CREATE INDEX IX_PatientProblems_NotDeleted ON [dbo].[PatientProblems](PatientId) WHERE IsDeleted = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientFamilyHistories_NotDeleted' AND object_id = OBJECT_ID('dbo.PatientFamilyHistories'))
    CREATE INDEX IX_PatientFamilyHistories_NotDeleted ON [dbo].[PatientFamilyHistories](PatientId) WHERE IsDeleted = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientSocialHistories_NotDeleted' AND object_id = OBJECT_ID('dbo.PatientSocialHistories'))
    CREATE INDEX IX_PatientSocialHistories_NotDeleted ON [dbo].[PatientSocialHistories](PatientId) WHERE IsDeleted = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientImmunizations_NotDeleted' AND object_id = OBJECT_ID('dbo.PatientImmunizations'))
    CREATE INDEX IX_PatientImmunizations_NotDeleted ON [dbo].[PatientImmunizations](PatientId) WHERE IsDeleted = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientSupplements_NotDeleted' AND object_id = OBJECT_ID('dbo.PatientSupplements'))
    CREATE INDEX IX_PatientSupplements_NotDeleted ON [dbo].[PatientSupplements](PatientId) WHERE IsDeleted = 0;
GO

PRINT 'Migration complete: AddSoftDeleteToHistoryReviewTables';
GO
