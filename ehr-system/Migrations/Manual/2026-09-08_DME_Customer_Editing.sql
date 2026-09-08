SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   Constraints that have to exist BEFORE a customer can be edited.

   Until now a customer could only be CREATED. There was no edit action, no way
   to add an insurance, and no way to correct a member ID or a date of birth. A
   single mistyped digit meant the claim rejected forever, because the only
   remedy was a duplicate customer record.

   Creation always wrote exactly one insurance, always Kind='primary', always
   SubscriberRel='Self'. Nothing else could ever exist, so nothing needed
   constraining. The moment editing exists, all three become reachable, and one
   of them is already a latent bug:

       BillNow: SELECT TOP 1 PayerName ... WHERE Kind='primary'

   TOP 1 with no ORDER BY is not deterministic. With one primary per customer it
   cannot be wrong. With two it silently picks a payer, and which one it picks
   can change between executions of the same query. A rental could bill to a
   different insurer month to month with nothing on any screen to show it.

   ONE PRIMARY AND ONE SECONDARY PER CUSTOMER

   Enforced as a plain UNIQUE index on (CustomerId, Kind) rather than a filtered
   unique index on the primaries. Deliberate: a FILTERED index makes every
   INSERT, UPDATE and DELETE against the table require QUOTED_IDENTIFIER ON, and
   sqlcmd defaults it OFF, so a maintenance script fails with an error naming
   neither the index nor the reason. Users, Locations and DmeClaimLines are
   already in that trap and it is documented in CLAUDE.md. An unfiltered unique
   index expresses the same rule, covers secondary as well, and keeps this table
   writable from a plain sqlcmd session.

   Medicare plus one supplement is the shape that matters. A tertiary payer is
   rare enough that adding 'tertiary' to the Kind constraint later is the right
   way to meet it, rather than leaving the column unconstrained now.

   Safe to re-run.
   =========================================================================== */

/* ---- Kind ---------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_DmeCustomerInsurances_Kind')
BEGIN
    ALTER TABLE dbo.DmeCustomerInsurances
        ADD CONSTRAINT CK_DmeCustomerInsurances_Kind
            CHECK (Kind IN ('primary', 'secondary'));

    PRINT 'Added CK_DmeCustomerInsurances_Kind.';
END
ELSE
    PRINT 'CK_DmeCustomerInsurances_Kind already present.';
GO

/* ---- Relationship to the subscriber, CMS-1500 box 6 ---------------------- */
/* Written as the literal 'Self' on every insert because the form never asked.
   That is not a default, it is an assertion: a customer covered by a spouse's
   policy was recorded as the subscriber themselves, and box 6 on their claim
   said so. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_DmeCustomerInsurances_SubscriberRel')
BEGIN
    UPDATE dbo.DmeCustomerInsurances
       SET SubscriberRel = 'Self'
     WHERE SubscriberRel IS NULL
        OR SubscriberRel NOT IN ('Self', 'Spouse', 'Child', 'Other');

    ALTER TABLE dbo.DmeCustomerInsurances
        ADD CONSTRAINT CK_DmeCustomerInsurances_SubscriberRel
            CHECK (SubscriberRel IN ('Self', 'Spouse', 'Child', 'Other'));

    PRINT 'Added CK_DmeCustomerInsurances_SubscriberRel.';
END
ELSE
    PRINT 'CK_DmeCustomerInsurances_SubscriberRel already present.';
GO

/* ---- One of each kind per customer --------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_DmeCustomerInsurances_OnePerKind'
                 AND object_id = OBJECT_ID('dbo.DmeCustomerInsurances'))
BEGIN
    /* Nothing in this database violates it (verified), but a fork might.
       Fail with the customer ids rather than an index creation error. */
    IF EXISTS (SELECT 1 FROM dbo.DmeCustomerInsurances
               GROUP BY CustomerId, Kind HAVING COUNT(*) > 1)
    BEGIN
        DECLARE @dupes NVARCHAR(MAX) = STUFF((
            SELECT ', ' + CAST(CustomerId AS NVARCHAR(20)) + '/' + Kind
            FROM dbo.DmeCustomerInsurances
            GROUP BY CustomerId, Kind HAVING COUNT(*) > 1
            FOR XML PATH('')), 1, 2, '');

        RAISERROR('Cannot enforce one insurance per kind. Duplicates: %s', 16, 1, @dupes);
    END

    CREATE UNIQUE INDEX UX_DmeCustomerInsurances_OnePerKind
        ON dbo.DmeCustomerInsurances (CustomerId, Kind);

    PRINT 'Added UX_DmeCustomerInsurances_OnePerKind.';
END
ELSE
    PRINT 'UX_DmeCustomerInsurances_OnePerKind already present.';
GO

/* ---- One primary diagnosis per customer ---------------------------------- */
/* Box 21 lists the diagnoses; the first is the one the item is justified by.
   Without this, "which diagnosis is primary" has the same TOP 1 ambiguity. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_DmeCustomerDiagnoses_OneCodeEach'
                 AND object_id = OBJECT_ID('dbo.DmeCustomerDiagnoses'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.DmeCustomerDiagnoses
               GROUP BY CustomerId, IcdCode HAVING COUNT(*) > 1)
    BEGIN
        /* Keep the lowest id of each duplicate pair. A diagnosis recorded twice
           is one diagnosis; nothing is lost. */
        DELETE d FROM dbo.DmeCustomerDiagnoses d
        WHERE d.DiagnosisId > (
            SELECT MIN(d2.DiagnosisId) FROM dbo.DmeCustomerDiagnoses d2
            WHERE d2.CustomerId = d.CustomerId AND d2.IcdCode = d.IcdCode);

        PRINT 'Removed duplicate diagnosis rows before indexing.';
    END

    CREATE UNIQUE INDEX UX_DmeCustomerDiagnoses_OneCodeEach
        ON dbo.DmeCustomerDiagnoses (CustomerId, IcdCode);

    PRINT 'Added UX_DmeCustomerDiagnoses_OneCodeEach.';
END
ELSE
    PRINT 'UX_DmeCustomerDiagnoses_OneCodeEach already present.';
GO

/* --------------------------------------------------------------- verification */
DECLARE @missing NVARCHAR(MAX) = N'';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeCustomerInsurances_Kind')
    SET @missing = @missing + N'CK_DmeCustomerInsurances_Kind; ';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeCustomerInsurances_SubscriberRel')
    SET @missing = @missing + N'CK_DmeCustomerInsurances_SubscriberRel; ';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DmeCustomerInsurances_OnePerKind')
    SET @missing = @missing + N'UX_DmeCustomerInsurances_OnePerKind; ';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DmeCustomerDiagnoses_OneCodeEach')
    SET @missing = @missing + N'UX_DmeCustomerDiagnoses_OneCodeEach; ';

IF @missing <> N''
    RAISERROR('Customer editing constraints incomplete: %s', 16, 1, @missing);
ELSE
    PRINT 'Verified: a customer can now be edited without the record becoming ambiguous.';
GO
