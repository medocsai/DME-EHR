SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   Customer documents: the insurance card and the photo ID.

   WHY THIS EXISTS

   Intake is when somebody is holding the insurance card and the ID, and the
   New Customer screen had nowhere to put either.

   WHY ONLY THOSE TWO

   Everything that defends a CLAIM (written order, CMN, medical records, prior
   authorisation, proof of delivery) is per item and per date span, so it lives
   on the ORDER in dbo.DmeOrderDocuments (2026-09-08_DME_Document_Categories).
   The card and the ID describe the person, not any one order. The Kind list is
   those two and nothing else, held by CK_DmeCustomerDocuments_Kind and by
   Services/DmeCustomerDocuments.cs.

   WHAT IS STORED

   FileName is CIPHERTEXT (the original name is PHI). StoragePath is opaque.
   FileHash is SHA-256 of the PLAINTEXT, taken before encryption. No IsDeleted
   (derived from DeletedAt by the view) and no LocationId (derived from the
   customer). Bytes go through DmeDocumentStore, the same rules as order
   documents.

   A DATABASE THAT ALREADY HAS THE TABLE

   The unmerged dme/corrections-and-permissions branch created this table with
   six kinds (cmn, rx, referral, other as well). If it exists, the constraint is
   narrowed to two. Rows filed under any other kind are NOT deleted or
   relabelled here: a CMN filed against a customer has to be moved to the right
   order by a person, so the script stops and says how many.

   Run with sqlcmd -I. Safe to re-run.
   =========================================================================== */

IF OBJECT_ID('dbo.DmeCustomerDocuments','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeCustomerDocuments (
        DocumentId       INT IDENTITY(1,1) PRIMARY KEY,
        TenantId         INT            NOT NULL,
        CustomerId       INT            NOT NULL,
        Kind             NVARCHAR(20)   NOT NULL,
        FileName         NVARCHAR(512)  NOT NULL,   -- ciphertext
        StoragePath      NVARCHAR(400)  NOT NULL,   -- opaque object key
        ContentType      NVARCHAR(120)  NOT NULL,
        FileSize         BIGINT         NOT NULL,   -- plaintext bytes
        FileHash         CHAR(64)       NOT NULL,   -- SHA-256 of the plaintext
        UploadedByUserId INT            NULL,
        UploadedAt       DATETIME2      NOT NULL CONSTRAINT DF_DmeCustomerDocuments_UploadedAt DEFAULT SYSUTCDATETIME(),
        DeletedAt        DATETIME2      NULL,       -- the only deletion fact

        CONSTRAINT FK_DmeCustomerDocuments_Customer
            FOREIGN KEY (CustomerId) REFERENCES dbo.DmeCustomers (CustomerId),
        CONSTRAINT CK_DmeCustomerDocuments_Kind
            CHECK (Kind IN ('insurance-card','id'))
    );

    CREATE INDEX IX_DmeCustomerDocuments_Customer
        ON dbo.DmeCustomerDocuments (CustomerId, TenantId);

    PRINT 'dbo.DmeCustomerDocuments created.';
END
ELSE
    PRINT 'dbo.DmeCustomerDocuments already exists.';
GO

/* ---------------------------------------------------------------------------
   Narrow an inherited six-kind constraint to two.
   --------------------------------------------------------------------------- */
DECLARE @other INT = (
    SELECT COUNT(*) FROM dbo.DmeCustomerDocuments
    WHERE Kind NOT IN ('insurance-card','id'));

IF @other > 0
BEGIN
    RAISERROR('%d customer document(s) are filed under a kind other than insurance-card or id. Those belong on an order (DmeOrderDocuments). Move them, then re-run.', 16, 1, @other);
    RETURN;
END

DECLARE @def NVARCHAR(MAX) = (
    SELECT definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID('dbo.DmeCustomerDocuments')
      AND name = 'CK_DmeCustomerDocuments_Kind');

IF @def IS NOT NULL AND (@def LIKE '%cmn%' OR @def LIKE '%other%')
BEGIN
    EXEC sp_executesql N'ALTER TABLE dbo.DmeCustomerDocuments DROP CONSTRAINT CK_DmeCustomerDocuments_Kind;';
    EXEC sp_executesql N'ALTER TABLE dbo.DmeCustomerDocuments ADD CONSTRAINT CK_DmeCustomerDocuments_Kind CHECK (Kind IN (''insurance-card'',''id''));';
    PRINT 'CK_DmeCustomerDocuments_Kind narrowed to insurance-card and id.';
END
GO

/* ---------------------------------------------------------------------------
   Row level security. sp_executesql because ALTER SECURITY POLICY is validated
   at compile time and an IF guard does not protect a bare statement.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeCustomerDocuments')
      AND sp.predicate_type_desc = 'FILTER')
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerDocuments;';
    PRINT 'FILTER predicate added on DmeCustomerDocuments.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeCustomerDocuments')
      AND sp.predicate_type_desc = 'BLOCK')
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerDocuments AFTER INSERT;';
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomerDocuments AFTER UPDATE;';
    PRINT 'BLOCK predicates added on DmeCustomerDocuments.';
END
GO

/* ---------------------------------------------------------------------------
   The live documents on a customer. The view excludes removed rows, which IS
   the removal mechanism, exactly as vDmeOrderDocuments does it.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.vDmeCustomerDocuments','V') IS NOT NULL DROP VIEW dbo.vDmeCustomerDocuments;
GO
CREATE VIEW dbo.vDmeCustomerDocuments
AS
SELECT  d.DocumentId,
        d.TenantId,
        d.CustomerId,
        c.LocationId,          -- derived from the customer, never stored here
        d.Kind,
        d.FileName,            -- ciphertext, decrypted in the app
        d.StoragePath,
        d.ContentType,
        d.FileSize,
        d.FileHash,
        d.UploadedByUserId,
        LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS UploadedByName,
        d.UploadedAt
FROM        dbo.DmeCustomerDocuments d
JOIN        dbo.DmeCustomers c ON c.CustomerId = d.CustomerId
LEFT JOIN   dbo.Users        u ON u.UserId     = d.UploadedByUserId
WHERE       d.DeletedAt IS NULL;
GO

/* ---------------------------------------------------------------------------
   Verify.
   --------------------------------------------------------------------------- */
DECLARE @missing NVARCHAR(400) = N'';

IF OBJECT_ID('dbo.vDmeCustomerDocuments','V') IS NULL
    SET @missing = @missing + N'vDmeCustomerDocuments; ';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_DmeCustomerDocuments_Kind'
                 AND definition NOT LIKE '%cmn%')
    SET @missing = @missing + N'CK_DmeCustomerDocuments_Kind (two kinds); ';

IF (SELECT COUNT(*) FROM sys.security_predicates
    WHERE target_object_id = OBJECT_ID('dbo.DmeCustomerDocuments')) < 3
    SET @missing = @missing + N'RLS predicates; ';

IF @missing <> N''
    RAISERROR('Customer documents incomplete: %s', 16, 1, @missing);
ELSE
    PRINT 'Verified: an insurance card and a photo ID can be filed against a customer, and nothing else.';
GO
