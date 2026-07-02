-- Migration: Add EncounterId column to all 6 patient history tables
-- Purpose: Track which encounter each history item was added in (This Visit vs Previous Records)
-- Date: 2026-04-09

-- PatientAllergies
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientAllergies') AND name = 'EncounterId')
BEGIN
    ALTER TABLE PatientAllergies ADD EncounterId INT NULL;
    ALTER TABLE PatientAllergies ADD CONSTRAINT FK_PatientAllergies_Encounters FOREIGN KEY (EncounterId) REFERENCES Encounters(EncounterId);
END
GO

-- PatientMedications
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientMedications') AND name = 'EncounterId')
BEGIN
    ALTER TABLE PatientMedications ADD EncounterId INT NULL;
    ALTER TABLE PatientMedications ADD CONSTRAINT FK_PatientMedications_Encounters FOREIGN KEY (EncounterId) REFERENCES Encounters(EncounterId);
END
GO

-- PatientProblems
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientProblems') AND name = 'EncounterId')
BEGIN
    ALTER TABLE PatientProblems ADD EncounterId INT NULL;
    ALTER TABLE PatientProblems ADD CONSTRAINT FK_PatientProblems_Encounters FOREIGN KEY (EncounterId) REFERENCES Encounters(EncounterId);
END
GO

-- PatientFamilyHistories
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientFamilyHistories') AND name = 'EncounterId')
BEGIN
    ALTER TABLE PatientFamilyHistories ADD EncounterId INT NULL;
    ALTER TABLE PatientFamilyHistories ADD CONSTRAINT FK_PatientFamilyHistories_Encounters FOREIGN KEY (EncounterId) REFERENCES Encounters(EncounterId);
END
GO

-- PatientSocialHistories
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientSocialHistories') AND name = 'EncounterId')
BEGIN
    ALTER TABLE PatientSocialHistories ADD EncounterId INT NULL;
    ALTER TABLE PatientSocialHistories ADD CONSTRAINT FK_PatientSocialHistories_Encounters FOREIGN KEY (EncounterId) REFERENCES Encounters(EncounterId);
END
GO

-- PatientImmunizations
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientImmunizations') AND name = 'EncounterId')
BEGIN
    ALTER TABLE PatientImmunizations ADD EncounterId INT NULL;
    ALTER TABLE PatientImmunizations ADD CONSTRAINT FK_PatientImmunizations_Encounters FOREIGN KEY (EncounterId) REFERENCES Encounters(EncounterId);
END
GO
