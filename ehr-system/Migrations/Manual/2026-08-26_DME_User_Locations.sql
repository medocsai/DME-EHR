/* ============================================================================
   User to location grants  (DMEEHR database)

   WHY
   Locations landed as a working FILTER: anyone in the tenant could switch to any
   branch, and the all-branches view showed the whole business. That is right for
   an owner and wrong for a delivery driver at one depot.

   This makes the branch a genuine restriction for the roles that should have
   one, using the same shape RehabDox uses.

   THE RULE THAT MATTERS MOST: AN EMPTY GRANT SET SEES NOTHING
   A restricted user with no rows here is a misconfigured user, and a
   misconfigured user must see nothing rather than everything. The fail-open
   shape to avoid, which RehabDox documents having found in its own codebase, is:

       if (allowed.Any()) { query = query.Where(...); }

   because it silently drops the filter for exactly the caller it was meant to
   contain. Services/DmeLocationScope.cs is written so that cannot happen.

   WHO IS EXEMPT, AND WHY THAT IS A DECISION
   Roles 0 (Super Admin) and 1 (Clinic Admin) bypass location scoping. A clinic
   admin administers the whole supplier, and the owner of a multi-branch DME
   business is exactly the person who needs the all-branches roll-up. Restricting
   them would defeat the reason locations were added.

   Roles 2 and above are restricted: clinician, front desk, biller, read only,
   medical assistant, nurse. In DME these are the people who work at a depot.

   WHY THIS TABLE IS BACKFILLED RATHER THAN CREATED EMPTY
   Checked before writing: this database has 27 active users in restricted roles.
   Creating the table empty would mean all 27 see nothing the moment this runs,
   which is an outage rather than a security improvement. Every existing user is
   therefore granted every active branch of their tenant, which preserves exactly
   today's behaviour. Narrowing someone is then a deliberate act on the user
   screen, not a side effect of a migration.

   WHY IT IS NOT IN THE ROW LEVEL SECURITY POLICY
   It carries no TenantId. The tenant comes through the user, exactly as it does
   for dbo.Users and dbo.Locations, neither of which is in the policy either.
   Adding a TenantId here would be a third copy of a fact both parents already
   hold.

   Idempotent: safe to re-run.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

IF OBJECT_ID('dbo.UserLocations','U') IS NULL
BEGIN
    CREATE TABLE dbo.UserLocations (
        UserLocationId INT IDENTITY(1,1) PRIMARY KEY,
        UserId         INT NOT NULL,
        LocationId     INT NOT NULL,
        CreatedAt      DATETIME2 NOT NULL CONSTRAINT DF_UserLocations_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_UserLocations_User     FOREIGN KEY (UserId)     REFERENCES dbo.Users(UserId),
        CONSTRAINT FK_UserLocations_Location FOREIGN KEY (LocationId) REFERENCES dbo.Locations(LocationId)
    );

    -- The same grant twice means nothing, and a duplicate would quietly double
    -- rows in any join through this table. RehabDox has no such index; that is a
    -- gap rather than a model to copy.
    CREATE UNIQUE INDEX UX_UserLocations_UserLocation ON dbo.UserLocations(UserId, LocationId);
    CREATE INDEX IX_UserLocations_LocationId ON dbo.UserLocations(LocationId);

    PRINT 'Created UserLocations';
END
GO

/* ---------------------------------------------------------------------------
   Backfill: preserve today's behaviour exactly.

   Every active user gets every active branch of their tenant. Nobody loses
   access on the day this runs, and every later change is somebody deciding.

   Super Admin is skipped: they belong to no tenant and bypass scoping anyway,
   so a grant row would be meaningless.
   --------------------------------------------------------------------------- */
INSERT INTO dbo.UserLocations (UserId, LocationId)
SELECT u.UserId, l.LocationId
FROM dbo.Users u
JOIN dbo.Locations l ON l.TenantId = u.TenantId AND l.IsActive = 1
WHERE u.TenantId IS NOT NULL
  AND u.IsActive = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.UserLocations ul
                  WHERE ul.UserId = u.UserId AND ul.LocationId = l.LocationId);
GO

/* ---------------------------------------------------------------------------
   Read model: who may see what, with the names attached.
   --------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vUserLocations AS
SELECT
    ul.UserLocationId,
    ul.UserId,
    ul.LocationId,
    ul.CreatedAt,
    u.TenantId,
    UserEmail    = u.Email,
    UserRole     = u.Role,
    LocationName = l.Name,
    IsPrimary    = l.IsPrimary,
    LocationActive = l.IsActive
FROM dbo.UserLocations ul
JOIN dbo.Users     u ON u.UserId = ul.UserId
JOIN dbo.Locations l ON l.LocationId = ul.LocationId;
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT CONCAT('grants: ', COUNT(*)) FROM dbo.UserLocations;

SELECT CONCAT('restricted users with no grant: ',
       (SELECT COUNT(*) FROM dbo.Users u
        WHERE u.IsActive = 1 AND u.TenantId IS NOT NULL AND u.Role >= 2
          AND NOT EXISTS (SELECT 1 FROM dbo.UserLocations ul WHERE ul.UserId = u.UserId)));

SELECT TOP 8 UserEmail, UserRole, LocationName FROM dbo.vUserLocations ORDER BY UserId, LocationName;
GO
