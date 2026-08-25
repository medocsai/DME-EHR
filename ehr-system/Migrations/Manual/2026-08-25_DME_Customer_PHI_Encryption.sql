/* ============================================================================
   DME Customer PHI Encryption  (DMEEHR database)

   WHY
   Clinical patient records in this same database are AES-GCM encrypted at rest.
   DmeCustomers was plaintext:

     SELECT TOP 2 FirstName, LastName, SsnLast4, Phone FROM dbo.DmeCustomers
       John      Doe    4821  (214) 555-0101
       Margaret  Ellis  7723  (214) 555-0144

     SELECT TOP 2 FirstName, LastName FROM dbo.Patients
       DmBmQNKaUVRaaB1o7QQ9fKkpv   /5kuL7O3USFb3lG3l2vmHx92Y

   A DME customer is a patient by any HIPAA definition. Which product created
   the row should not decide whether their name and address are readable to
   anyone with a database connection or a stolen backup file.

   WHAT THIS DOES
   1. Widens the PHI columns, because ciphertext is longer than plaintext.
      SsnLast4 NVARCHAR(4) cannot physically hold an encrypted value, and State
      NVARCHAR(4) has the same problem. This step is required before any
      encryption is written, or the write fails with a truncation error.
   2. Creates DmeCustomerSearchTokens, the blind index that keeps the customer
      search working once names are ciphertext (LIKE cannot match ciphertext).
      Same approach as the clinical PatientSearchTokens table.

   The encryption of the existing rows is NOT done here. It runs from the
   application, through the same EncryptionHelper the clinical side uses, so
   there is one implementation of the cryptography and the SQL never needs the
   key. See /Dme/BackfillPhi.

   Idempotent: safe to re-run.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* ---------------------------------------------------------------------------
   1. Widen the PHI columns to hold ciphertext

   AES-GCM output is base64 of nonce + ciphertext + tag, so even a 4-character
   value becomes ~60 characters. Sized at 512 to leave room without thinking
   about it again.
   --------------------------------------------------------------------------- */
DECLARE @col SYSNAME, @sql NVARCHAR(MAX);
DECLARE widen CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES
        ('FirstName'), ('LastName'), ('Phone'), ('Email'),
        ('AddressLine1'), ('City'), ('State'), ('Zip'),
        ('EmergencyName'), ('EmergencyRel'), ('EmergencyPhone'), ('SsnLast4')
    ) AS x(name);

OPEN widen;
FETCH NEXT FROM widen INTO @col;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns c
               WHERE c.object_id = OBJECT_ID('dbo.DmeCustomers')
                 AND c.name = @col
                 AND (c.max_length / 2) < 512)
    BEGIN
        -- Preserve nullability: FirstName and LastName are NOT NULL.
        DECLARE @nullable BIT = (SELECT is_nullable FROM sys.columns
                                 WHERE object_id = OBJECT_ID('dbo.DmeCustomers') AND name = @col);
        SET @sql = 'ALTER TABLE dbo.DmeCustomers ALTER COLUMN ' + QUOTENAME(@col) +
                   ' NVARCHAR(512) ' + CASE WHEN @nullable = 1 THEN 'NULL' ELSE 'NOT NULL' END + ';';
        EXEC sp_executesql @sql;
        PRINT 'Widened DmeCustomers.' + @col + ' for ciphertext';
    END
    FETCH NEXT FROM widen INTO @col;
END
CLOSE widen; DEALLOCATE widen;
GO

/* ---------------------------------------------------------------------------
   1a. Dob becomes a string column so it can hold ciphertext

   Date of birth is a HIPAA identifier and belongs with the rest of the PHI, but
   a DATE column physically cannot store an encrypted value. The column is
   converted to NVARCHAR.

   Converted via an explicit CONVERT(..., 23) rather than a plain ALTER COLUMN,
   because an implicit DATE-to-string conversion uses the session's date format:
   the same migration run on a machine with a different regional setting would
   silently produce 12/04/1958 instead of 1958-04-12, and every age on every
   screen would then be wrong for half the customers. ISO 8601 also parses
   unambiguously back in .NET regardless of culture.

   Verified safe first: Dob is display-only in this product (F.Date and F.Age).
   Nothing sorts, filters or joins on it, so losing the DATE type costs nothing
   here. If a future feature needs to query by age, that wants a separate
   derived column or a blind index, not a plaintext birth date.
   --------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.columns c
           JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID('dbo.DmeCustomers') AND c.name = 'Dob' AND t.name = 'date')
BEGIN
    ALTER TABLE dbo.DmeCustomers ADD DobText NVARCHAR(512) NULL;

    EXEC sp_executesql N'UPDATE dbo.DmeCustomers SET DobText = CONVERT(varchar(10), Dob, 23) WHERE Dob IS NOT NULL;';

    ALTER TABLE dbo.DmeCustomers DROP COLUMN Dob;
    EXEC sp_rename 'dbo.DmeCustomers.DobText', 'Dob', 'COLUMN';
    PRINT 'Converted DmeCustomers.Dob to NVARCHAR(512) (ISO 8601) for encryption';
END
GO

/* ---------------------------------------------------------------------------
   1b. DmeClaims.CustomerName also holds ciphertext

   That column is deliberately stored rather than derived (a submitted claim is
   a document as filed, and must not be rewritten when the customer is renamed),
   but it is still a patient name and still PHI. Widen it for the same reason as
   the columns above.
   --------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DmeClaims') AND name = 'CustomerName' AND (max_length / 2) < 512)
BEGIN
    ALTER TABLE dbo.DmeClaims ALTER COLUMN CustomerName NVARCHAR(512) NULL;
    PRINT 'Widened DmeClaims.CustomerName for ciphertext';
END
GO

/* ---------------------------------------------------------------------------
   2. Blind index for customer search

   Stores keyed HMAC hashes of every prefix of the searchable fields. A search
   for "mar" hashes the term the same way and matches, so the database never
   holds a searchable copy of the name itself.

   TokenHash is not unique: two customers can share a prefix, which is the
   point. The index is on (TenantId, TokenHash) because that is the lookup.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmeCustomerSearchTokens','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeCustomerSearchTokens (
        TokenId      INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId   INT           NOT NULL,
        TenantId     INT           NOT NULL CONSTRAINT DF_DmeCustTok_Tenant DEFAULT 1,
        TokenHash    NVARCHAR(64)  NOT NULL,
        FieldType    NVARCHAR(20)  NOT NULL,   -- FirstName | LastName | FullName | Phone
        PrefixLength INT           NOT NULL
    );
    CREATE INDEX IX_DmeCustomerSearchTokens_Lookup ON dbo.DmeCustomerSearchTokens(TenantId, TokenHash);
    CREATE INDEX IX_DmeCustomerSearchTokens_Customer ON dbo.DmeCustomerSearchTokens(CustomerId);
    PRINT 'Created DmeCustomerSearchTokens';
END
GO

/* Same tenant isolation as every other DME table. Via sp_executesql because
   ALTER SECURITY POLICY is validated at compile time, so an IF guard alone
   does not survive a re-run. */
IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeCustomerSearchTokens'))
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
            ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerSearchTokens,
            ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerSearchTokens AFTER INSERT,
            ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerSearchTokens AFTER UPDATE;';
    PRINT 'DmeCustomerSearchTokens added to TenantIsolationPolicy';
END
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT c.name AS Column_, c.max_length / 2 AS MaxChars, c.is_nullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.DmeCustomers')
  AND c.name IN ('FirstName','LastName','Phone','Email','AddressLine1','City','State','Zip',
                 'EmergencyName','EmergencyRel','EmergencyPhone','SsnLast4')
ORDER BY c.name;
GO
