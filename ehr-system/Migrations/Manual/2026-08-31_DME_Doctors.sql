/* ============================================================================
   Referring doctors: a way to add one.

   dbo.DmeDoctors was seeded with three rows and had no INSERT anywhere in the
   product. A supplier who took an order from a fourth doctor had no way to
   record them, and the ordering physician is not optional on DMEPOS: their name
   and NPI go in boxes 17 and 17b of the CMS-1500, and a claim without them is
   rejected.

   Two things are added:

     RetiredAt   a doctor is retired, never deleted. Old orders name them, and
                 that has to survive the referral relationship ending. Same
                 shape as DmeDistributors.RetiredAt and DmePayments.VoidedAt.

     UX_DmeDoctors_Npi  one NPI, one doctor, per supplier. An NPI identifies a
                 person nationally, so two rows sharing one is a duplicate, and
                 picking the wrong one puts the wrong prescriber on a claim.
                 Filtered on RetiredAt so a retired row does not block re-adding
                 a doctor you have started working with again.

   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.DmeDoctors', 'RetiredAt') IS NULL
    ALTER TABLE dbo.DmeDoctors ADD RetiredAt DATETIME2 NULL;
GO

/* Blank NPIs are collapsed to NULL first: '' repeated across rows would trip
   the unique index, and "we do not have it yet" is a fact, not an empty string. */
UPDATE dbo.DmeDoctors SET Npi = NULL WHERE Npi IS NOT NULL AND LTRIM(RTRIM(Npi)) = '';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DmeDoctors_Npi'
                 AND object_id = OBJECT_ID('dbo.DmeDoctors'))
BEGIN
    /* Filtered, so it only covers doctors still in service and still carrying an
       NPI. NOTE: a filtered index makes every INSERT, UPDATE and DELETE against
       this table require QUOTED_IDENTIFIER ON. sqlcmd defaults it OFF, so a
       maintenance script against DmeDoctors must run with -I. The same trap
       already applies to Users, Locations and DmeClaimLines. */
    CREATE UNIQUE INDEX UX_DmeDoctors_Npi
        ON dbo.DmeDoctors (TenantId, Npi)
        WHERE Npi IS NOT NULL AND RetiredAt IS NULL;
END
GO

PRINT 'Doctors: RetiredAt added, NPI unique per supplier among doctors in service.';
GO
