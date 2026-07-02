-- Migration 015: Add OTP fields to Users + create TrustedDevices table
-- Enables email-based OTP login (6-digit code, 5-minute expiry, 60s resend cooldown, 5-attempt limit)
-- and "Remember this device" trust (15-day SHA256 token).
--
-- Run with:
--   sqlcmd -S localhost -d IMEHR -E -Q "SET QUOTED_IDENTIFIER ON" -i migration_015_add_otp_and_trusted_devices.sql

SET QUOTED_IDENTIFIER ON;
GO

-- ============================================
-- Step 1: Add OTP columns to Users table
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'OtpCode')
BEGIN
    ALTER TABLE Users ADD OtpCode NVARCHAR(10) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'OtpExpiry')
BEGIN
    ALTER TABLE Users ADD OtpExpiry DATETIME2 NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'OtpAttempts')
BEGIN
    ALTER TABLE Users ADD OtpAttempts INT NOT NULL CONSTRAINT DF_Users_OtpAttempts DEFAULT 0;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'OtpResendCooldownUntil')
BEGIN
    ALTER TABLE Users ADD OtpResendCooldownUntil DATETIME2 NULL;
END
GO

-- ============================================
-- Step 2: Create TrustedDevices table
-- ============================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TrustedDevices')
BEGIN
    CREATE TABLE TrustedDevices
    (
        TrustedDeviceId   INT IDENTITY(1,1) NOT NULL,
        UserId            INT NOT NULL,
        DeviceTokenHash   NVARCHAR(128) NOT NULL,
        ExpiresAt         DATETIME2 NOT NULL,
        CreatedAt         DATETIME2 NOT NULL CONSTRAINT DF_TrustedDevices_CreatedAt DEFAULT (GETUTCDATE()),
        UserAgent         NVARCHAR(500) NULL,
        CONSTRAINT PK_TrustedDevices PRIMARY KEY CLUSTERED (TrustedDeviceId),
        CONSTRAINT FK_TrustedDevices_Users FOREIGN KEY (UserId) REFERENCES Users (UserId)
    );

    CREATE UNIQUE INDEX UX_TrustedDevices_TokenHash ON TrustedDevices (DeviceTokenHash);
    CREATE INDEX IX_TrustedDevices_UserId_ExpiresAt ON TrustedDevices (UserId, ExpiresAt);
END
GO

PRINT 'Migration 015 complete: OTP fields added to Users, TrustedDevices table created.';
GO
