/* ============================================================================
   DME — onboard a new tenant  (DMEEHR database)

   RUN THIS BY HAND, as a Super Admin, after creating a tenant in the clinic
   console (/Home/Tenants). Set @TargetTenantId and @SourceTenantId below.

   WHY IT IS NEEDED
   The HCPCS catalog and the payer list are TENANT data: each DME supplier keeps
   its own item master and its own contract pricing. That is the correct model,
   but it means a tenant created through the console starts with an empty
   catalog, and with no catalog there is nothing to put on an order. The tenant
   exists and cannot be used until this runs.

   WHY IT IS A SCRIPT AND NOT A BUTTON IN THE APP
   Every DME connection is pinned to the caller's tenant via SESSION_CONTEXT,
   and the row level security policy BLOCKS writes that name a different tenant.
   That guard is the whole point of the tenant isolation work, so an in-app
   "copy catalog to another tenant" action would have to punch a hole straight
   through it. A hole that exists is a hole that gets used. Onboarding happens
   once per customer, by hand, by a person who already has database access.

   WHY IT COPIES RATHER THAN INVENTS
   Prices are commercial terms. Seeding a new supplier with made-up
   reimbursement rates would put wrong numbers on real claims. Copying from a
   nominated source tenant means every value started as one somebody actually
   set, and the new tenant edits from there.

   Referring doctors are deliberately NOT copied: those are one supplier's own
   referral relationships, and copying them moves one customer's contacts into
   another customer's account.

   Idempotent: rows that already exist for the target tenant are skipped.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

DECLARE @TargetTenantId INT = 0;   -- <<< set me: the new tenant
DECLARE @SourceTenantId INT = 1;   -- <<< the tenant whose catalog to copy

IF @TargetTenantId = 0
BEGIN
    RAISERROR('Set @TargetTenantId before running this script.', 16, 1);
    RETURN;
END

IF @TargetTenantId = @SourceTenantId
BEGIN
    RAISERROR('Source and target tenant are the same; nothing to do.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = @TargetTenantId)
BEGIN
    RAISERROR('Target tenant does not exist. Create it in /Home/Tenants first.', 16, 1);
    RETURN;
END

/* Run with no SESSION_CONTEXT set, so the row level security predicate falls
   through and both tenants are visible to this one script. This is the
   deliberate exception described in the header. */
EXEC sp_set_session_context N'CurrentTenantId', NULL;

INSERT INTO dbo.HcpcsCodes (Hcpcs,Name,Category,IsSerialized,Rentable,Purchasable,
                            PurchasePrice,MonthlyRate,CappedRentalMonths,Modifiers,ReorderPoint,TenantId)
SELECT s.Hcpcs, s.Name, s.Category, s.IsSerialized, s.Rentable, s.Purchasable,
       s.PurchasePrice, s.MonthlyRate, s.CappedRentalMonths, s.Modifiers, s.ReorderPoint, @TargetTenantId
FROM dbo.HcpcsCodes s
WHERE s.TenantId = @SourceTenantId
  AND NOT EXISTS (SELECT 1 FROM dbo.HcpcsCodes t
                  WHERE t.TenantId = @TargetTenantId AND t.Hcpcs = s.Hcpcs);
PRINT CONCAT('Catalog items copied: ', @@ROWCOUNT);

/* Payers are NOT copied any more, and that is not an omission.

   dbo.DmePayers became Office Ally's national list on 2026-08-27: global, no
   TenantId, outside the security policy, exactly like dbo.DmeCarcCodes. A new
   supplier gets all 4,017 payers the moment it exists. Copying a national list
   per tenant was thousands of duplicates of a fact none of them owns.
   See Migrations/Manual/2026-08-27_DME_Payer_Catalog.sql. */

/* Order and claim numbering needs no seeding: DmeDb.NextNumber creates a
   tenant's counter on first use. */

SELECT @TargetTenantId AS TenantId,
       (SELECT COUNT(*) FROM dbo.HcpcsCodes WHERE TenantId = @TargetTenantId) AS CatalogItems,
       (SELECT COUNT(*) FROM dbo.DmePayers) AS Payers_GlobalCatalog,
       (SELECT COUNT(*) FROM dbo.DmeDoctors WHERE TenantId = @TargetTenantId) AS Doctors_NotCopiedByDesign;
GO
