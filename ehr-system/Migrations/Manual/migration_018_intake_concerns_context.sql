-- Migration 018: Add Section 2 (Health Concerns) free-text context fields
-- to PatientIntakeSubmission. These capture patient's section-level answers
-- that aren't per-concern: "When did you last feel well?", "What triggered
-- your health change?", "What makes symptoms better / worse?", "Additional
-- health history & timeline".
--
-- Dated 2026-04-24. Idempotent (safe to run multiple times).

SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.PatientIntakeSubmissions', 'LastFeltWell') IS NULL
BEGIN
    ALTER TABLE dbo.PatientIntakeSubmissions ADD LastFeltWell NVARCHAR(1000) NULL;
END
GO

IF COL_LENGTH('dbo.PatientIntakeSubmissions', 'WhatTriggered') IS NULL
BEGIN
    ALTER TABLE dbo.PatientIntakeSubmissions ADD WhatTriggered NVARCHAR(1000) NULL;
END
GO

IF COL_LENGTH('dbo.PatientIntakeSubmissions', 'BetterFactors') IS NULL
BEGIN
    ALTER TABLE dbo.PatientIntakeSubmissions ADD BetterFactors NVARCHAR(1000) NULL;
END
GO

IF COL_LENGTH('dbo.PatientIntakeSubmissions', 'WorseFactors') IS NULL
BEGIN
    ALTER TABLE dbo.PatientIntakeSubmissions ADD WorseFactors NVARCHAR(1000) NULL;
END
GO

IF COL_LENGTH('dbo.PatientIntakeSubmissions', 'AdditionalTimeline') IS NULL
BEGIN
    ALTER TABLE dbo.PatientIntakeSubmissions ADD AdditionalTimeline NVARCHAR(MAX) NULL;
END
GO
