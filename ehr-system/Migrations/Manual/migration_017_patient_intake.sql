-- Migration 017: Patient Intake Form (Phase 1 - Backend)
-- Adds Source/IntakeSubmissionId provenance columns to 6 existing clinical tables,
-- Patient.IntakePortalToken, Location.EnableLongevity, and creates 5 new tables:
--   PatientHealthConcerns, PatientSupplements, PatientLongevityProfiles,
--   PatientIntakeSubmissions, IntakeVerificationAttempts.
--
-- Run with:
--   sqlcmd -S localhost -d IMEHR -E -Q "SET QUOTED_IDENTIFIER ON" -i migration_017_patient_intake.sql

SET QUOTED_IDENTIFIER ON;
GO

-- ============================================
-- Step 1: Add IntakePortalToken to Patient
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Patients') AND name = 'IntakePortalToken')
BEGIN
    ALTER TABLE Patients
        ADD IntakePortalToken UNIQUEIDENTIFIER NULL;
    PRINT 'Added Patients.IntakePortalToken.';
END
ELSE
BEGIN
    PRINT 'Patients.IntakePortalToken already exists — skipped.';
END
GO

-- ============================================
-- Step 2: Add EnableLongevity to Location
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Locations') AND name = 'EnableLongevity')
BEGIN
    ALTER TABLE Locations
        ADD EnableLongevity BIT NOT NULL CONSTRAINT DF_Locations_EnableLongevity DEFAULT (0);
    PRINT 'Added Locations.EnableLongevity.';
END
ELSE
BEGIN
    PRINT 'Locations.EnableLongevity already exists — skipped.';
END
GO

-- ============================================
-- Step 3: Create PatientIntakeSubmissions (first so FKs can reference it)
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PatientIntakeSubmissions')
BEGIN
    CREATE TABLE PatientIntakeSubmissions
    (
        PatientIntakeSubmissionId INT IDENTITY(1,1) NOT NULL,
        PatientId                 INT NOT NULL,
        TenantId                  INT NOT NULL,
        StartedAt                 DATETIME2 NOT NULL CONSTRAINT DF_PatientIntakeSubmissions_StartedAt DEFAULT (GETUTCDATE()),
        SubmittedAt               DATETIME2 NULL,
        SectionsTouched           NVARCHAR(500) NULL,   -- JSON array, e.g., ["demographics","concerns"]
        SourceChannel             INT NOT NULL CONSTRAINT DF_PatientIntakeSubmissions_SourceChannel DEFAULT (0),  -- 0=Portal, 1=Tablet
        IpAddress                 NVARCHAR(45) NULL,
        UserAgent                 NVARCHAR(500) NULL,
        CreatedAt                 DATETIME2 NOT NULL CONSTRAINT DF_PatientIntakeSubmissions_CreatedAt DEFAULT (GETUTCDATE()),
        UpdatedAt                 DATETIME2 NULL,
        IsDeleted                 BIT NOT NULL CONSTRAINT DF_PatientIntakeSubmissions_IsDeleted DEFAULT (0),
        CONSTRAINT PK_PatientIntakeSubmissions PRIMARY KEY CLUSTERED (PatientIntakeSubmissionId),
        CONSTRAINT FK_PatientIntakeSubmissions_Patients FOREIGN KEY (PatientId)
            REFERENCES Patients (PatientId),
        CONSTRAINT FK_PatientIntakeSubmissions_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (TenantId)
    );

    CREATE INDEX IX_PatientIntakeSubmissions_PatientId
        ON PatientIntakeSubmissions (PatientId, StartedAt DESC);
    CREATE INDEX IX_PatientIntakeSubmissions_TenantId
        ON PatientIntakeSubmissions (TenantId);

    PRINT 'Created table PatientIntakeSubmissions.';
END
ELSE
BEGIN
    PRINT 'Table PatientIntakeSubmissions already exists — skipped.';
END
GO

-- ============================================
-- Step 4: Create PatientHealthConcerns
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PatientHealthConcerns')
BEGIN
    CREATE TABLE PatientHealthConcerns
    (
        PatientHealthConcernId INT IDENTITY(1,1) NOT NULL,
        PatientId              INT NOT NULL,
        TenantId               INT NOT NULL,
        Priority               INT NOT NULL,                 -- 1..5 patient ranking
        Concern                NVARCHAR(200) NOT NULL,
        Details                NVARCHAR(2000) NULL,          -- onset/frequency/severity/better-worse
        Severity               INT NULL,                     -- 0=mild, 1=moderate, 2=severe
        Source                 INT NOT NULL CONSTRAINT DF_PatientHealthConcerns_Source DEFAULT (1),
        IntakeSubmissionId     INT NULL,
        CreatedAt              DATETIME2 NOT NULL CONSTRAINT DF_PatientHealthConcerns_CreatedAt DEFAULT (GETUTCDATE()),
        UpdatedAt              DATETIME2 NULL,
        IsDeleted              BIT NOT NULL CONSTRAINT DF_PatientHealthConcerns_IsDeleted DEFAULT (0),
        CONSTRAINT PK_PatientHealthConcerns PRIMARY KEY CLUSTERED (PatientHealthConcernId),
        CONSTRAINT FK_PatientHealthConcerns_Patients FOREIGN KEY (PatientId)
            REFERENCES Patients (PatientId),
        CONSTRAINT FK_PatientHealthConcerns_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (TenantId),
        CONSTRAINT FK_PatientHealthConcerns_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId)
    );

    CREATE INDEX IX_PatientHealthConcerns_PatientId
        ON PatientHealthConcerns (TenantId, PatientId, Priority);

    PRINT 'Created table PatientHealthConcerns.';
END
ELSE
BEGIN
    PRINT 'Table PatientHealthConcerns already exists — skipped.';
END
GO

-- ============================================
-- Step 5: Create PatientSupplements
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PatientSupplements')
BEGIN
    CREATE TABLE PatientSupplements
    (
        PatientSupplementId INT IDENTITY(1,1) NOT NULL,
        PatientId           INT NOT NULL,
        TenantId            INT NOT NULL,
        SupplementName      NVARCHAR(200) NOT NULL,
        Notes               NVARCHAR(1000) NULL,           -- brand, dose, frequency, duration
        IsActive            BIT NOT NULL CONSTRAINT DF_PatientSupplements_IsActive DEFAULT (1),
        Source              INT NOT NULL CONSTRAINT DF_PatientSupplements_Source DEFAULT (1),
        IntakeSubmissionId  INT NULL,
        CreatedAt           DATETIME2 NOT NULL CONSTRAINT DF_PatientSupplements_CreatedAt DEFAULT (GETUTCDATE()),
        UpdatedAt           DATETIME2 NULL,
        IsDeleted           BIT NOT NULL CONSTRAINT DF_PatientSupplements_IsDeleted DEFAULT (0),
        CONSTRAINT PK_PatientSupplements PRIMARY KEY CLUSTERED (PatientSupplementId),
        CONSTRAINT FK_PatientSupplements_Patients FOREIGN KEY (PatientId)
            REFERENCES Patients (PatientId),
        CONSTRAINT FK_PatientSupplements_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (TenantId),
        CONSTRAINT FK_PatientSupplements_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId)
    );

    CREATE INDEX IX_PatientSupplements_PatientId
        ON PatientSupplements (TenantId, PatientId);

    PRINT 'Created table PatientSupplements.';
END
ELSE
BEGIN
    PRINT 'Table PatientSupplements already exists — skipped.';
END
GO

-- ============================================
-- Step 6: Create PatientLongevityProfiles (one row per patient)
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PatientLongevityProfiles')
BEGIN
    CREATE TABLE PatientLongevityProfiles
    (
        PatientLongevityProfileId INT IDENTITY(1,1) NOT NULL,
        PatientId                 INT NOT NULL,
        TenantId                  INT NOT NULL,
        SymptomRatings            NVARCHAR(MAX) NULL,    -- JSON: {"brainFog":7,"fatigue":8,...}
        Goals                     NVARCHAR(MAX) NULL,    -- JSON: ["extendLifespan",...]
        PriorTesting              NVARCHAR(MAX) NULL,    -- JSON: ["23andMe",...]
        CurrentInterventions      NVARCHAR(MAX) NULL,    -- JSON: ["Metformin","NMN",...]
        ToxinExposure             NVARCHAR(MAX) NULL,    -- JSON: {"mold":true,...}
        BiomarkerGoals            NVARCHAR(1000) NULL,
        OptimalHealthVision       NVARCHAR(1000) NULL,
        Source                    INT NOT NULL CONSTRAINT DF_PatientLongevityProfiles_Source DEFAULT (1),
        IntakeSubmissionId        INT NULL,
        CreatedAt                 DATETIME2 NOT NULL CONSTRAINT DF_PatientLongevityProfiles_CreatedAt DEFAULT (GETUTCDATE()),
        UpdatedAt                 DATETIME2 NULL,
        IsDeleted                 BIT NOT NULL CONSTRAINT DF_PatientLongevityProfiles_IsDeleted DEFAULT (0),
        CONSTRAINT PK_PatientLongevityProfiles PRIMARY KEY CLUSTERED (PatientLongevityProfileId),
        CONSTRAINT UX_PatientLongevityProfiles_PatientId UNIQUE (PatientId),
        CONSTRAINT FK_PatientLongevityProfiles_Patients FOREIGN KEY (PatientId)
            REFERENCES Patients (PatientId),
        CONSTRAINT FK_PatientLongevityProfiles_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (TenantId),
        CONSTRAINT FK_PatientLongevityProfiles_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId)
    );

    PRINT 'Created table PatientLongevityProfiles.';
END
ELSE
BEGIN
    PRINT 'Table PatientLongevityProfiles already exists — skipped.';
END
GO

-- ============================================
-- Step 7: Create IntakeVerificationAttempts (rate-limit log)
-- Mirrors KioskVerificationAttempts but parallel (v1 decision per rules).
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IntakeVerificationAttempts')
BEGIN
    CREATE TABLE IntakeVerificationAttempts
    (
        IntakeVerificationAttemptId INT IDENTITY(1,1) NOT NULL,
        TenantId                    INT NOT NULL,
        LocationId                  INT NOT NULL,
        PatientId                   INT NULL,
        AttemptedDob                DATE NULL,
        AttemptedSsnLast4Hash       NVARCHAR(128) NULL,
        IsSuccessful                BIT NOT NULL CONSTRAINT DF_IntakeVerificationAttempts_IsSuccessful DEFAULT (0),
        IpAddress                   NVARCHAR(45) NULL,
        UserAgent                   NVARCHAR(500) NULL,
        AttemptedAt                 DATETIME2 NOT NULL CONSTRAINT DF_IntakeVerificationAttempts_AttemptedAt DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_IntakeVerificationAttempts PRIMARY KEY CLUSTERED (IntakeVerificationAttemptId),
        CONSTRAINT FK_IntakeVerificationAttempts_Tenants FOREIGN KEY (TenantId)
            REFERENCES Tenants (TenantId),
        CONSTRAINT FK_IntakeVerificationAttempts_Locations FOREIGN KEY (LocationId)
            REFERENCES Locations (LocationId),
        CONSTRAINT FK_IntakeVerificationAttempts_Patients FOREIGN KEY (PatientId)
            REFERENCES Patients (PatientId)
    );

    CREATE INDEX IX_IntakeVerificationAttempts_RateLimit
        ON IntakeVerificationAttempts (TenantId, LocationId, IpAddress, AttemptedAt DESC);

    PRINT 'Created table IntakeVerificationAttempts.';
END
ELSE
BEGIN
    PRINT 'Table IntakeVerificationAttempts already exists — skipped.';
END
GO

-- ============================================
-- Step 8: Add Source + IntakeSubmissionId to 6 existing clinical tables
-- Existing rows default to Source=1 (Clinic) via DEFAULT constraint.
-- ============================================

-- PatientAllergies
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientAllergies') AND name = 'Source')
BEGIN
    ALTER TABLE PatientAllergies
        ADD Source INT NOT NULL CONSTRAINT DF_PatientAllergies_Source DEFAULT (1);
    PRINT 'Added PatientAllergies.Source.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientAllergies') AND name = 'IntakeSubmissionId')
BEGIN
    ALTER TABLE PatientAllergies
        ADD IntakeSubmissionId INT NULL;
    PRINT 'Added PatientAllergies.IntakeSubmissionId.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientAllergies_IntakeSubmissions')
BEGIN
    ALTER TABLE PatientAllergies
        ADD CONSTRAINT FK_PatientAllergies_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId);
    PRINT 'Added FK_PatientAllergies_IntakeSubmissions.';
END
GO

-- PatientMedications
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientMedications') AND name = 'Source')
BEGIN
    ALTER TABLE PatientMedications
        ADD Source INT NOT NULL CONSTRAINT DF_PatientMedications_Source DEFAULT (1);
    PRINT 'Added PatientMedications.Source.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientMedications') AND name = 'IntakeSubmissionId')
BEGIN
    ALTER TABLE PatientMedications
        ADD IntakeSubmissionId INT NULL;
    PRINT 'Added PatientMedications.IntakeSubmissionId.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientMedications_IntakeSubmissions')
BEGIN
    ALTER TABLE PatientMedications
        ADD CONSTRAINT FK_PatientMedications_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId);
    PRINT 'Added FK_PatientMedications_IntakeSubmissions.';
END
GO

-- PatientProblems
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientProblems') AND name = 'Source')
BEGIN
    ALTER TABLE PatientProblems
        ADD Source INT NOT NULL CONSTRAINT DF_PatientProblems_Source DEFAULT (1);
    PRINT 'Added PatientProblems.Source.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientProblems') AND name = 'IntakeSubmissionId')
BEGIN
    ALTER TABLE PatientProblems
        ADD IntakeSubmissionId INT NULL;
    PRINT 'Added PatientProblems.IntakeSubmissionId.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientProblems_IntakeSubmissions')
BEGIN
    ALTER TABLE PatientProblems
        ADD CONSTRAINT FK_PatientProblems_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId);
    PRINT 'Added FK_PatientProblems_IntakeSubmissions.';
END
GO

-- PatientFamilyHistories
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientFamilyHistories') AND name = 'Source')
BEGIN
    ALTER TABLE PatientFamilyHistories
        ADD Source INT NOT NULL CONSTRAINT DF_PatientFamilyHistories_Source DEFAULT (1);
    PRINT 'Added PatientFamilyHistories.Source.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientFamilyHistories') AND name = 'IntakeSubmissionId')
BEGIN
    ALTER TABLE PatientFamilyHistories
        ADD IntakeSubmissionId INT NULL;
    PRINT 'Added PatientFamilyHistories.IntakeSubmissionId.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientFamilyHistories_IntakeSubmissions')
BEGIN
    ALTER TABLE PatientFamilyHistories
        ADD CONSTRAINT FK_PatientFamilyHistories_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId);
    PRINT 'Added FK_PatientFamilyHistories_IntakeSubmissions.';
END
GO

-- PatientSocialHistories
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientSocialHistories') AND name = 'Source')
BEGIN
    ALTER TABLE PatientSocialHistories
        ADD Source INT NOT NULL CONSTRAINT DF_PatientSocialHistories_Source DEFAULT (1);
    PRINT 'Added PatientSocialHistories.Source.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientSocialHistories') AND name = 'IntakeSubmissionId')
BEGIN
    ALTER TABLE PatientSocialHistories
        ADD IntakeSubmissionId INT NULL;
    PRINT 'Added PatientSocialHistories.IntakeSubmissionId.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientSocialHistories_IntakeSubmissions')
BEGIN
    ALTER TABLE PatientSocialHistories
        ADD CONSTRAINT FK_PatientSocialHistories_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId);
    PRINT 'Added FK_PatientSocialHistories_IntakeSubmissions.';
END
GO

-- PatientImmunizations
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientImmunizations') AND name = 'Source')
BEGIN
    ALTER TABLE PatientImmunizations
        ADD Source INT NOT NULL CONSTRAINT DF_PatientImmunizations_Source DEFAULT (1);
    PRINT 'Added PatientImmunizations.Source.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientImmunizations') AND name = 'IntakeSubmissionId')
BEGIN
    ALTER TABLE PatientImmunizations
        ADD IntakeSubmissionId INT NULL;
    PRINT 'Added PatientImmunizations.IntakeSubmissionId.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_PatientImmunizations_IntakeSubmissions')
BEGIN
    ALTER TABLE PatientImmunizations
        ADD CONSTRAINT FK_PatientImmunizations_IntakeSubmissions FOREIGN KEY (IntakeSubmissionId)
            REFERENCES PatientIntakeSubmissions (PatientIntakeSubmissionId);
    PRINT 'Added FK_PatientImmunizations_IntakeSubmissions.';
END
GO

PRINT 'Migration 017 complete: Patient Intake backend schema in place.';
GO
