/* =============================================================================
   DME — Proof of delivery attachments
   Date: 2026-08-27
   Migration order: 14 (after 2026-08-27_DME_Drop_Ship.sql)

   WHY
   ---
   The client: "Proof of Delivery: Allow for the option to attach Proof of
   delivery files as pdf, pictures, etc."

   The POD panel captured a typed name and a drawn signature and nothing else.
   There was nowhere to put the delivery ticket the driver photographed or the
   signed paper the customer scanned, and on a drop-shipped order there is no
   signature at all: the carrier's paperwork IS the proof. That document is what
   defends the claim in an audit.

   THIS IS THE FIRST REAL FILE UPLOAD IN THE PRODUCT
   -------------------------------------------------
   The Attachments panel on the New Customer screen is demo UI: it lists what you
   pick and persists nothing, and its own code says so. So this brings storage,
   size and type limits, and PHI at rest with it.

   WHAT IS STORED HERE, AND WHAT IS NOT
   ------------------------------------
   The FILE goes to object storage. This table holds only the facts about it
   that have no other home, and nothing derived:

     StoragePath     where the bytes are
     FileName        the original name, CIPHERTEXT: "john-doe-pod.pdf" is PHI
     ContentType     what it is
     FileSize        how big
     FileHash        SHA-256 of the PLAINTEXT, so tampering is detectable
     UploadedByUserId / UploadedAt / DeletedAt

   NO IsDeleted and NO IsEncrypted column:
     - Every file is encrypted, so a flag saying so is a constant.
     - Deletion is DeletedAt, and IsDeleted is derived. A POD is the evidence a
       claim was legitimate, so it is RETIRED, never erased. Same rule as
       voiding a payment and retiring a distributor.

   NO LocationId. It derives from the order, per the locations rule: LocationId
   is stored on exactly three tables and this is not one of them.

   THE BYTES ARE ENCRYPTED BEFORE THEY LEAVE THE APP
   ------------------------------------------------
   A proof of delivery carries the customer's name, their home address and their
   signature. It is PHI, and the bucket must never hold it in the clear. The
   stored filename is opaque for the same reason: a bucket listing must not read
   "john-doe-oxygen-pod.pdf".

   NO SIGNED URLS. The file is served through our own controller, authenticated,
   tenant checked and audited like every other PHI read. A signed URL is valid
   for anyone holding it and leaves both the tenant check and the audit trail
   behind, which is exactly what the foundation work existed to prevent.

   SAFE TO RE-RUN.
   ============================================================================= */

SET NOCOUNT ON;
GO

IF OBJECT_ID('dbo.DmeOrderDocuments','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeOrderDocuments (
        DocumentId       INT IDENTITY(1,1) PRIMARY KEY,
        TenantId         INT            NOT NULL,
        OrderId          INT            NOT NULL,
        -- Ciphertext. The original name routinely contains the customer's name.
        FileName         NVARCHAR(512)  NOT NULL,
        -- The object key. Opaque, so a bucket listing reveals nothing.
        StoragePath      NVARCHAR(400)  NOT NULL,
        ContentType      NVARCHAR(120)  NOT NULL,
        FileSize         BIGINT         NOT NULL,
        -- SHA-256 of the PLAINTEXT bytes, computed before encryption. What makes
        -- "this is the document that was uploaded" checkable years later.
        FileHash         CHAR(64)       NOT NULL,
        UploadedByUserId INT            NULL,
        UploadedAt       DATETIME2      NOT NULL CONSTRAINT DF_DmeOrderDocuments_UploadedAt DEFAULT SYSUTCDATETIME(),
        -- The only deletion fact. IsDeleted is derived from it in vDmeOrderDocuments.
        DeletedAt        DATETIME2      NULL
    );
    PRINT 'dbo.DmeOrderDocuments created.';
END
ELSE
    PRINT 'dbo.DmeOrderDocuments already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('dbo.DmeOrderDocuments') AND name = 'IX_DmeOrderDocuments_Order')
    CREATE INDEX IX_DmeOrderDocuments_Order ON dbo.DmeOrderDocuments (OrderId, TenantId);
GO

/* ---------------------------------------------------------------------------
   Row level security, like every other DME table
   --------------------------------------------------------------------------- */
IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeOrderDocuments')
      AND sp.predicate_type_desc = 'FILTER')
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeOrderDocuments;';
    PRINT 'FILTER predicate added on DmeOrderDocuments.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.security_predicates sp
    JOIN sys.security_policies p ON p.object_id = sp.object_id
    WHERE p.name = 'TenantIsolationPolicy'
      AND sp.target_object_id = OBJECT_ID('dbo.DmeOrderDocuments')
      AND sp.predicate_type_desc = 'BLOCK')
BEGIN
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeOrderDocuments AFTER INSERT;';
    EXEC sp_executesql N'
        ALTER SECURITY POLICY dbo.TenantIsolationPolicy
        ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeOrderDocuments AFTER UPDATE;';
    PRINT 'BLOCK predicates added on DmeOrderDocuments.';
END
GO

/* ---------------------------------------------------------------------------
   The live documents on an order.

   IsDeleted is DERIVED. The view excludes removed rows, which is the whole
   removal mechanism, exactly as vDmePayments excludes voided receipts.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.vDmeOrderDocuments','V') IS NOT NULL DROP VIEW dbo.vDmeOrderDocuments;
GO
CREATE VIEW dbo.vDmeOrderDocuments
AS
SELECT  d.DocumentId,
        d.TenantId,
        d.OrderId,
        o.OrderNumber,
        c.LocationId,          -- derived from the order's customer, never stored here
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
FROM        dbo.DmeOrderDocuments d
JOIN        dbo.DmeOrders    o ON o.OrderId    = d.OrderId
JOIN        dbo.DmeCustomers c ON c.CustomerId = o.CustomerId
LEFT JOIN   dbo.Users        u ON u.UserId     = d.UploadedByUserId
WHERE       d.DeletedAt IS NULL;
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT 'documents'         AS Item, COUNT(*) AS Value FROM dbo.vDmeOrderDocuments
UNION ALL
SELECT 'in RLS policy',     COUNT(*) FROM sys.security_predicates
    WHERE target_object_id = OBJECT_ID('dbo.DmeOrderDocuments')
UNION ALL
SELECT 'stored IsDeleted or IsEncrypted columns (must be 0)', COUNT(*)
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.DmeOrderDocuments')
      AND name IN ('IsDeleted','IsEncrypted')
UNION ALL
SELECT 'stored LocationId (must be 0)', COUNT(*)
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.DmeOrderDocuments') AND name = 'LocationId';
GO
