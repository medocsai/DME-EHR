-- ============================================
-- STRIPE CONNECT MIGRATION 003
-- Adds LocationId to Charges table
-- Per Hammas: charges should know their location for future use cases
-- ============================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Charges') AND name = 'LocationId')
BEGIN
    ALTER TABLE Charges ADD LocationId INT NULL;
    PRINT 'Added Charges.LocationId';
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Charges_Locations')
BEGIN
    ALTER TABLE Charges ADD CONSTRAINT FK_Charges_Locations
        FOREIGN KEY (LocationId) REFERENCES Locations(LocationId);
    PRINT 'Added FK_Charges_Locations';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Charges_LocationId' AND object_id = OBJECT_ID('Charges'))
BEGIN
    CREATE INDEX IX_Charges_LocationId
        ON Charges(LocationId);
    PRINT 'Added IX_Charges_LocationId';
END

PRINT 'Migration 003 complete';
