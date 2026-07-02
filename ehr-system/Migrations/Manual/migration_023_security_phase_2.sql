-- ============================================================================
-- Security Hardening Phase 2 (qa-testing branch)
-- ============================================================================
-- Run with:
--   sqlcmd -S localhost -d IMEHR -E -i migration_023_security_phase_2.sql
-- or, for production, equivalent connection params + the same -i.
--
-- Idempotent: each ALTER/CREATE is guarded; safe to re-run.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ----------------------------------------------------------------------------
-- H6: Intake portal token expiry
-- ----------------------------------------------------------------------------
-- Tablet intake URLs (/intake/p/{token}) previously had no expiry — a leaked
-- URL granted access forever. Add an expiry column; tokens issued going
-- forward default to 30-day expiry. Existing rows have NULL = legacy "never
-- expires" until rotated.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'IntakePortalTokenExpiresAt' AND Object_ID = Object_ID(N'dbo.Patients')
)
BEGIN
    ALTER TABLE dbo.Patients
        ADD IntakePortalTokenExpiresAt DATETIME2 NULL;
    PRINT 'Added Patients.IntakePortalTokenExpiresAt';
END
ELSE
BEGIN
    PRINT 'Patients.IntakePortalTokenExpiresAt already exists, skipping';
END
GO

-- ----------------------------------------------------------------------------
-- E3: Audit log immutability
-- ----------------------------------------------------------------------------
-- Block UPDATE on AuditLogs unconditionally — no caller should ever amend
-- an audit row. Block DELETE unless the connection has set
-- SESSION_CONTEXT('AllowAuditDelete') = 1, which only the retention background
-- service does (after verifying age > HIPAA:AuditRetentionDays). This means
-- a malicious clinic admin or compromised app account cannot wipe their
-- access trail; only a deliberate, scoped retention cleanup can purge.
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_AuditLogs_Immutable')
BEGIN
    DROP TRIGGER dbo.TR_AuditLogs_Immutable;
    PRINT 'Dropped existing TR_AuditLogs_Immutable (will recreate)';
END
GO

CREATE TRIGGER dbo.TR_AuditLogs_Immutable
ON dbo.AuditLogs
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- UPDATE is never allowed.
    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
    BEGIN
        RAISERROR('AuditLogs are append-only — UPDATE is not permitted (HIPAA 45 CFR 164.312(b)).', 16, 1);
        RETURN;
    END

    -- DELETE is allowed ONLY when the retention service has set
    -- SESSION_CONTEXT('AllowAuditDelete') = 1 on the current connection.
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
    BEGIN
        DECLARE @allow SQL_VARIANT = SESSION_CONTEXT(N'AllowAuditDelete');
        IF @allow IS NULL OR CAST(@allow AS INT) <> 1
        BEGIN
            RAISERROR('AuditLogs DELETE is restricted to the retention service.', 16, 1);
            RETURN;
        END

        -- Authorized retention cleanup — perform the deletion.
        DELETE a
        FROM dbo.AuditLogs a
        INNER JOIN deleted d ON d.AuditId = a.AuditId;
    END
END
GO

PRINT 'Created TR_AuditLogs_Immutable (blocks UPDATE always; DELETE only with AllowAuditDelete session context)';
GO

-- ----------------------------------------------------------------------------
-- C2: Password account lockout
-- ----------------------------------------------------------------------------
-- Per HIPAA configuration MaxLoginAttempts=5: lock the user account for 15
-- minutes after 5 wrong-password attempts in a row. The OTP lockout from A6
-- only fires once a correct password has been entered; this layer protects
-- the password verify itself.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'FailedPasswordAttempts' AND Object_ID = Object_ID(N'dbo.Users')
)
BEGIN
    ALTER TABLE dbo.Users ADD FailedPasswordAttempts INT NOT NULL CONSTRAINT DF_Users_FailedPasswordAttempts DEFAULT 0;
    PRINT 'Added Users.FailedPasswordAttempts';
END
ELSE
BEGIN
    PRINT 'Users.FailedPasswordAttempts already exists, skipping';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'PasswordLockedUntil' AND Object_ID = Object_ID(N'dbo.Users')
)
BEGIN
    ALTER TABLE dbo.Users ADD PasswordLockedUntil DATETIME2 NULL;
    PRINT 'Added Users.PasswordLockedUntil';
END
ELSE
BEGIN
    PRINT 'Users.PasswordLockedUntil already exists, skipping';
END
GO

-- ----------------------------------------------------------------------------
-- D1: JWT invalidation on logout / password change / role change
-- ----------------------------------------------------------------------------
-- TokenVersion is bumped whenever a security-relevant change happens
-- (logout, password change, role change). The JWT carries the version it
-- was issued with; the auth middleware compares against the current DB
-- value and rejects if they differ. So a stolen JWT becomes useless the
-- moment the legitimate user logs out or changes their password.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'TokenVersion' AND Object_ID = Object_ID(N'dbo.Users')
)
BEGIN
    ALTER TABLE dbo.Users ADD TokenVersion INT NOT NULL CONSTRAINT DF_Users_TokenVersion DEFAULT 1;
    PRINT 'Added Users.TokenVersion';
END
ELSE
BEGIN
    PRINT 'Users.TokenVersion already exists, skipping';
END
GO

-- ----------------------------------------------------------------------------
-- F2: SQL Server Row-Level Security (DEFINED, DISABLED BY DEFAULT)
-- ----------------------------------------------------------------------------
-- Defense-in-depth on top of EF Core global query filters (F1). Even if a
-- developer writes raw SQL that forgets WHERE TenantId = X, the database
-- itself filters out cross-tenant rows. Application sets
-- SESSION_CONTEXT('CurrentTenantId') per request via TenantResolutionMiddleware.
--
-- The policy is created in STATE = OFF so this migration is safe to deploy
-- without coordinating an immediate behavior change. Verify the application
-- is setting the session context on every authenticated request, then flip
-- with:
--     ALTER SECURITY POLICY dbo.TenantIsolationPolicy WITH (STATE = ON);
-- and re-run smoke tests. To roll back:
--     ALTER SECURITY POLICY dbo.TenantIsolationPolicy WITH (STATE = OFF);
--
-- Predicate semantics:
--   - SESSION_CONTEXT('CurrentTenantId') = row.TenantId  → row visible
--   - SESSION_CONTEXT('CurrentTenantId') IS NULL         → row visible
--     (Super Admin, unauthenticated login lookup, background services)
--
-- Tables covered: highest-PHI tenant-scoped entities matching the F1 EF
-- filter set. Add more tables here if you tighten the F1 set.
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'TenantIsolationPolicy')
BEGIN
    DROP SECURITY POLICY dbo.TenantIsolationPolicy;
    PRINT 'Dropped existing dbo.TenantIsolationPolicy (will recreate)';
END
GO

IF EXISTS (SELECT 1 FROM sys.objects WHERE name = N'fn_TenantPredicate' AND type = 'IF')
BEGIN
    DROP FUNCTION dbo.fn_TenantPredicate;
    PRINT 'Dropped existing dbo.fn_TenantPredicate (will recreate)';
END
GO

CREATE FUNCTION dbo.fn_TenantPredicate(@RowTenantId INT)
RETURNS TABLE
WITH SCHEMABINDING
AS RETURN
    SELECT 1 AS fn_Result
    WHERE
        -- App-set session context matches the row's tenant.
        @RowTenantId = CAST(SESSION_CONTEXT(N'CurrentTenantId') AS INT)
        -- Or no context set (Super Admin, system / background tasks,
        -- unauthenticated lookup paths) — falls through to existing
        -- application-level controls.
        OR SESSION_CONTEXT(N'CurrentTenantId') IS NULL;
GO
PRINT 'Created dbo.fn_TenantPredicate';
GO

CREATE SECURITY POLICY dbo.TenantIsolationPolicy
    ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.Patients,
    ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.ClinicalNotes,
    ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.Appointments,
    ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.Encounters,
    ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.Insurances,
    ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.Prescriptions
WITH (STATE = OFF);  -- Flip to ON after verifying middleware sets context.
GO
PRINT 'Created dbo.TenantIsolationPolicy in STATE = OFF (enable after deploy verification)';
GO
