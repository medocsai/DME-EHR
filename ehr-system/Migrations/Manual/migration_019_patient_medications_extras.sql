-- Migration 019: Add 4 patient-level fields for the Medications & Supplements
-- section of the intake form, per AtomicMedX PDF:
--   Drug Reaction or Adverse Medication History (free text)
--   Primary Pharmacy & Phone (single line)
--   Compounding / Specialty Pharmacy & Phone (single line)
--   Current Healthcare Team (PCP, specialists, etc. — free text)
--
-- These persist across intake sessions (they're patient-level attributes),
-- so they live on Patient, not PatientIntakeSubmission.
-- Dated 2026-04-24. Idempotent.

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.Patients', 'DrugReactionHistory') IS NULL
BEGIN
    ALTER TABLE dbo.Patients ADD DrugReactionHistory NVARCHAR(MAX) NULL;
END
GO

IF COL_LENGTH('dbo.Patients', 'PrimaryPharmacyInfo') IS NULL
BEGIN
    ALTER TABLE dbo.Patients ADD PrimaryPharmacyInfo NVARCHAR(500) NULL;
END
GO

IF COL_LENGTH('dbo.Patients', 'CompoundingPharmacyInfo') IS NULL
BEGIN
    ALTER TABLE dbo.Patients ADD CompoundingPharmacyInfo NVARCHAR(500) NULL;
END
GO

IF COL_LENGTH('dbo.Patients', 'HealthcareTeamNotes') IS NULL
BEGIN
    ALTER TABLE dbo.Patients ADD HealthcareTeamNotes NVARCHAR(MAX) NULL;
END
GO
