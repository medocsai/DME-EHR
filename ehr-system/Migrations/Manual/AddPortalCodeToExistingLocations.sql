-- Backfill PortalCode for any Locations created before CreateLocationAsync was fixed.
-- Safe to run multiple times — WHERE filter skips rows that already have a code.
-- NEWID() is evaluated per-row, so each location gets its own unique 8-char code.

SET QUOTED_IDENTIFIER ON;

-- Preview first (optional): rows that will be updated
-- SELECT LocationId, TenantId, Name, PortalCode FROM Locations WHERE PortalCode IS NULL OR PortalCode = '';

UPDATE Locations
SET PortalCode = LOWER(LEFT(REPLACE(CAST(NEWID() AS varchar(36)), '-', ''), 8))
WHERE PortalCode IS NULL OR PortalCode = '';

-- Verify: should return 0 rows after running
SELECT LocationId, TenantId, Name, PortalCode
FROM Locations
WHERE PortalCode IS NULL OR PortalCode = '';
