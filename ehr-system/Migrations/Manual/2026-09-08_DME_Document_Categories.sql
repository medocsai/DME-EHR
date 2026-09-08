SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   Give an order document a category, so the paperwork a DMEPOS supplier is
   actually audited on has somewhere to live.

   WHY THIS IS NOT COSMETIC

   dbo.DmeOrderDocuments held only proof of delivery, and the panel on
   /Dme/Order/{id} was titled that. Proof of delivery answers "did it arrive".
   It is not the document a payer asks for when they deny a claim.

   Almost no DMEPOS denial is about the equipment. They are about the file: the
   standard written order, the certificate of medical necessity, the chart notes
   proving the item was needed, the prior authorisation. Those had nowhere to be
   stored, so a supplier could pass every screen in this product and still lose
   an appeal for want of a document the software gave them no way to keep.

   The New Customer screen carried an Attachments panel offering exactly these
   categories. It accepted a file, listed it, and then discarded it silently on
   save, which is the worst of the three arrangements: staff scanned a CMN,
   believed it was on file, and had nothing when the payer asked. That panel is
   removed in the same commit and this is where those documents go instead.

   WHY ON THE ORDER, NOT ON THE CUSTOMER

   DMEPOS documentation is per item and per date span. A CMN covers a specific
   item for a specific period. A written order is for a specific order. Medical
   records support a specific claim. Filing them against the customer would put
   an audit answer one join away from the thing being audited.

   The genuinely customer-level documents are the insurance card and a photo ID,
   and both are convenience: what a claim needs off the card is already captured
   as data on DmeCustomerInsurances. So no second documents table, no second
   service, no second storage path.

   'pod' IS THE DEFAULT AND THAT IS DELIBERATE. Every row that exists today was
   attached through a panel titled Proof of delivery, so backfilling them as
   'pod' records what was actually meant rather than guessing.

   Safe to re-run.
   =========================================================================== */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeOrderDocuments')
                 AND name = 'Category')
BEGIN
    ALTER TABLE dbo.DmeOrderDocuments
        ADD Category VARCHAR(20) NOT NULL
            CONSTRAINT DF_DmeOrderDocuments_Category DEFAULT 'pod';

    PRINT 'Added DmeOrderDocuments.Category, existing rows backfilled as pod.';
END
ELSE
    PRINT 'DmeOrderDocuments.Category already present.';
GO

/* The allowed set, enforced in the database rather than only in the picker.
   A category posted by anything other than the form still has to be one of
   these, and a typo becomes a constraint violation instead of a document
   nobody can find again. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_DmeOrderDocuments_Category')
BEGIN
    ALTER TABLE dbo.DmeOrderDocuments
        ADD CONSTRAINT CK_DmeOrderDocuments_Category CHECK (Category IN (
            'pod',        -- proof of delivery: signature, photo, tracking slip
            'swo',        -- standard written order, the prescription
            'cmn',        -- certificate of medical necessity
            'records',    -- chart notes proving the item was needed
            'auth',       -- prior authorisation
            'abn',        -- advance beneficiary notice
            'insurance',  -- a scanned card, filed against the order it was taken for
            'other'));

    PRINT 'Added CK_DmeOrderDocuments_Category.';
END
ELSE
    PRINT 'CK_DmeOrderDocuments_Category already present.';
GO

/* The view has to expose it, or the panel cannot group by it. Reads go through
   the view because its WHERE DeletedAt IS NULL is the removal mechanism. */
IF OBJECT_ID('dbo.vDmeOrderDocuments') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
    ALTER VIEW dbo.vDmeOrderDocuments
    AS
    SELECT  d.DocumentId,
            d.TenantId,
            d.OrderId,
            o.OrderNumber,
            c.LocationId,          -- derived from the order''s customer, never stored here
            d.Category,
            d.FileName,            -- ciphertext, decrypt in the app
            d.StoragePath,
            d.ContentType,
            d.FileSize,
            d.FileHash,
            d.UploadedByUserId,
            -- Composed here rather than stored: staff names are not PHI and change
            -- when somebody marries, and the document should follow.
            LTRIM(RTRIM(ISNULL(u.FirstName,'''') + '' '' + ISNULL(u.LastName,''''))) AS UploadedByName,
            d.UploadedAt
    FROM        dbo.DmeOrderDocuments d
    JOIN        dbo.DmeOrders    o ON o.OrderId    = d.OrderId
    JOIN        dbo.DmeCustomers c ON c.CustomerId = o.CustomerId
    LEFT JOIN   dbo.Users        u ON u.UserId     = d.UploadedByUserId
    WHERE       d.DeletedAt IS NULL;';

    PRINT 'vDmeOrderDocuments now exposes Category.';
END
GO

/* --------------------------------------------------------------- verification */
DECLARE @missing NVARCHAR(MAX) = N'';

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeOrderDocuments') AND name = 'Category')
    SET @missing = @missing + N'DmeOrderDocuments.Category; ';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeOrderDocuments_Category')
    SET @missing = @missing + N'CK_DmeOrderDocuments_Category; ';

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.vDmeOrderDocuments') AND name = 'Category')
    SET @missing = @missing + N'vDmeOrderDocuments.Category; ';

IF @missing <> N''
    RAISERROR('Document categories incomplete: %s', 16, 1, @missing);
ELSE
    PRINT 'Verified: order documents carry a category, constrained and exposed.';
GO
