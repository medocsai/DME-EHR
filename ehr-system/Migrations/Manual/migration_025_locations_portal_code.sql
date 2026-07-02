-- ============================================================================
-- Migration 025: Locations.PortalCode + PlaceOfServiceCode + FacilityNpi
-- ============================================================================
-- Production schema (ServerImEhrSchema.sql, 4/27/2026) and the C# Location
-- model declare these columns, but no prior migration creates them. Same
-- "developer added property without writing migration" pattern as 024.
--
-- AddPortalCodeToExistingLocations.sql (the existing backfill script) assumes
-- PortalCode already exists — this migration must run BEFORE that backfill.
--
-- Idempotent — guarded by COL_LENGTH; safe to re-run.
-- ============================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.Locations', 'PortalCode') IS NULL
BEGIN
    ALTER TABLE dbo.Locations ADD PortalCode NVARCHAR(MAX) NULL;
    PRINT 'Added Locations.PortalCode';
END
ELSE
BEGIN
    PRINT 'Locations.PortalCode already exists, skipping';
END
GO

IF COL_LENGTH('dbo.Locations', 'PlaceOfServiceCode') IS NULL
BEGIN
    ALTER TABLE dbo.Locations ADD PlaceOfServiceCode NVARCHAR(10) NULL;
    PRINT 'Added Locations.PlaceOfServiceCode';
END
ELSE
BEGIN
    PRINT 'Locations.PlaceOfServiceCode already exists, skipping';
END
GO

IF COL_LENGTH('dbo.Locations', 'FacilityNpi') IS NULL
BEGIN
    ALTER TABLE dbo.Locations ADD FacilityNpi NVARCHAR(20) NULL;
    PRINT 'Added Locations.FacilityNpi';
END
ELSE
BEGIN
    PRINT 'Locations.FacilityNpi already exists, skipping';
END
GO

PRINT 'Migration 025 complete: Locations schema sync';
GO
