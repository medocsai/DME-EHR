-- ============================================================================
-- Migration 024: PatientIntakeSubmissions.CancerSpecify + LifestyleData
-- ============================================================================
-- The C# model PatientIntakeSubmission.cs declares these properties (CancerSpecify
-- on line 77, LifestyleData on line 84) and IntakePrefillService / IntakeSubmissionService
-- read+write them, but no prior migration ever created the columns. Any return-visit
-- intake prefill threw SqlException 207 ("Invalid column name") on production.
--
-- CancerSpecify  — Section 3 free-text: cancer types and years (NVARCHAR(1000)).
-- LifestyleData  — Section 5 structured choices stored as JSON (NVARCHAR(MAX)).
--
-- Run with:
--   sqlcmd -S localhost -d IMEHR -E -I -i migration_024_intake_cancer_lifestyle.sql
--
-- Idempotent — guarded by COL_LENGTH; safe to re-run.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.PatientIntakeSubmissions', 'CancerSpecify') IS NULL
BEGIN
    ALTER TABLE dbo.PatientIntakeSubmissions ADD CancerSpecify NVARCHAR(1000) NULL;
    PRINT 'Added PatientIntakeSubmissions.CancerSpecify';
END
ELSE
BEGIN
    PRINT 'PatientIntakeSubmissions.CancerSpecify already exists, skipping';
END
GO

IF COL_LENGTH('dbo.PatientIntakeSubmissions', 'LifestyleData') IS NULL
BEGIN
    ALTER TABLE dbo.PatientIntakeSubmissions ADD LifestyleData NVARCHAR(MAX) NULL;
    PRINT 'Added PatientIntakeSubmissions.LifestyleData';
END
ELSE
BEGIN
    PRINT 'PatientIntakeSubmissions.LifestyleData already exists, skipping';
END
GO

PRINT 'Migration 024 complete: Intake cancer + lifestyle columns in place';
GO
