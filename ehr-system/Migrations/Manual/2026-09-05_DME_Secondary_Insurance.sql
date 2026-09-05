/* ============================================================================
   Secondary insurance: two guards on a column that had none.

   dbo.DmeCustomerInsurances.Kind has always been NVARCHAR(12) with a comment
   next to it reading "primary | secondary" and nothing enforcing it. That was
   survivable while only one screen wrote the table and only ever wrote
   'primary'. It stops being survivable now that the customer form writes both.

   WHY IT MATTERS, CONCRETELY
   Six places read WHERE Kind='primary': the customer list's payer column, the
   claim raised at delivery, the claim raised from a rental month, box 11 of the
   CMS-1500, and the form itself. SQL Server's default collation is case
   insensitive so 'Primary' would still match, but 'Prim', 'PRI' or a stray
   space would not, and every one of those reads would silently find nothing.
   A claim would then be raised with a NULL payer: not an error, just a claim
   addressed to nobody, discovered weeks later by the biller.

   1. CK_DmeCustomerInsurances_Kind    the column may hold those two words only.
   2. UX_DmeCustomerInsurances_Kind    one primary and one secondary per person.

   The second is the one that changes behaviour. Everything reading this table
   says TOP 1, which is what you write when you know a customer has one primary
   and cannot prove it. A duplicate would make the form edit the lower id while
   a claim read whichever the engine returned first, and the two would disagree
   with no error anywhere. The index turns that from a silent wrong answer into
   a refused write.

   VERIFIED AGAINST THE DATA FIRST, on 2026-09-05: six rows, five primary and
   one secondary, no customer holding two of either. Nothing existing violates
   either guard, so neither is an outage.

   Not filtered, so QUOTED_IDENTIFIER does not bite here. Run with sqlcmd -I
   anyway: dbo.Users and dbo.Locations in the same session do carry filtered
   indexes.

   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- the values */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                WHERE name = 'CK_DmeCustomerInsurances_Kind')
BEGIN
    ALTER TABLE dbo.DmeCustomerInsurances
        ADD CONSTRAINT CK_DmeCustomerInsurances_Kind
        CHECK (Kind IN ('primary', 'secondary'));

    PRINT 'Added CK_DmeCustomerInsurances_Kind';
END
ELSE
    PRINT 'CK_DmeCustomerInsurances_Kind already present';
GO

/* ------------------------------------------------------------- one of each */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'UX_DmeCustomerInsurances_Kind'
                  AND object_id = OBJECT_ID('dbo.DmeCustomerInsurances'))
BEGIN
    CREATE UNIQUE INDEX UX_DmeCustomerInsurances_Kind
        ON dbo.DmeCustomerInsurances (CustomerId, Kind);

    PRINT 'Added UX_DmeCustomerInsurances_Kind';
END
ELSE
    PRINT 'UX_DmeCustomerInsurances_Kind already present';
GO

SELECT 'Insurance rows by kind' AS Summary, Kind, COUNT(*) AS Rows_
  FROM dbo.DmeCustomerInsurances
 GROUP BY Kind
 ORDER BY Kind;
GO
