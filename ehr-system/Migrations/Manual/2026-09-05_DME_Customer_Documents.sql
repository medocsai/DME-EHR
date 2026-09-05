/* ============================================================================
   Customer documents: the CMN, the prescription, the insurance card, the ID.

   The Attachments panel on the New Customer screen has been demo UI since the
   product was written. It let an operator pick a file, listed it, and stored
   nothing at all. The list read exactly like a saved document, which is the
   worst version of that bug: an intake clerk attaches the referral, sees it on
   the screen, and the referral is gone the moment the page navigates.

   This is the same machinery proof of delivery already uses, against the same
   IFileStorageService and the same EncryptionHelper, with one addition: a
   customer document has a KIND. A biller looking for the CMN that justifies an
   oxygen claim is not looking for "a file"; DmeOrderDocuments needs no such
   column because every row in it is the same thing.

   WHAT IS STORED, AND WHAT IS NOT
   FileName is CIPHERTEXT: the original name routinely reads "margaret-ellis-
   cmn.pdf". StoragePath is opaque so a bucket listing reveals nothing. FileHash
   is SHA-256 of the PLAINTEXT, taken before encryption, so the document stays
   checkable across a key rotation. There is no IsDeleted and no LocationId:
   the first is derived from DeletedAt by the view, the second from the customer.

   Run with sqlcmd -I.
   Safe to re-run.
   ============================================================================ */

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------------------
   The table.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmeCustomerDocuments','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeCustomerDocuments (
        DocumentId       INT IDENTITY(1,1) PRIMARY KEY,
        TenantId         INT            NOT NULL,
        CustomerId       INT            NOT NULL,
        -- What the document IS. Constrained below, because six months from now
        -- a screen filtering for 'cmn' must not silently miss rows filed as
        -- 'CMN ' or 'Cmn'. Same lesson as DmeCustomerInsurances.Kind.
        Kind             NVARCHAR(20)   NOT NULL,
        -- Ciphertext. The original name routinely contains the customer's name.
        FileName         NVARCHAR(512)  NOT NULL,
        -- The object key. Opaque, so a bucket listing reveals nothing.
        StoragePath      NVARCHAR(400)  NOT NULL,
        ContentType      NVARCHAR(120)  NOT NULL,
        FileSize         BIGINT         NOT NULL,
        -- SHA-256 of the PLAINTEXT bytes, computed before encryption.
        FileHash         CHAR(64)       NOT NULL,
        UploadedByUserId INT            NULL,
        UploadedAt       DATETIME2      NOT NULL CONSTRAINT DF_DmeCustomerDocuments_UploadedAt DEFAULT SYSUTCDATETIME(),
        -- The only deletion fact. IsDeleted is derived from it in the view.
        DeletedAt        DATETIME2      NULL,

        CONSTRAINT CK_DmeCustomerDocuments_Kind
            CHECK (Kind IN ('cmn','rx','insurance-card','id','referral','other'))
    );

    CREATE INDEX IX_DmeCustomerDocuments_Customer
        ON dbo.DmeCustomerDocuments (CustomerId, TenantId);

    PRINT 'dbo.DmeCustomerDocuments created.';
END
ELSE
    PRINT 'dbo.DmeCustomerDocuments already exists.';
GO

/* ---------------------------------------------------------------------------
   Row level security.

   ALTER SECURITY POLICY is validated at COMPILE time, so an IF NOT EXISTS guard
   around a bare statement does not protect it. sp_executesql defers the parse.
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
   The live documents on a customer.

   IsDeleted is DERIVED. The view excludes removed rows, which IS the removal
   mechanism, exactly as vDmeOrderDocuments and vDmePayments do it.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.vDmeCustomerDocuments','V') IS NOT NULL DROP VIEW dbo.vDmeCustomerDocuments;
GO
CREATE VIEW dbo.vDmeCustomerDocuments
AS
SELECT  d.DocumentId,
        d.TenantId,
        d.CustomerId,
        c.AccountNo,
        c.LocationId,          -- derived from the customer, never stored here
        d.Kind,
        d.FileName,            -- ciphertext, decrypt in the app
        d.StoragePath,
        d.ContentType,
        d.FileSize,
        d.FileHash,
        d.UploadedByUserId,
        -- Composed here rather than stored: staff names are not PHI and change
        -- when somebody marries, and the document should follow.
        LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))) AS UploadedByName,
        d.UploadedAt
FROM        dbo.DmeCustomerDocuments d
JOIN        dbo.DmeCustomers c ON c.CustomerId = d.CustomerId
LEFT JOIN   dbo.Users        u ON u.UserId     = d.UploadedByUserId
WHERE       d.DeletedAt IS NULL;
GO

SELECT 'Customer documents' AS Summary, COUNT(*) AS Rows_ FROM dbo.DmeCustomerDocuments;
GO
