-- Migration 020: Create PatientGenderHealths table for intake Section 7
-- (Gender-Specific Health). One row per patient. Biological sex captured at
-- the section level (independent of Demographics.Gender which may be identity).
-- Structured answers stored as JSON for flexibility.
-- Dated 2026-04-24. Idempotent.

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.PatientGenderHealths', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PatientGenderHealths (
        PatientGenderHealthsId INT IDENTITY(1,1) PRIMARY KEY,
        PatientId INT NOT NULL,
        TenantId INT NOT NULL,
        BiologicalSex NVARCHAR(20) NULL,
        StructuredData NVARCHAR(MAX) NULL,
        Source INT NOT NULL DEFAULT 1,
        IntakeSubmissionId INT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_PatientGenderHealths_Patients FOREIGN KEY (PatientId) REFERENCES dbo.Patients(PatientId),
        CONSTRAINT FK_PatientGenderHealths_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(TenantId)
    );
    CREATE UNIQUE INDEX UX_PatientGenderHealths_PatientId ON dbo.PatientGenderHealths(PatientId) WHERE IsDeleted = 0;
END
GO
