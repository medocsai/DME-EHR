-- ============================================
-- Migration 011: Patient Portal Booking
-- 1. Rename CreatedBy → CreatedByUserId on Appointments
-- 2. Add CreatedByPatientId (FK → Patients)
-- 3. Seed DefaultPatientBookingDuration system setting
-- ============================================

-- 1. Rename CreatedBy → CreatedByUserId
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Appointments') AND name = 'CreatedBy')
AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Appointments') AND name = 'CreatedByUserId')
BEGIN
    EXEC sp_rename 'Appointments.CreatedBy', 'CreatedByUserId', 'COLUMN';
    PRINT 'Renamed Appointments.CreatedBy → CreatedByUserId';
END
GO

-- 2. Add CreatedByPatientId column
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Appointments') AND name = 'CreatedByPatientId')
BEGIN
    ALTER TABLE Appointments ADD CreatedByPatientId INT NULL;
    PRINT 'Added Appointments.CreatedByPatientId';
END
GO

-- 3. Add FK constraint for CreatedByPatientId → Patients
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Appointments_CreatedByPatientId')
BEGIN
    ALTER TABLE Appointments
    ADD CONSTRAINT FK_Appointments_CreatedByPatientId
    FOREIGN KEY (CreatedByPatientId) REFERENCES Patients(PatientId);
    PRINT 'Added FK_Appointments_CreatedByPatientId';
END
GO

-- 4. Seed DefaultPatientBookingDuration setting for all tenants that don't have it
INSERT INTO SystemSettings (TenantId, SettingKey, SettingValue, DataType, Description, Category, DefaultValue, CreatedAt, UpdatedAt)
SELECT t.TenantId, 'DefaultPatientBookingDuration', '30', 'int',
       'Default appointment duration (minutes) for patient portal bookings', 'Appointments', '30',
       GETUTCDATE(), GETUTCDATE()
FROM Tenants t
WHERE NOT EXISTS (
    SELECT 1 FROM SystemSettings s
    WHERE s.TenantId = t.TenantId AND s.SettingKey = 'DefaultPatientBookingDuration'
);
PRINT 'Seeded DefaultPatientBookingDuration setting';
GO
