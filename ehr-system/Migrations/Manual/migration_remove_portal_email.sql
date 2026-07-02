-- ============================================
-- REMOVE EMAIL FROM PatientPortalAccounts
-- Email now lives only in Patient table (encrypted at rest).
-- Portal login resolves email by decrypting Patient.Email in memory.
-- ============================================

-- Drop the index first (it depends on the Email column)
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PatientPortalAccounts_Email_LocationId' AND object_id = OBJECT_ID('PatientPortalAccounts'))
BEGIN
    DROP INDEX IX_PatientPortalAccounts_Email_LocationId ON PatientPortalAccounts;
    PRINT 'Dropped index IX_PatientPortalAccounts_Email_LocationId';
END

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('PatientPortalAccounts') AND name = 'Email')
BEGIN
    ALTER TABLE PatientPortalAccounts DROP COLUMN Email;
    PRINT 'Dropped Email column from PatientPortalAccounts';
END
