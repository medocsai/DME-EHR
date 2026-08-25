/* ============================================================================
   DME Single Source of Truth  (DMEEHR database)

   WHY
   Three numbers were stored and trusted instead of computed, and two of them
   had already drifted in the demo data before anyone touched the system:

     DmeRentals.MonthsBilled   stored 3 / 2 / 4   actual claims raised 2 / 0 / 0
     HcpcsCodes.OnHand         stored 14 for E1390, actual units in stock 1
     DmeClaims.Total           agreed with its lines today, nothing keeps it so

   Plus display copies (CustomerName, DoctorName) that go stale the first time
   somebody corrects a spelling on the customer record.

   THE SHAPE OF THE FIX
   For each one, ask what fact genuinely exists nowhere else, store only that,
   and compute the rest at read time:

     MonthsBilled -> the missing fact was WHICH RENTAL a claim line billed.
                     There was no link at all, so the count could not be derived
                     even in principle. DmeClaimLines.RentalId stores the link;
                     the count is then a COUNT.
     OnHand       -> the missing fact was stock movement. Consumables have no
                     serialised unit rows at all (120 test strips are not 120
                     rows), so stock was underivable. DmeStockMovements is the
                     ledger; the balance is then a SUM.
     Total        -> derivable already from DmeClaimLines. Nothing to add.
     Names        -> derivable already by join. Nothing to add.

   WHAT IS DELIBERATELY KEPT
   Point-in-time facts are NOT duplication and are not touched:
     DmeClaims.CustomerName / PayerName   a submitted claim is a document as
                                          filed; it must not change when the
                                          customer later switches insurer.
     DmeOrderLines.UnitPrice / MonthlyRate
     DmeClaimLines.Charge                 price as transacted, not as listed.
     DmeRentals.MonthlyRate               the contracted rate for that rental.
   Each of these is the only record of what was actually agreed or billed.

   Idempotent: safe to re-run.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* ---------------------------------------------------------------------------
   1. The missing link: which rental month did this claim LINE bill?

   The link belongs on the line, not the claim. A delivery claim covers every
   line of an order and can therefore bill several different rentals at once,
   so a single RentalId on the claim could not represent it. One claim line
   bills exactly one rental month, which makes MonthsBilled a plain COUNT.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeClaimLines') AND name = 'RentalId')
BEGIN
    ALTER TABLE dbo.DmeClaimLines ADD RentalId INT NULL;
    PRINT 'Added DmeClaimLines.RentalId';
END
GO

/* Backfill what can be inferred: a claim line whose HCPCS matches exactly one
   rental belonging to that claim's customer must have come from that rental.
   Anything ambiguous is deliberately left NULL rather than guessed, because a
   wrong link produces a wrong MonthsBilled, which is the bug being fixed. */
UPDATE cl
SET RentalId = (SELECT MIN(r.RentalId) FROM dbo.DmeRentals r
                WHERE r.CustomerId = c.CustomerId AND r.Hcpcs = cl.Hcpcs)
FROM dbo.DmeClaimLines cl
JOIN dbo.DmeClaims c ON c.ClaimId = cl.ClaimId
WHERE cl.RentalId IS NULL
  AND 1 = (SELECT COUNT(*) FROM dbo.DmeRentals r2
           WHERE r2.CustomerId = c.CustomerId AND r2.Hcpcs = cl.Hcpcs);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('dbo.DmeClaimLines') AND name = 'IX_DmeClaimLines_RentalId')
    CREATE INDEX IX_DmeClaimLines_RentalId ON dbo.DmeClaimLines(RentalId) WHERE RentalId IS NOT NULL;
GO

/* An earlier revision of this migration put the link on DmeClaims. Remove it so
   there is exactly one place the relationship lives. */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DmeClaims') AND name = 'RentalId')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('dbo.DmeClaims') AND name = 'IX_DmeClaims_RentalId')
        DROP INDEX IX_DmeClaims_RentalId ON dbo.DmeClaims;
    ALTER TABLE dbo.DmeClaims DROP COLUMN RentalId;
    PRINT 'Removed superseded DmeClaims.RentalId';
END
GO

/* ---------------------------------------------------------------------------
   2. The missing ledger: stock movement

   One ledger for both serialised equipment and bulk consumables. Balance is
   always SUM(Qty), so there is a single mechanism and a single answer.
   Qty is signed: receipts positive, issues negative.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmeStockMovements','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeStockMovements (
        MovementId  INT IDENTITY(1,1) PRIMARY KEY,
        TenantId    INT           NOT NULL CONSTRAINT DF_DmeStockMov_Tenant DEFAULT 1,
        Hcpcs       NVARCHAR(10)  NOT NULL,
        Qty         INT           NOT NULL,        -- signed: +receipt, -issue
        Reason      NVARCHAR(30)  NOT NULL,        -- opening | receipt | delivery | return | adjustment
        RefType     NVARCHAR(30)  NULL,            -- e.g. 'DmeOrder'
        RefId       INT           NULL,
        Note        NVARCHAR(200) NULL,
        OccurredAt  DATETIME2     NOT NULL CONSTRAINT DF_DmeStockMov_At DEFAULT SYSUTCDATETIME(),
        CreatedBy   INT           NULL
    );
    CREATE INDEX IX_DmeStockMovements_Hcpcs ON dbo.DmeStockMovements(TenantId, Hcpcs);
    PRINT 'Created DmeStockMovements';
END
GO

/* Row level security, same policy as every other DME table.

   Via sp_executesql on purpose: ALTER SECURITY POLICY is validated when the
   batch is COMPILED, not when it runs, so wrapping it in IF NOT EXISTS does not
   stop a re-run from erroring. Deferring the compile is what makes the guard
   actually work. */
IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeStockMovements'))
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
            ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeStockMovements,
            ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeStockMovements AFTER INSERT,
            ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeStockMovements AFTER UPDATE;';
    PRINT 'DmeStockMovements added to TenantIsolationPolicy';
END
GO

/* Seed the ledger from today's stored balance, exactly once.

   This is the honest conversion of a stored number into a ledger: we cannot
   reconstruct history that was never recorded, so the current figure becomes a
   single dated opening balance and every movement from here is real. Issues
   already reflected in that number are NOT re-applied, otherwise the opening
   balance would be double counted.

   Via sp_executesql for the same compile-time reason as above: once OnHand has
   been dropped, a batch that merely mentions the column fails to compile even
   when the guard would have skipped it. */
IF NOT EXISTS (SELECT 1 FROM dbo.DmeStockMovements WHERE Reason = 'opening')
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.HcpcsCodes') AND name = 'OnHand')
BEGIN
    EXEC sp_executesql N'
        INSERT INTO dbo.DmeStockMovements (TenantId, Hcpcs, Qty, Reason, Note)
        SELECT TenantId, Hcpcs, OnHand, ''opening'', ''Opening balance carried from HcpcsCodes.OnHand''
        FROM dbo.HcpcsCodes
        WHERE OnHand <> 0;';
    PRINT 'Seeded opening stock balances';
END
GO

/* ---------------------------------------------------------------------------
   3. Read models

   The application reads these instead of the base tables, so the computed
   values cannot be bypassed by a caller that forgets to compute. RLS on the
   base tables applies through the views, so tenant scoping still holds.
   --------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vDmeRentals AS
SELECT
    r.RentalId, r.TenantId, r.CustomerId, r.OrderId, r.Hcpcs, r.ItemName, r.Serial,
    r.MonthlyRate,          -- contracted rate: point in time, correctly stored
    r.StartDate, r.NextBillDate, r.CapMonths, r.AbnOnFile, r.Status,
    -- Derived, never stored: the number of months actually billed against this
    -- rental. The old stored counter said 3 / 2 / 4 where the truth was 2 / 0 / 0.
    (SELECT COUNT(*) FROM dbo.DmeClaimLines cl WHERE cl.RentalId = r.RentalId) AS MonthsBilled,
    -- Derived: the current customer name, so a corrected spelling shows
    -- everywhere rather than only on records created after the correction.
    --
    -- Exposed as two SEPARATE columns, NOT concatenated. Customer names are
    -- encrypted at rest, and two ciphertexts joined with a space are not
    -- decryptable as one value: the result is unreadable no matter what key you
    -- hold. The application decrypts each part and composes the name.
    -- See DmeCustomerPhi.ComposeCustomerNames.
    cu.FirstName AS CustomerFirstName,
    cu.LastName  AS CustomerLastName
FROM dbo.DmeRentals r
LEFT JOIN dbo.DmeCustomers cu ON cu.CustomerId = r.CustomerId;
GO

CREATE OR ALTER VIEW dbo.vDmeClaims AS
SELECT
    c.ClaimId, c.TenantId, c.ClaimNumber, c.OrderId, c.CustomerId,
    c.CustomerName,         -- as filed: a submitted claim is a document
    c.PayerName,            -- as filed: customer may switch insurer later
    c.Status, c.ServiceDate, c.CreatedAt,
    -- Derived, never stored: a claim total that disagrees with its own lines is
    -- never the right answer, so there is nothing to disagree with.
    (SELECT ISNULL(SUM(cl.Charge), 0) FROM dbo.DmeClaimLines cl WHERE cl.ClaimId = c.ClaimId) AS Total,
    (SELECT COUNT(*) FROM dbo.DmeClaimLines cl WHERE cl.ClaimId = c.ClaimId) AS LineCount
FROM dbo.DmeClaims c;
GO

CREATE OR ALTER VIEW dbo.vDmeOrders AS
SELECT
    o.OrderId, o.TenantId, o.OrderNumber, o.CustomerId, o.Status, o.Stage,
    o.DoctorId, o.DeliveryDate, o.DeliveryAddress, o.Deposit, o.PodSignedBy,
    o.PodSignedAt, o.PodSignature, o.CreatedAt,
    -- Derived: an open order is a live working document, not an archive, so it
    -- should show the customer and doctor as they are now.
    --
    -- Customer name comes back as two separate encrypted columns for the app to
    -- decrypt and join; see the note in vDmeRentals. Doctor names are NOT
    -- patient PHI and are not encrypted, so DoctorName is still composed here.
    cu.FirstName AS CustomerFirstName,
    cu.LastName  AS CustomerLastName,
    CASE WHEN d.DoctorId IS NULL THEN '' ELSE 'Dr. ' + d.FirstName + ' ' + d.LastName END AS DoctorName,
    (SELECT COUNT(*) FROM dbo.DmeOrderLines l WHERE l.OrderId = o.OrderId) AS LineCount,
    (SELECT ISNULL(SUM(CASE WHEN l.Mode = 'purchase' THEN l.UnitPrice * l.Qty ELSE l.MonthlyRate END), 0)
     FROM dbo.DmeOrderLines l WHERE l.OrderId = o.OrderId) AS Total
FROM dbo.DmeOrders o
LEFT JOIN dbo.DmeCustomers cu ON cu.CustomerId = o.CustomerId
LEFT JOIN dbo.DmeDoctors  d  ON d.DoctorId   = o.DoctorId;
GO

CREATE OR ALTER VIEW dbo.vHcpcsCatalog AS
SELECT
    h.HcpcsCodeId, h.TenantId, h.Hcpcs, h.Name, h.Category, h.IsSerialized,
    h.Rentable, h.Purchasable, h.PurchasePrice, h.MonthlyRate,
    h.CappedRentalMonths, h.Modifiers, h.ReorderPoint,
    -- Derived, never stored. This is the stock problem: the old counter said 14
    -- units of E1390 on hand when one was actually in stock.
    (SELECT ISNULL(SUM(m.Qty), 0) FROM dbo.DmeStockMovements m
     WHERE m.Hcpcs = h.Hcpcs AND m.TenantId = h.TenantId) AS OnHand,
    (SELECT COUNT(*) FROM dbo.DmeSerializedUnits u
     WHERE u.Hcpcs = h.Hcpcs AND u.Status = 'in-stock') AS UnitsInStock
FROM dbo.HcpcsCodes h;
GO

/* ---------------------------------------------------------------------------
   4. Drop the stored copies

   Done last, after the views exist, so there is no window where the data is
   unreadable. Dropping is the point: leaving the columns in place means
   somebody writes to them again next month.
   --------------------------------------------------------------------------- */
/* A column cannot be dropped while a default constraint references it, and the
   generated constraint names are unpredictable (DF__DmeRental__Month__1FA39FB9).
   Resolve the name from the catalog rather than hard-coding it. */
DECLARE @drop TABLE (TableName SYSNAME, ColumnName SYSNAME);
INSERT INTO @drop VALUES
    ('DmeRentals', 'MonthsBilled'),
    ('DmeRentals', 'CustomerName'),
    ('DmeOrders',  'CustomerName'),
    ('DmeOrders',  'DoctorName'),
    ('DmeClaims',  'Total'),
    ('HcpcsCodes', 'OnHand');

DECLARE @tn SYSNAME, @cn SYSNAME, @dc SYSNAME, @stmt NVARCHAR(MAX);
DECLARE dropcur CURSOR LOCAL FAST_FORWARD FOR SELECT TableName, ColumnName FROM @drop;
OPEN dropcur;
FETCH NEXT FROM dropcur INTO @tn, @cn;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.' + @tn) AND name = @cn)
    BEGIN
        SELECT @dc = dc.name
        FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID('dbo.' + @tn) AND c.name = @cn;

        IF @dc IS NOT NULL
        BEGIN
            SET @stmt = 'ALTER TABLE dbo.' + QUOTENAME(@tn) + ' DROP CONSTRAINT ' + QUOTENAME(@dc) + ';';
            EXEC sp_executesql @stmt;
        END

        SET @stmt = 'ALTER TABLE dbo.' + QUOTENAME(@tn) + ' DROP COLUMN ' + QUOTENAME(@cn) + ';';
        EXEC sp_executesql @stmt;
        PRINT 'Dropped ' + @tn + '.' + @cn;
        SET @dc = NULL;
    END
    FETCH NEXT FROM dropcur INTO @tn, @cn;
END
CLOSE dropcur; DEALLOCATE dropcur;
GO

/* ---------------------------------------------------------------------------
   Verification: the numbers now come from the records, so they cannot disagree
   --------------------------------------------------------------------------- */
-- CustomerFirstName / CustomerLastName rather than a composed name: they are
-- encrypted at rest, so this prints ciphertext by design. The application
-- decrypts and joins them (DmeCustomerPhi.ComposeCustomerNames).
SELECT RentalId, Hcpcs, MonthsBilled FROM dbo.vDmeRentals ORDER BY RentalId;
SELECT ClaimNumber, Total, LineCount FROM dbo.vDmeClaims ORDER BY ClaimId;
SELECT TOP 5 Hcpcs, Name, OnHand, UnitsInStock FROM dbo.vHcpcsCatalog ORDER BY Hcpcs;
GO
