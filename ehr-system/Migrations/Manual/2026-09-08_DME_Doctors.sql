SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   Ordering physicians become manageable.

   WHAT WAS THERE

   dbo.DmeDoctors held three seeded rows. The New Order form offered them in a
   <select> and there was no way, anywhere in the product, to add a fourth.

   Every DMEPOS claim needs an ordering physician: CMS-1500 box 17 and 17b, and
   a claim without them is denied. Referrals arrive from whichever doctor the
   patient happens to see, so a supplier who cannot record a new one cannot
   process the referral at all. They would either raise the order under the
   wrong doctor, which is a false claim, or not raise it.

   WHAT THIS ADDS

   RetiredAt, so a doctor who stops referring is taken out of the picker while
   every order they ever signed still says who signed it. Same shape as
   DmeDistributors and DmeSftpAccounts: retired, never deleted, because the row
   is the record of what past claims were filed under.

   A unique index on (TenantId, Npi), because the NPI is the physician's
   national identity and two rows sharing one is the same doctor entered twice.
   Filtered to exclude NULL, since a doctor may be recorded before their NPI is
   known and several such rows must not collide with each other.

   THE FILTERED INDEX IS A DELIBERATE COST HERE, unlike on
   DmeCustomerInsurances where an unfiltered one expressed the same rule. It
   puts this table into the QUOTED_IDENTIFIER trap documented in CLAUDE.md, so a
   maintenance script touching DmeDoctors must run sqlcmd -I. Accepted because
   the alternative, rejecting every doctor whose NPI is not yet known, is worse
   than the trap.

   Safe to re-run.
   =========================================================================== */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeDoctors') AND name = 'RetiredAt')
BEGIN
    ALTER TABLE dbo.DmeDoctors ADD RetiredAt DATETIME2(7) NULL;
    PRINT 'Added DmeDoctors.RetiredAt.';
END
ELSE
    PRINT 'DmeDoctors.RetiredAt already present.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_DmeDoctors_Npi' AND object_id = OBJECT_ID('dbo.DmeDoctors'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.DmeDoctors
               WHERE Npi IS NOT NULL
               GROUP BY TenantId, Npi HAVING COUNT(*) > 1)
    BEGIN
        DECLARE @dupes NVARCHAR(MAX) = STUFF((
            SELECT ', ' + Npi FROM dbo.DmeDoctors
            WHERE Npi IS NOT NULL
            GROUP BY TenantId, Npi HAVING COUNT(*) > 1
            FOR XML PATH('')), 1, 2, '');

        RAISERROR('Cannot enforce one doctor per NPI. Duplicates: %s', 16, 1, @dupes);
    END

    CREATE UNIQUE INDEX UX_DmeDoctors_Npi
        ON dbo.DmeDoctors (TenantId, Npi)
        WHERE Npi IS NOT NULL;

    PRINT 'Added UX_DmeDoctors_Npi.';
END
ELSE
    PRINT 'UX_DmeDoctors_Npi already present.';
GO

/* --------------------------------------------------------------- verification */
DECLARE @missing NVARCHAR(MAX) = N'';

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeDoctors') AND name = 'RetiredAt')
    SET @missing = @missing + N'DmeDoctors.RetiredAt; ';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DmeDoctors_Npi')
    SET @missing = @missing + N'UX_DmeDoctors_Npi; ';

IF @missing <> N''
    RAISERROR('Doctor management incomplete: %s', 16, 1, @missing);
ELSE
    PRINT 'Verified: a referring physician can be added, and retired without losing history.';
GO
