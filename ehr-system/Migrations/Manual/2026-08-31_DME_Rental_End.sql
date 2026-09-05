/* ============================================================================
   Ending a rental.

   A rental could be started and never stopped. Equipment came back, a customer
   died or went into hospital, and the rental stayed 'active', going on asking
   to be billed from the Rentals screen. Billing a returned item is not an
   untidy record, it is a claim for equipment nobody has.

   It also produced a dead end: a customer with an active rental cannot be
   archived, and there was no way to end the rental, so such a customer could
   never come off the working list.

   Three facts are stored, none of them derivable:

     EndedAt     when it stopped
     EndReason   why, in the operator's words
     EndedBy     who decided

   Status is NOT stored any more. It was a second copy of "EndedAt IS NULL",
   and the pair could disagree. vDmeRentals computes it, exactly as
   vDmePayments computes IsVoided from VoidedAt and DmeDistributors derives
   IsRetired from RetiredAt.

   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* ---------- 1. the three facts ---------- */

IF COL_LENGTH('dbo.DmeRentals', 'EndedAt') IS NULL
    ALTER TABLE dbo.DmeRentals ADD EndedAt DATETIME2 NULL;
GO

IF COL_LENGTH('dbo.DmeRentals', 'EndReason') IS NULL
    ALTER TABLE dbo.DmeRentals ADD EndReason NVARCHAR(200) NULL;
GO

IF COL_LENGTH('dbo.DmeRentals', 'EndedBy') IS NULL
    ALTER TABLE dbo.DmeRentals ADD EndedBy INT NULL;
GO

/* ---------- 2. carry the old stored status across ----------
   Anything not 'active' today was ended at some point we no longer know, so it
   is dated now and said so. Nothing is invented about WHEN. */

IF COL_LENGTH('dbo.DmeRentals', 'Status') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE dbo.DmeRentals
           SET EndedAt   = SYSUTCDATETIME(),
               EndReason = ''Carried over from the old status column''
         WHERE Status <> ''active'' AND EndedAt IS NULL;';
END
GO

/* ---------- 3. Status becomes computed ----------
   The view is rebuilt first so nothing reads a column that is about to go.
   Every caller already goes through the view: the controller does not touch
   dbo.DmeRentals directly, which is what makes this swap safe. */

CREATE OR ALTER VIEW dbo.vDmeRentals AS
SELECT
    r.RentalId, r.TenantId, r.CustomerId, r.OrderId, r.Hcpcs, r.ItemName, r.Serial,
    r.MonthlyRate, r.StartDate, r.NextBillDate, r.CapMonths, r.AbnOnFile,

    -- Derived, never stored. A stored copy could disagree with EndedAt, and
    -- then two screens answer the same question differently.
    CASE WHEN r.EndedAt IS NULL THEN 'active' ELSE 'ended' END AS Status,

    r.EndedAt, r.EndReason, r.EndedBy,

    (SELECT COUNT(*) FROM dbo.DmeClaimLines cl WHERE cl.RentalId = r.RentalId) AS MonthsBilled,

    -- Whether this rental has reached the point where Medicare says the
    -- equipment belongs to the customer and billing stops. Computed for the
    -- same reason MonthsBilled is: a stored flag would drift the moment a
    -- claim line is voided.
    CASE
        WHEN r.CapMonths IS NULL OR r.CapMonths <= 0 THEN CAST(0 AS BIT)
        WHEN (SELECT COUNT(*) FROM dbo.DmeClaimLines cl WHERE cl.RentalId = r.RentalId) >= r.CapMonths
            THEN CAST(1 AS BIT)
        ELSE CAST(0 AS BIT)
    END AS CapReached,

    -- Names come back as two SEPARATE encrypted columns. Two ciphertexts joined
    -- in SQL are not decryptable by anyone with any key.
    cu.FirstName AS CustomerFirstName,
    cu.LastName  AS CustomerLastName,
    cu.LocationId,
    loc.Name AS LocationName
FROM dbo.DmeRentals r
LEFT JOIN dbo.DmeCustomers cu ON cu.CustomerId = r.CustomerId
LEFT JOIN dbo.Locations    loc ON loc.LocationId = cu.LocationId;
GO

/* Now the stored column can go. Named constraints are dropped first: a default
   on the column would block the drop, and its name is generated, so it has to
   be looked up rather than guessed. */

IF COL_LENGTH('dbo.DmeRentals', 'Status') IS NOT NULL
BEGIN
    DECLARE @df SYSNAME =
        (SELECT dc.name FROM sys.default_constraints dc
          WHERE dc.parent_object_id = OBJECT_ID('dbo.DmeRentals')
            AND dc.parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.DmeRentals'), 'Status', 'ColumnId'));

    IF @df IS NOT NULL
    BEGIN
        -- Built into a variable first: sp_executesql takes an nvarchar
        -- parameter, not an expression, so concatenating at the call site is a
        -- syntax error rather than a runtime one.
        DECLARE @dropDefault NVARCHAR(400) =
            N'ALTER TABLE dbo.DmeRentals DROP CONSTRAINT ' + QUOTENAME(@df) + N';';
        EXEC sp_executesql @dropDefault;
    END

    -- sp_executesql because a batch naming a column that no longer exists is
    -- rejected at COMPILE time, so an IF guard alone does not protect it.
    EXEC sp_executesql N'ALTER TABLE dbo.DmeRentals DROP COLUMN Status;';
END
GO

/* ---------- 4. the stock ledger learns two more reasons ----------
   'return'     equipment came back off a rental
   'receipt'    new stock bought in
   Both are positive. Until now the ledger only ever went down, because
   'delivery' was the only reason the application could write. */

PRINT 'Rental end: EndedAt, EndReason, EndedBy added; Status now computed in vDmeRentals.';
GO
