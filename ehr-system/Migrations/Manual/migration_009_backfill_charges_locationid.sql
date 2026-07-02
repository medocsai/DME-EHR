-- ============================================
-- STRIPE CONNECT MIGRATION 009
-- Backfill Charges.LocationId from Appointment.LocationId for existing rows
-- Charges created from non-encounter sources will need explicit location context (handled in code)
-- ============================================

UPDATE c
SET c.LocationId = a.LocationId
FROM Charges c
INNER JOIN Appointments a ON c.AppointmentId = a.AppointmentId
WHERE c.LocationId IS NULL
  AND c.AppointmentId IS NOT NULL
  AND a.LocationId IS NOT NULL;

PRINT CONCAT('Backfilled ', @@ROWCOUNT, ' Charges.LocationId from Appointments');

-- For Charges without an Appointment, fallback to Patient's preferred location
UPDATE c
SET c.LocationId = p.PreferredLocationId
FROM Charges c
INNER JOIN Patients p ON c.PatientId = p.PatientId
WHERE c.LocationId IS NULL
  AND p.PreferredLocationId IS NOT NULL;

PRINT CONCAT('Backfilled ', @@ROWCOUNT, ' Charges.LocationId from Patient.PreferredLocationId');

PRINT 'Migration 009 complete';
