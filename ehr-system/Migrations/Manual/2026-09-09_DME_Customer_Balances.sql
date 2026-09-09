SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   What a customer owes.

   WHAT WAS MISSING

   PatientResponsibility is computed per claim line in vDmeClaimLines, from the
   PR group of the remittance. Nothing rolled it up per CUSTOMER, so there was
   no screen anywhere that answered "who owes us money".

   That is not a reporting nicety. PR money is the patient's share: deductible,
   coinsurance and copay. A supplier is REQUIRED to make a genuine effort to
   collect it, because routinely waiving patient responsibility is treated as
   inducing patients to use you, which is its own offence rather than merely a
   debt written off. A supplier cannot make that effort against a number no
   screen displays.

   HOW IT IS DERIVED

   Balance = what the payers said the patient owes
           - what the customer has actually paid

   The first half is read from vDmeClaimLines, NOT re-derived from the payment
   lines underneath it. That matters: vDmeClaimLines already answers "what is
   the patient responsibility on this line", and a second derivation of the same
   question is exactly the drift this codebase keeps removing. One view owns the
   answer; this one adds up its answers.

   The second half is customer money only: vDmePaymentLines carries Source, so
   'customer' receipts are separable from payer receipts. A payer's cheque
   reduces what the PAYER owes, never what the patient owes.

   Voided payments fall out on their own, because both views already exclude
   them. That exclusion is the whole reversal mechanism.

   A KNOWN LIMIT, WRITTEN DOWN RATHER THAN HIDDEN

   PatientResponsibility SUMS across remittances on the same claim line. With a
   primary and then a secondary payer that overstates: the primary says the
   patient owes 36, the secondary pays 30 of it and says the patient owes 6, and
   the sum is 42 rather than 6.

   Left as a sum deliberately, because vDmeClaimLines already sums and having
   two different answers to one question is worse than having one answer with a
   documented limit. Fixing it properly means deciding that the LATEST
   remittance supersedes the earlier one, and that decision belongs with
   secondary billing, which is not built. Until then a supplier posting a
   secondary payment should expect this figure to read high.

   AGING

   OldestOutstandingServiceDate is the earliest service date on a claim that
   still carries patient responsibility. Enough to answer "how long has this
   been owed", which is what somebody chasing it needs. Bucketed aging
   (0-30/31-60/61-90/90+) needs per-claim apportionment of the payments and is
   deliberately not attempted here.

   LocationId is derived from the customer, like everywhere else, so the branch
   filter works without a stored copy.

   Safe to re-run.
   =========================================================================== */

IF OBJECT_ID('dbo.vDmeCustomerBalances') IS NOT NULL
    DROP VIEW dbo.vDmeCustomerBalances;
GO

CREATE VIEW dbo.vDmeCustomerBalances
AS
SELECT  cu.CustomerId,
        cu.TenantId,
        cu.LocationId,
        cu.AccountNo,
        -- Ciphertext, in two columns. Compose them in the app with
        -- _phi.ComposeCustomerNames; two ciphertexts joined in SQL are not
        -- decryptable by anyone with any key.
        cu.FirstName            AS CustomerFirstName,
        cu.LastName             AS CustomerLastName,
        cu.Phone,

        ISNULL(pr.Total, 0)                        AS PatientResponsibility,
        ISNULL(paid.Total, 0)                      AS CustomerPaid,
        ISNULL(pr.Total, 0) - ISNULL(paid.Total, 0) AS Balance,

        oldest.ServiceDate                         AS OldestOutstandingServiceDate
FROM        dbo.DmeCustomers cu

/* What every payer has said this customer owes. */
OUTER APPLY (
    SELECT SUM(cl.PatientResponsibility) AS Total
    FROM   dbo.vDmeClaimLines cl
    JOIN   dbo.DmeClaims      c ON c.ClaimId = cl.ClaimId
    WHERE  c.CustomerId = cu.CustomerId
) pr

/* What the customer has actually paid. Source separates their money from the
   payer's; a payer cheque reduces what the payer owes, not what they owe. */
OUTER APPLY (
    SELECT SUM(v.PaidAmount) AS Total
    FROM   dbo.vDmePaymentLines v
    WHERE  v.CustomerId = cu.CustomerId
      AND  v.Source     = 'customer'
      AND  v.IsVoided   = 0
) paid

/* The earliest claim still carrying patient responsibility. */
OUTER APPLY (
    SELECT MIN(c.ServiceDate) AS ServiceDate
    FROM   dbo.DmeClaims      c
    JOIN   dbo.vDmeClaimLines cl ON cl.ClaimId = c.ClaimId
    WHERE  c.CustomerId = cu.CustomerId
      AND  cl.PatientResponsibility > 0
) oldest;
GO

/* --------------------------------------------------------------- verification */
DECLARE @missing NVARCHAR(MAX) = N'';

IF OBJECT_ID('dbo.vDmeCustomerBalances') IS NULL
    SET @missing = @missing + N'vDmeCustomerBalances; ';
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID('dbo.vDmeCustomerBalances') AND name = 'Balance')
        SET @missing = @missing + N'vDmeCustomerBalances.Balance; ';

    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID('dbo.vDmeCustomerBalances') AND name = 'LocationId')
        SET @missing = @missing + N'vDmeCustomerBalances.LocationId; ';

    /* The name columns must stay SEPARATE. A view that concatenated them would
       hand the app one unusable string, and the screen would render base64. */
    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID('dbo.vDmeCustomerBalances') AND name = 'CustomerFirstName')
        SET @missing = @missing + N'vDmeCustomerBalances.CustomerFirstName; ';
END

IF @missing <> N''
    RAISERROR('Customer balances incomplete: %s', 16, 1, @missing);
ELSE
    PRINT 'Verified: what a customer owes is now a question the database can answer.';
GO
