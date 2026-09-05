/* ============================================================================
   Making an ICD-10 code lookup fast enough to use.

   Typing a valid code returned "No diagnosis matches that". The code was there
   all along: the search timed out and an empty result reads the same as no
   result. Measured at 33 SECONDS for one lookup of E86.0 against 74,719 rows.

   The cause was in the WHERE clause:

       REPLACE(Code,'.','') LIKE '%E860%'

   Two things wrong at once, either of which alone defeats an index:
     - a FUNCTION on the column, so the index on Code cannot be used,
     - a LEADING WILDCARD, so even an index on the result could not seek.

   Every one of the 74,719 rows had a string built for it, on every keystroke of
   a typeahead.

   The fix is a PERSISTED computed column. The undotted code is derived by the
   database from the one it already stores, so there is still a single source of
   truth: it cannot drift, because nothing can write to it. Persisting it lets
   an index be built on it.

   The leading wildcard is dealt with in DmeIcdCatalog: codes are matched as a
   PREFIX now. Nobody searches for the middle of a diagnosis code, and "E86"
   finding E86.0, E86.1 and E86.9 is what the operator means.

   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* ---------- the derived column ----------
   PERSISTED so it can be indexed; computed so it can never disagree with Code.
   Not nullable in practice, because Code is not. */

/* CAST to the same width as Code. REPLACE() returns NVARCHAR(4000) by default,
   which makes the index key 8000 bytes: over the 1700 byte limit, so SQL Server
   accepts the index with a warning and then fails inserts for long values. The
   longest real ICD-10 code is 8 characters. */

/* An earlier run may have created the untyped version. Rebuild it rather than
   leave a working-but-warned index in place. */
IF COL_LENGTH('dbo.IcdCodes', 'CodeBare') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('dbo.IcdCodes')
                  AND name = 'CodeBare' AND max_length > 40)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'IX_IcdCodes_CodeBare' AND object_id = OBJECT_ID('dbo.IcdCodes'))
        DROP INDEX IX_IcdCodes_CodeBare ON dbo.IcdCodes;

    ALTER TABLE dbo.IcdCodes DROP COLUMN CodeBare;
END
GO

IF COL_LENGTH('dbo.IcdCodes', 'CodeBare') IS NULL
BEGIN
    ALTER TABLE dbo.IcdCodes
        ADD CodeBare AS CAST(REPLACE(Code, '.', '') AS NVARCHAR(20)) PERSISTED;
END
GO

/* ---------- the index that makes a prefix search a seek ----------
   Description is INCLUDEd so the lookup is covered: the typeahead needs the
   code and its wording, and nothing else. */

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'IX_IcdCodes_CodeBare'
                  AND object_id = OBJECT_ID('dbo.IcdCodes'))
BEGIN
    CREATE INDEX IX_IcdCodes_CodeBare
        ON dbo.IcdCodes (CodeBare)
        INCLUDE (Code, Description);
END
GO

/* ---------- the same problem, one table over ----------
   HcpcsNationalCodes is searched the same way. At 8,623 rows it measures under
   10ms today, so this is not urgent, but the shape of the query is identical
   and the table only grows. Left alone deliberately rather than changed
   without a measurement to justify it. */

PRINT 'ICD: CodeBare computed column + IX_IcdCodes_CodeBare created.';
GO
