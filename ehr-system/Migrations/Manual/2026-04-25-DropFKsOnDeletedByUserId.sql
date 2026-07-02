-- =====================================================================
-- 2026-04-25: Drop FK constraints on DeletedByUserId for History Review tables
--
-- The FK on DeletedByUserId enforces that the column references a real Users.UserId.
-- This causes SQL error 547 in cases where the JWT issued before AuthService was
-- updated to include a "UserId" claim, leaving the column written as 0 (which has
-- no matching row).
--
-- Existing patient-history tables (e.g. CreatedByUserId) do NOT have FKs to Users,
-- so we are aligning with that pattern for consistency. Audit log captures who
-- performed each action; the DeletedByUserId column is data-only.
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('FK_PatientAllergies_DeletedByUser', 'F') IS NOT NULL
    ALTER TABLE [dbo].[PatientAllergies] DROP CONSTRAINT FK_PatientAllergies_DeletedByUser;
GO
IF OBJECT_ID('FK_PatientMedications_DeletedByUser', 'F') IS NOT NULL
    ALTER TABLE [dbo].[PatientMedications] DROP CONSTRAINT FK_PatientMedications_DeletedByUser;
GO
IF OBJECT_ID('FK_PatientProblems_DeletedByUser', 'F') IS NOT NULL
    ALTER TABLE [dbo].[PatientProblems] DROP CONSTRAINT FK_PatientProblems_DeletedByUser;
GO
IF OBJECT_ID('FK_PatientFamilyHistories_DeletedByUser', 'F') IS NOT NULL
    ALTER TABLE [dbo].[PatientFamilyHistories] DROP CONSTRAINT FK_PatientFamilyHistories_DeletedByUser;
GO
IF OBJECT_ID('FK_PatientSocialHistories_DeletedByUser', 'F') IS NOT NULL
    ALTER TABLE [dbo].[PatientSocialHistories] DROP CONSTRAINT FK_PatientSocialHistories_DeletedByUser;
GO
IF OBJECT_ID('FK_PatientImmunizations_DeletedByUser', 'F') IS NOT NULL
    ALTER TABLE [dbo].[PatientImmunizations] DROP CONSTRAINT FK_PatientImmunizations_DeletedByUser;
GO
IF OBJECT_ID('FK_PatientSupplements_DeletedByUser', 'F') IS NOT NULL
    ALTER TABLE [dbo].[PatientSupplements] DROP CONSTRAINT FK_PatientSupplements_DeletedByUser;
GO

PRINT 'Migration complete: DropFKsOnDeletedByUserId';
GO
