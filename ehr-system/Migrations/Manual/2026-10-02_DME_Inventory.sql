SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   Inventory that can be run: stock in, stock moved, stock corrected, and
   equipment back from a rental.

   WHAT WAS MISSING

   The stock ledger (dbo.DmeStockMovements) only ever went DOWN. Delivery wrote
   a negative movement and nothing in the product wrote a positive one, so a new
   supplier started at zero and every delivery from their own shelf drove
   on-hand negative. Lakeview only looked right because the 2026-08-25 migration
   seeded an opening balance.

   Three more holes of the same shape:
     - Delivery INVENTED a serial number ("SN-" + random) for every serialised
       unit instead of using the unit that was actually on the shelf. The unit
       register and the rental then named a serial that exists nowhere.
     - A rental could not be returned. The equipment stayed "rented" to the
       customer for ever and never came back into stock.
     - Nothing could correct a miscount, a damaged unit or a unit sent for
       repair.

   WHAT THIS ADDS (schema only; the screens are on /Dme/Inventory and
   /Dme/Rentals, the rules in Services/DmeInventory.cs)

   1. CK_DmeStockMovements_Reason. The ledger's reasons were a comment. Now they
      are a list: opening | receipt | delivery | return | adjustment | transfer.
   2. CK_DmeSerializedUnits_Status. Same for the unit lifecycle:
      in-stock | maintenance | rented | sold | recalled | written-off.
   3. UX_DmeSerializedUnits_Serial. One serial per item per supplier. A recall
      notice names a serial; two units answering to it is no answer at all.
      Filtered to leave out the "-" placeholder older deliveries wrote for
      consumables. FILTERED, so writes need QUOTED_IDENTIFIER ON: sqlcmd -I.
   4. DmeRentals.UnitId. WHICH physical unit is out on this rental. The serial
      text on the rental is the point-in-time record and stays; this is the
      link that lets the unit come back. Backfilled by matching serial, item and
      customer.
   5. vDmeStockLedger. The movements with names, for the history panel.

   NOTHING DERIVED IS STORED. On-hand stays SUM(Qty). A unit's availability is
   its Status. When a rental was returned is the OccurredAt of its 'return'
   movement.

   Run with sqlcmd -I. Safe to re-run.
   =========================================================================== */

/* ---------------------------------------------------------------------------
   1. Movement reasons.
   --------------------------------------------------------------------------- */
DECLARE @badReason INT = (
    SELECT COUNT(*) FROM dbo.DmeStockMovements
    WHERE Reason NOT IN ('opening','receipt','delivery','return','adjustment','transfer'));

IF @badReason > 0
BEGIN
    RAISERROR('%d stock movement(s) carry a reason outside the list. Inspect them before re-running.', 16, 1, @badReason);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeStockMovements_Reason')
BEGIN
    EXEC sp_executesql N'ALTER TABLE dbo.DmeStockMovements ADD CONSTRAINT CK_DmeStockMovements_Reason
        CHECK (Reason IN (''opening'',''receipt'',''delivery'',''return'',''adjustment'',''transfer''));';
    PRINT 'Added CK_DmeStockMovements_Reason.';
END
GO

/* ---------------------------------------------------------------------------
   2. Unit lifecycle.
   --------------------------------------------------------------------------- */
DECLARE @badStatus INT = (
    SELECT COUNT(*) FROM dbo.DmeSerializedUnits
    WHERE Status NOT IN ('in-stock','maintenance','rented','sold','recalled','written-off'));

IF @badStatus > 0
BEGIN
    RAISERROR('%d serialised unit(s) carry a status outside the list. Inspect them before re-running.', 16, 1, @badStatus);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeSerializedUnits_Status')
BEGIN
    EXEC sp_executesql N'ALTER TABLE dbo.DmeSerializedUnits ADD CONSTRAINT CK_DmeSerializedUnits_Status
        CHECK (Status IN (''in-stock'',''maintenance'',''rented'',''sold'',''recalled'',''written-off''));';
    PRINT 'Added CK_DmeSerializedUnits_Status.';
END
GO

/* ---------------------------------------------------------------------------
   3. One serial per item per supplier.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DmeSerializedUnits_Serial'
               AND object_id = OBJECT_ID('dbo.DmeSerializedUnits'))
BEGIN
    DECLARE @dupes INT = (
        SELECT COUNT(*) FROM (
            SELECT TenantId, Hcpcs, SerialNumber
            FROM dbo.DmeSerializedUnits
            WHERE SerialNumber <> N'-' AND SerialNumber <> N'—'
            GROUP BY TenantId, Hcpcs, SerialNumber
            HAVING COUNT(*) > 1) d);

    IF @dupes > 0
    BEGIN
        RAISERROR('%d serial number(s) appear on more than one unit of the same item. Resolve them before re-running.', 16, 1, @dupes);
        RETURN;
    END

    EXEC sp_executesql N'CREATE UNIQUE INDEX UX_DmeSerializedUnits_Serial
        ON dbo.DmeSerializedUnits (TenantId, Hcpcs, SerialNumber)
        WHERE SerialNumber <> N''-'' AND SerialNumber <> N''—'';';
    PRINT 'Added UX_DmeSerializedUnits_Serial.';
END
GO

/* ---------------------------------------------------------------------------
   4. Which unit is out on a rental.
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.DmeRentals', 'UnitId') IS NULL
BEGIN
    ALTER TABLE dbo.DmeRentals ADD UnitId INT NULL;
    PRINT 'Added DmeRentals.UnitId.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DmeRentals_Unit')
BEGIN
    EXEC sp_executesql N'ALTER TABLE dbo.DmeRentals ADD CONSTRAINT FK_DmeRentals_Unit
        FOREIGN KEY (UnitId) REFERENCES dbo.DmeSerializedUnits (UnitId);';
    PRINT 'Added FK_DmeRentals_Unit.';
END
GO

/* Backfill: the unit with the same serial, item and customer. Only an exact,
   single match is linked; anything ambiguous stays NULL rather than guessed. */
EXEC sp_executesql N'
    UPDATE r SET UnitId = u.UnitId
    FROM dbo.DmeRentals r
    JOIN dbo.DmeSerializedUnits u
      ON u.TenantId = r.TenantId AND u.Hcpcs = r.Hcpcs
     AND u.SerialNumber = r.Serial AND u.CustomerId = r.CustomerId
    WHERE r.UnitId IS NULL
      AND (SELECT COUNT(*) FROM dbo.DmeSerializedUnits u2
           WHERE u2.TenantId = r.TenantId AND u2.Hcpcs = r.Hcpcs
             AND u2.SerialNumber = r.Serial AND u2.CustomerId = r.CustomerId) = 1;
    PRINT CONCAT(''Linked '', @@ROWCOUNT, '' rental(s) to their unit.'');';
GO

/* ---------------------------------------------------------------------------
   5. The ledger, with names.
   --------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vDmeStockLedger AS
SELECT  m.MovementId,
        m.TenantId,
        m.LocationId,
        l.Name AS LocationName,
        m.Hcpcs,
        h.Name AS ItemName,
        m.Qty,
        m.Reason,
        m.RefType,
        m.RefId,
        m.Note,
        m.OccurredAt,
        m.CreatedBy,
        LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS CreatedByName
FROM        dbo.DmeStockMovements m
LEFT JOIN   dbo.Locations  l ON l.LocationId = m.LocationId
LEFT JOIN   dbo.HcpcsCodes h ON h.Hcpcs = m.Hcpcs AND h.TenantId = m.TenantId
LEFT JOIN   dbo.Users      u ON u.UserId = m.CreatedBy;
GO

/* ---------------------------------------------------------------------------
   6. Reconcile serialised items to their units.

   From here on a serialised item's ledger count equals its units on the shelf
   (in-stock or maintenance) at each location, because every movement of one is
   a movement of a unit. The opening balance seeded on 2026-08-25 came from the
   old HcpcsCodes.OnHand counter, which is exactly the number that was wrong
   (it said 14 oxygen concentrators when one was on the shelf). So, once, each
   serialised item at each location gets ONE adjustment that brings the ledger
   to the units that physically exist. Nothing is deleted: the opening row and
   this correction both stay, and the history explains the number.

   Consumables are NOT touched: for them the ledger is the only record there is.
   Re-running finds nothing to correct.
   --------------------------------------------------------------------------- */
;WITH keys AS (
    SELECT TenantId, Hcpcs, LocationId FROM dbo.DmeStockMovements
    UNION
    SELECT TenantId, Hcpcs, LocationId FROM dbo.DmeSerializedUnits
),
diff AS (
    SELECT k.TenantId, k.Hcpcs, k.LocationId,
           Shelf  = (SELECT COUNT(*) FROM dbo.DmeSerializedUnits u
                     WHERE u.TenantId = k.TenantId AND u.Hcpcs = k.Hcpcs AND u.LocationId = k.LocationId
                       AND u.Status IN ('in-stock','maintenance')),
           Ledger = (SELECT ISNULL(SUM(m.Qty),0) FROM dbo.DmeStockMovements m
                     WHERE m.TenantId = k.TenantId AND m.Hcpcs = k.Hcpcs AND m.LocationId = k.LocationId)
    FROM keys k
    JOIN dbo.HcpcsCodes h ON h.TenantId = k.TenantId AND h.Hcpcs = k.Hcpcs AND h.IsSerialized = 1
    WHERE k.LocationId IS NOT NULL
)
INSERT INTO dbo.DmeStockMovements (TenantId, LocationId, Hcpcs, Qty, Reason, Note)
SELECT TenantId, LocationId, Hcpcs, Shelf - Ledger, 'adjustment',
       'Reconciled to the serialised units on the shelf (2026-10-02)'
FROM diff
WHERE Shelf <> Ledger;

PRINT CONCAT('Reconciled ', @@ROWCOUNT, ' serialised item/location count(s) to their units.');
GO

/* ---------------------------------------------------------------------------
   Verify.
   --------------------------------------------------------------------------- */
DECLARE @missing NVARCHAR(400) = N'';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeStockMovements_Reason')
    SET @missing = @missing + N'CK_DmeStockMovements_Reason; ';
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeSerializedUnits_Status')
    SET @missing = @missing + N'CK_DmeSerializedUnits_Status; ';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DmeSerializedUnits_Serial')
    SET @missing = @missing + N'UX_DmeSerializedUnits_Serial; ';
IF COL_LENGTH('dbo.DmeRentals', 'UnitId') IS NULL
    SET @missing = @missing + N'DmeRentals.UnitId; ';
IF OBJECT_ID('dbo.vDmeStockLedger', 'V') IS NULL
    SET @missing = @missing + N'vDmeStockLedger; ';
IF EXISTS (
    SELECT 1 FROM dbo.DmeSerializedUnits u
    JOIN dbo.HcpcsCodes h ON h.TenantId = u.TenantId AND h.Hcpcs = u.Hcpcs AND h.IsSerialized = 1
    GROUP BY u.TenantId, u.Hcpcs, u.LocationId
    HAVING SUM(CASE WHEN u.Status IN ('in-stock','maintenance') THEN 1 ELSE 0 END)
        <> (SELECT ISNULL(SUM(m.Qty),0) FROM dbo.DmeStockMovements m
            WHERE m.TenantId = u.TenantId AND m.Hcpcs = u.Hcpcs AND m.LocationId = u.LocationId))
    SET @missing = @missing + N'serialised ledger equals units on the shelf; ';

IF @missing <> N''
    RAISERROR('Inventory incomplete: %s', 16, 1, @missing);
ELSE
    PRINT 'Verified: stock can be received, moved, corrected and returned, and serials are unique.';
GO
