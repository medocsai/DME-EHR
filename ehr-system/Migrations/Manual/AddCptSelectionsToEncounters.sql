-- Add CptSelections column to Encounters table for persisting CPT code selections
-- JSON string storing selected CPT codes during encounter workflow
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Encounters') AND name = 'CptSelections')
BEGIN
    ALTER TABLE Encounters ADD CptSelections NVARCHAR(MAX) NULL;
    PRINT 'CptSelections column added to Encounters table.';
END
ELSE
BEGIN
    PRINT 'CptSelections column already exists. Skipping.';
END
