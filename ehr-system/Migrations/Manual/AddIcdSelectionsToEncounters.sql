-- Add IcdSelections column to Encounters table for persisting ICD-10 diagnosis code selections
-- JSON string storing selected ICD-10 codes during encounter workflow
-- Mirrors the existing CptSelections column pattern
-- Order in JSON array determines order on CMS-1500 Box 21 (first = primary diagnosis = letter A)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Encounters') AND name = 'IcdSelections')
BEGIN
    ALTER TABLE Encounters ADD IcdSelections NVARCHAR(MAX) NULL;
    PRINT 'IcdSelections column added to Encounters table.';
END
ELSE
BEGIN
    PRINT 'IcdSelections column already exists. Skipping.';
END
