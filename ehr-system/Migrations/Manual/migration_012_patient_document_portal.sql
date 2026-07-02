-- Migration: Add IsPatientUploaded to PatientDocuments
-- Purpose: Track documents uploaded by patients via the portal vs clinic-uploaded
-- Date: 2026-04-15

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PatientDocuments' AND COLUMN_NAME = 'IsPatientUploaded')
BEGIN
    ALTER TABLE PatientDocuments ADD IsPatientUploaded BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsPatientUploaded column to PatientDocuments';
END
ELSE
BEGIN
    PRINT 'IsPatientUploaded column already exists';
END
