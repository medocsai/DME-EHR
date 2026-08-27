/* =============================================================================
   DME — Drop shipping: items that never enter the warehouse
   Date: 2026-08-27
   Migration order: 13 (after 2026-08-27_DME_Hcpcs_Catalog.sql)

   WHY
   ---
   The client's words, under Inventory: "Most of the items we deliver are
   drop-shipped from manufacturer/distributors. Is it possible include it here?"

   Inventory assumed the supplier holds the goods. DmeStockMovements is a ledger
   of physical stock in a branch and DmeSerializedUnits tracks units in the
   supplier's possession. A drop-shipped item goes from the distributor to the
   customer's home. It is never on a shelf, so it has no on-hand quantity, and
   most of what this supplier delivers works that way.

   THE FIX IS MOSTLY AN ABSENCE
   ---------------------------
   A drop-shipped line writes NO STOCK MOVEMENT and NO SERIALISED UNIT, because
   nothing entered or left a warehouse. On-hand therefore stays correct by
   construction rather than by remembering to compensate. Forcing these through
   the ledger would make the inventory figures wrong for the MAJORITY of what
   they deliver, which is the stock problem at full scale.

   WHAT IS ACTUALLY STORED
   -----------------------
   Two facts with no home today, both on the ORDER LINE, because the same item
   can be shipped from stock one week and direct from the distributor the next:

     DistributorId   who it ships from
     DistributorRef  their order or tracking number

   "Is this line drop-shipped" is NOT stored. It is DERIVED from DistributorId
   being present. A separate flag would be a second copy of the same fact and
   the two would disagree the first time one was set without the other.

   WHY DISTRIBUTORS ARE TENANT DATA
   --------------------------------
   Unlike payers, ICD-10 and HCPCS, this is not a national list. Each supplier
   negotiates with their own distributors, so dbo.DmeDistributors is tenant
   scoped and goes into TenantIsolationPolicy like every other DME table.

   THE CONSEQUENCE WORTH KNOWING
   -----------------------------
   On a drop-shipped line nobody from this supplier is present at the door, so
   there is no signature to capture. The distributor's tracking reference IS the
   delivery evidence, and the proof-of-delivery screen says so instead of
   demanding a signature that would have to be invented.

   Billing does not change. A drop-shipped item is still delivered, still
   billed, and still generates a rental and a claim line.

   SAFE TO RE-RUN.
   ============================================================================= */

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   1. Who a supplier buys from
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmeDistributors','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeDistributors (
        DistributorId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId      INT           NOT NULL,
        Name          NVARCHAR(160) NOT NULL,
        AccountNo     NVARCHAR(60)  NULL,   -- the supplier's account number WITH them
        Phone         NVARCHAR(40)  NULL,
        Email         NVARCHAR(160) NULL,
        -- Taken out of service, never deleted: past orders are the record of
        -- who shipped them. Same rule as DmeSftpAccounts.
        RetiredAt     DATETIME2     NULL,
        CreatedAt     DATETIME2     NOT NULL CONSTRAINT DF_DmeDistributors_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    PRINT 'dbo.DmeDistributors created.';
END
ELSE
    PRINT 'dbo.DmeDistributors already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('dbo.DmeDistributors') AND name = 'IX_DmeDistributors_Tenant')
    CREATE INDEX IX_DmeDistributors_Tenant ON dbo.DmeDistributors (TenantId, Name);
GO

/* One supplier cannot have the same distributor twice under the same name.
   Scoped to the tenant, because two suppliers may well both buy from Invacare. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('dbo.DmeDistributors') AND name = 'UX_DmeDistributors_NamePerTenant')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM (SELECT TenantId, Name FROM dbo.DmeDistributors
                                  GROUP BY TenantId, Name HAVING COUNT(*) > 1) d)
        CREATE UNIQUE INDEX UX_DmeDistributors_NamePerTenant ON dbo.DmeDistributors (TenantId, Name);
    ELSE
        PRINT 'WARNING: duplicate distributor names exist, unique index NOT created. Resolve them first.';
END
GO

/* ---------------------------------------------------------------------------
   2. The order line points at one, or does not
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeOrderLines') AND name = 'DistributorId')
BEGIN
    -- NULL means "out of our own stock", which is what every existing line is.
    -- That is why there is no backfill and no default: the absence IS the
    -- answer, and it is the correct answer for all of them.
    ALTER TABLE dbo.DmeOrderLines ADD DistributorId INT NULL;
    PRINT 'DmeOrderLines.DistributorId added.';
END
ELSE
    PRINT 'DmeOrderLines.DistributorId already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeOrderLines') AND name = 'DistributorRef')
BEGIN
    ALTER TABLE dbo.DmeOrderLines ADD DistributorRef NVARCHAR(80) NULL;
    PRINT 'DmeOrderLines.DistributorRef added.';
END
ELSE
    PRINT 'DmeOrderLines.DistributorRef already exists.';
GO

/* No FOREIGN KEY to DmeDistributors, for the same reason nothing else in this
   schema carries one across a row-level-security boundary: the constraint would
   be checked against rows the caller's predicate hides, and a retired
   distributor must still resolve on an old order. The check that a posted
   distributor belongs to this tenant lives in DmeController, beside the
   identical check the location picker already does. */

/* NOT a filtered index, and that is deliberate.

   The obvious choice here is `WHERE DistributorId IS NOT NULL`, because the
   column is NULL on almost every line. The first draft did exactly that, and a
   filtered index makes its table permanently require QUOTED_IDENTIFIER ON for
   every INSERT, UPDATE and DELETE against it, by anything, forever. sqlcmd runs
   with that option OFF unless invoked with -I, so a maintenance script written
   against the table months from now fails with an error that mentions neither
   the index nor drop shipping.

   Four tables in this database already carry that constraint from earlier
   migrations: Users, Locations and DmeClaimLines. This one deliberately does
   not join them. A plain index costs a few kilobytes on a small table; the
   trap costs somebody an afternoon. */
IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID('dbo.DmeOrderLines')
             AND name = 'IX_DmeOrderLines_Distributor' AND has_filter = 1)
BEGIN
    DROP INDEX IX_DmeOrderLines_Distributor ON dbo.DmeOrderLines;
    PRINT 'Filtered IX_DmeOrderLines_Distributor dropped, see the note above.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('dbo.DmeOrderLines') AND name = 'IX_DmeOrderLines_Distributor')
    CREATE INDEX IX_DmeOrderLines_Distributor ON dbo.DmeOrderLines (DistributorId);
GO

/* ---------------------------------------------------------------------------
   3. Row level security, exactly like every other DME table

   ALTER SECURITY POLICY is validated at COMPILE time, so an IF NOT EXISTS guard
   around a literal statement does not protect it. sp_executesql defers it.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeDistributors')
      AND sp.predicate_type_desc = 'FILTER')
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeDistributors;';
    PRINT 'FILTER predicate added on DmeDistributors.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeDistributors')
      AND sp.predicate_type_desc = 'BLOCK')
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeDistributors AFTER INSERT;';
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeDistributors AFTER UPDATE;';
    PRINT 'BLOCK predicates added on DmeDistributors.';
END
GO

/* ---------------------------------------------------------------------------
   4. What is on its way from a distributor

   Everything here is DERIVED. A line is drop-shipped because it names a
   distributor; it has arrived because its order is delivered. Neither is
   stored, so neither can drift.

   This is the third number the Inventory screen shows, beside company stock and
   branch stock, and it deliberately does NOT roll into either: an item sitting
   in a distributor's warehouse is not stock this supplier holds.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.vDmeDropShipments','V') IS NOT NULL DROP VIEW dbo.vDmeDropShipments;
GO
CREATE VIEW dbo.vDmeDropShipments
AS
SELECT  ol.LineId,
        ol.TenantId,
        ol.OrderId,
        o.OrderNumber,
        o.Status        AS OrderStatus,
        o.DeliveryDate,
        c.CustomerId,
        c.FirstName     AS CustomerFirstName,   -- ciphertext, compose in the app
        c.LastName      AS CustomerLastName,
        c.LocationId,
        ol.Hcpcs,
        ol.ItemName,
        ol.Qty,
        ol.DistributorId,
        d.Name          AS DistributorName,
        d.AccountNo     AS DistributorAccountNo,
        ol.DistributorRef,
        CAST(CASE WHEN o.Status = 'delivered' THEN 1 ELSE 0 END AS BIT) AS HasArrived
FROM        dbo.DmeOrderLines  ol
JOIN        dbo.DmeOrders      o ON o.OrderId    = ol.OrderId
JOIN        dbo.DmeCustomers   c ON c.CustomerId = o.CustomerId
LEFT JOIN   dbo.DmeDistributors d ON d.DistributorId = ol.DistributorId
WHERE       ol.DistributorId IS NOT NULL;
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT 'distributors'           AS Item, COUNT(*) AS Value FROM dbo.DmeDistributors
UNION ALL
SELECT 'drop-shipped lines',     COUNT(*) FROM dbo.vDmeDropShipments
UNION ALL
SELECT 'in RLS policy',          COUNT(*) FROM sys.security_predicates
    WHERE target_object_id = OBJECT_ID('dbo.DmeDistributors')
UNION ALL
SELECT 'stock movements against a drop-shipped line (must be 0)', COUNT(*)
    FROM dbo.DmeStockMovements m
    WHERE m.RefType = 'DmeOrder'
      AND EXISTS (SELECT 1 FROM dbo.vDmeDropShipments ds
                  WHERE ds.OrderId = m.RefId AND ds.Hcpcs = m.Hcpcs);
GO
