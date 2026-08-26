/* ============================================================================
   DME Payments and Denials  (DMEEHR database)

   WHY
   The claim lifecycle ended at 'submitted'. Nothing ever came back, so there
   was no payment data in the system at all: no amount paid, no amount denied,
   no denial reason, no balance. The client asked for three monthly dashboard
   numbers (amount paid, amount denied, most frequent denial code) and none of
   them could be answered, because the money-coming-back half of the billing
   cycle did not exist.

   This builds that half. The decisions behind it, with the reasoning, are in
   docs/BILLING-DECISIONS.md. In short:

     1. Money enters by MANUAL POSTING. Payer money and customer money land in
        one model. An 835/ERA parser, when there is a clearinghouse account,
        becomes a second writer into these same tables and needs no new schema.
     2. A line is DENIED when it was paid nothing and carries a CO/PI
        adjustment. Amount denied is the billed charge of those lines.
     3. MONTHLY means posting date, so the numbers reconcile to a bank
        statement.
     4. Only TWO amounts are stored per line: AllowedAmount and PaidAmount.
        Everything else is derived from the CAS adjustment rows.

   THE SINGLE SOURCE OF TRUTH TEST, RUN ON EVERY COLUMN BELOW
   For each value: where does this fact already live? Only facts with no other
   home are stored.

     AllowedAmount            the payer's own number. Exists nowhere else.
     PaidAmount               the payer's own number. Exists nowhere else.
     Adjustment rows          one row per reason the payer moved money. This is
                              the fact that neither RehabDox nor IMEHR ever
                              recorded, and it is the only reason the client's
                              "most frequent denial code" is a COUNT instead of
                              a project.
     DmePayments.Amount       the face value of the check or EFT as it arrived.
                              NOT a duplicate of SUM(lines): a $500 check with
                              $450 allocated leaves $50 genuinely unapplied, and
                              that gap is real information a biller has to
                              chase. The GAP is derived; both sides of it are
                              facts from different places.

     Adjustment total         SUM of CO and PI rows.               derived
     Patient responsibility   SUM of PR rows.                      derived
     Line denied              PaidAmount = 0 and a CO/PI row.      derived
     Claim paid to date       SUM over its payment lines.          derived
     Claim balance            charge minus everything above.       derived
     Claim payment status     computed from the above.             derived
     Payment voided           VoidedAt IS NOT NULL.                derived

   Nothing is written back to DmeClaims. That is the same rule the previous
   migration applied when MonthsBilled, OnHand and Claims.Total were dropped.

   WHY A PAYMENT IS VOIDED AND NEVER EDITED
   RehabDox reverses a posting by storing the prior values alongside it, which
   works but means every posted row carries a shadow copy of its own history.
   Here a payment is immutable once posted and a mistake is VOIDED, which is
   what a biller does on paper anyway. A voided payment drops out of every
   computed number because the views exclude it, so there is no reversal
   arithmetic to get wrong and the original entry survives for the audit trail.

   Idempotent: safe to re-run.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* ---------------------------------------------------------------------------
   1. CARC reference list

   Claim Adjustment Reason Codes are a national X12 code list maintained by the
   Washington Publishing Company. Every payer in the country uses the same
   numbers, so unlike HcpcsCodes (which carries each supplier's own contract
   pricing and is therefore tenant data) this table has no per-tenant content.

   It is deliberately NOT tenant scoped and NOT in the RLS policy. That also
   means onboarding a new tenant gets the code list for free rather than
   needing another seeding script.

   The GROUP code is not stored here. The same reason code appears under
   different groups depending on the claim (CARC 45 is normally CO, but a payer
   can report it as OA), so a stored group would be a guess that overrides what
   the remittance actually said. The group is recorded per adjustment, from the
   EOB, where it is a fact.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmeCarcCodes','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeCarcCodes (
        Code        NVARCHAR(5)   NOT NULL PRIMARY KEY,
        Description NVARCHAR(300) NOT NULL
    );
    PRINT 'Created DmeCarcCodes';
END
GO

/* Seeded with the codes a DME supplier actually meets. Official CARC wording,
   abbreviated only where the full text runs past what fits a dropdown. */
MERGE dbo.DmeCarcCodes AS t
USING (VALUES
    ('1',   'Deductible amount'),
    ('2',   'Coinsurance amount'),
    ('3',   'Co-payment amount'),
    ('4',   'The procedure code is inconsistent with the modifier used'),
    ('5',   'The procedure code is inconsistent with the place of service'),
    ('11',  'The diagnosis is inconsistent with the procedure'),
    ('16',  'Claim/service lacks information or has submission/billing error(s)'),
    ('18',  'Exact duplicate claim/service'),
    ('23',  'The impact of prior payer(s) adjudication including payments and/or adjustments'),
    ('26',  'Expenses incurred prior to coverage'),
    ('27',  'Expenses incurred after coverage terminated'),
    ('29',  'The time limit for filing has expired'),
    ('45',  'Charge exceeds fee schedule/maximum allowable or contracted fee arrangement'),
    ('49',  'Non-covered service because this is a routine/preventive exam'),
    ('50',  'Non-covered service because it is not deemed a medical necessity by the payer'),
    ('55',  'Procedure/treatment/drug is deemed experimental/investigational by the payer'),
    ('59',  'Processed based on multiple or concurrent procedure rules'),
    ('96',  'Non-covered charge(s)'),
    ('97',  'The benefit for this service is included in the payment for another service'),
    ('109', 'Claim/service not covered by this payer/contractor'),
    ('119', 'Benefit maximum for this time period or occurrence has been reached'),
    ('125', 'Submission/billing error(s)'),
    ('151', 'Payer deems the information submitted does not support this many services'),
    ('167', 'This (these) diagnosis(es) is (are) not covered'),
    ('181', 'Procedure code was invalid on the date of service'),
    ('182', 'Procedure modifier was invalid on the date of service'),
    ('183', 'The referring provider is not eligible to refer the service billed'),
    ('185', 'The rendering provider is not eligible to perform the service billed'),
    ('197', 'Precertification/authorization/notification/pre-treatment absent'),
    ('198', 'Precertification/notification/authorization/pre-treatment exceeded'),
    ('204', 'Service/equipment/drug is not covered under the patient''s current benefit plan'),
    ('226', 'Information requested from the billing/rendering provider was not provided'),
    ('234', 'This procedure is not paid separately'),
    ('243', 'Services not authorized by network/primary care providers'),
    ('252', 'An attachment/other documentation is required to adjudicate this claim/service'),
    ('253', 'Sequestration, reduction in federal payment'),
    ('256', 'Service not payable per managed care contract'),
    ('A1',  'Claim/service denied'),
    ('B7',  'Provider was not certified/eligible to be paid for this procedure on this date of service'),
    ('B15', 'This service requires that a qualifying service be received and covered')
) AS s(Code, Description)
ON t.Code = s.Code
WHEN NOT MATCHED THEN INSERT (Code, Description) VALUES (s.Code, s.Description)
WHEN MATCHED AND t.Description <> s.Description THEN UPDATE SET Description = s.Description;
GO

/* ---------------------------------------------------------------------------
   2. Payment header: one check, EFT, card swipe or cash receipt

   Source separates the two ways a DME supplier is paid. Both have to live in
   one model or the balance is wrong: a customer who pays a $30 copay in cash
   has settled part of the same claim the payer partly paid, and a design that
   only understands remittances has nowhere to put that.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmePayments','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmePayments (
        PaymentId       INT IDENTITY(1,1) PRIMARY KEY,
        TenantId        INT           NOT NULL CONSTRAINT DF_DmePayments_Tenant DEFAULT 1,
        PaymentNumber   NVARCHAR(20)  NOT NULL,
        Source          NVARCHAR(10)  NOT NULL,          -- payer | customer
        PayerName       NVARCHAR(120) NULL,              -- as remitted, point in time
        CustomerId      INT           NULL,              -- set when Source='customer'
        PostedDate      DATE          NOT NULL,          -- drives the monthly tiles
        Method          NVARCHAR(20)  NOT NULL,          -- check | eft | card | cash | other
        ReferenceNumber NVARCHAR(50)  NULL,              -- check number / EFT trace
        Amount          DECIMAL(10,2) NOT NULL,          -- face value as received
        Note            NVARCHAR(300) NULL,
        CreatedAt       DATETIME2     NOT NULL CONSTRAINT DF_DmePayments_Created DEFAULT SYSUTCDATETIME(),
        CreatedBy       INT           NULL,
        VoidedAt        DATETIME2     NULL,
        VoidedBy        INT           NULL,
        VoidReason      NVARCHAR(200) NULL,
        CONSTRAINT CK_DmePayments_Source CHECK (Source IN ('payer','customer')),
        CONSTRAINT CK_DmePayments_Method CHECK (Method IN ('check','eft','card','cash','other'))
    );
    CREATE INDEX IX_DmePayments_TenantId ON dbo.DmePayments(TenantId);
    CREATE INDEX IX_DmePayments_Posted   ON dbo.DmePayments(TenantId, PostedDate);
    PRINT 'Created DmePayments';
END
GO

/* ---------------------------------------------------------------------------
   3. Payment line: one claim line, adjudicated

   ClaimLineId rather than ClaimId. The payer adjudicates line by line, and a
   claim that pays two lines and denies a third is the ordinary case, not an
   edge case. Posting at claim level would make partial denial unrepresentable,
   which is the exact number the client asked for.

   Everything else about the line (HCPCS, charge, which rental month it billed)
   is reachable by join, so none of it is copied here.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmePaymentLines','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmePaymentLines (
        PaymentLineId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId      INT           NOT NULL CONSTRAINT DF_DmePaymentLines_Tenant DEFAULT 1,
        PaymentId     INT           NOT NULL,
        ClaimLineId   INT           NOT NULL,
        AllowedAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_DmePaymentLines_Allowed DEFAULT 0,
        PaidAmount    DECIMAL(10,2) NOT NULL CONSTRAINT DF_DmePaymentLines_Paid DEFAULT 0,
        CONSTRAINT FK_DmePaymentLines_Payment   FOREIGN KEY (PaymentId)   REFERENCES dbo.DmePayments(PaymentId),
        CONSTRAINT FK_DmePaymentLines_ClaimLine FOREIGN KEY (ClaimLineId) REFERENCES dbo.DmeClaimLines(ClaimLineId)
    );
    CREATE INDEX IX_DmePaymentLines_TenantId  ON dbo.DmePaymentLines(TenantId);
    CREATE INDEX IX_DmePaymentLines_Payment   ON dbo.DmePaymentLines(PaymentId);
    CREATE INDEX IX_DmePaymentLines_ClaimLine ON dbo.DmePaymentLines(ClaimLineId);
    PRINT 'Created DmePaymentLines';
END
GO

/* ---------------------------------------------------------------------------
   4. Adjustments: the X12 CAS segment, one row per reason

   This is the table that makes the client's third tile possible. Neither
   RehabDox nor IMEHR stores the reason code per line: RehabDox keeps CAS group
   TOTALS (how much was written off, how much is the patient's) and throws the
   individual CARC away, so "most frequent denial code" cannot be answered from
   it at any price.

   GroupCode and ReasonCode together are what an EOB prints, and together they
   are what separates a denial from a discount. Storing only the amount, as the
   reference implementation does, loses the distinction permanently.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmePaymentLineAdjustments','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmePaymentLineAdjustments (
        AdjustmentId  INT IDENTITY(1,1) PRIMARY KEY,
        TenantId      INT           NOT NULL CONSTRAINT DF_DmePayAdj_Tenant DEFAULT 1,
        PaymentLineId INT           NOT NULL,
        GroupCode     NVARCHAR(2)   NOT NULL,   -- CO | PR | OA | PI
        ReasonCode    NVARCHAR(5)   NOT NULL,   -- CARC, e.g. 45, 50, 1
        Amount        DECIMAL(10,2) NOT NULL,
        CONSTRAINT FK_DmePayAdj_Line FOREIGN KEY (PaymentLineId) REFERENCES dbo.DmePaymentLines(PaymentLineId),
        CONSTRAINT CK_DmePayAdj_Group CHECK (GroupCode IN ('CO','PR','OA','PI'))
    );
    CREATE INDEX IX_DmePayAdj_TenantId ON dbo.DmePaymentLineAdjustments(TenantId);
    CREATE INDEX IX_DmePayAdj_Line     ON dbo.DmePaymentLineAdjustments(PaymentLineId);
    CREATE INDEX IX_DmePayAdj_Reason   ON dbo.DmePaymentLineAdjustments(TenantId, GroupCode, ReasonCode);
    PRINT 'Created DmePaymentLineAdjustments';
END
GO

/* ---------------------------------------------------------------------------
   5. Row level security on the three new tenant tables

   Via sp_executesql on purpose: ALTER SECURITY POLICY is validated when the
   batch is COMPILED, not when it runs, so an IF NOT EXISTS guard around a bare
   ALTER does not survive a re-run. See the note in the previous migration.

   FILTER and BLOCK both, as everywhere else. FILTER alone stops a cross-tenant
   read but still lets a write plant a row in someone else's tenant, and a
   payment planted in another supplier's books is about as bad as it gets.
   --------------------------------------------------------------------------- */
DECLARE @rls SYSNAME, @rlsSql NVARCHAR(MAX);
DECLARE rlscur CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES
        ('DmePayments'), ('DmePaymentLines'), ('DmePaymentLineAdjustments')
    ) AS x(name);

OPEN rlscur;
FETCH NEXT FROM rlscur INTO @rls;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.security_predicates sp
        JOIN sys.security_policies p ON p.object_id = sp.object_id
        WHERE p.name = 'TenantIsolationPolicy'
          AND sp.target_object_id = OBJECT_ID('dbo.' + @rls))
    BEGIN
        SET @rlsSql = N'
            ALTER SECURITY POLICY dbo.TenantIsolationPolicy
                ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.' + QUOTENAME(@rls) + N',
                ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.' + QUOTENAME(@rls) + N' AFTER INSERT,
                ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.' + QUOTENAME(@rls) + N' AFTER UPDATE;';
        EXEC sp_executesql @rlsSql;
        PRINT @rls + ' added to TenantIsolationPolicy';
    END
    FETCH NEXT FROM rlscur INTO @rls;
END
CLOSE rlscur; DEALLOCATE rlscur;
GO

/* ---------------------------------------------------------------------------
   6. DmeClaims.Status becomes the SUBMISSION status only

   The column was documented as "ready | submitted | paid | denied". The first
   two are workflow that the application owns. The last two are now facts about
   payments, and a stored copy of them is the stock problem in its purest form:
   a claim row saying 'paid' with no payment behind it, or still saying
   'submitted' after a check has cleared.

   So the column is constrained to what it legitimately owns, and paid/denied
   are computed in vDmeClaims. The constraint is the part that makes this a
   long-term fix rather than a cleanup: without it somebody writes 'paid' back
   into the table next month and the drift starts again.

   Checked against the data before constraining, per the standing rule: the
   seed only ever produces 'ready' and 'submitted', and nothing in the codebase
   writes anything else. Any row that somehow holds paid/denied is moved to
   'submitted' and PRINTed, because a claim that was marked paid was certainly
   submitted, and the payment itself has to be posted for real.
   --------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM dbo.DmeClaims WHERE Status NOT IN ('ready','submitted'))
BEGIN
    DECLARE @moved INT;
    UPDATE dbo.DmeClaims SET Status = 'submitted' WHERE Status NOT IN ('ready','submitted');
    SET @moved = @@ROWCOUNT;
    PRINT 'Normalised ' + CAST(@moved AS NVARCHAR(10)) +
          ' claim(s) whose Status held a payment outcome. Post the payment to record it.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DmeClaims_Status')
BEGIN
    ALTER TABLE dbo.DmeClaims WITH CHECK
        ADD CONSTRAINT CK_DmeClaims_Status CHECK (Status IN ('ready','submitted'));
    PRINT 'DmeClaims.Status constrained to the submission lifecycle';
END
GO

/* ---------------------------------------------------------------------------
   7. Read models

   The application reads these and never the base tables, so a caller cannot
   bypass the derivation by forgetting to compute.
   --------------------------------------------------------------------------- */

/* One adjudicated claim line: what the payer allowed, paid, and why it moved
   the rest. Every column below the two stored amounts is computed here so no
   screen and no report can arrive at a different answer. */
CREATE OR ALTER VIEW dbo.vDmePaymentLines AS
SELECT
    pl.PaymentLineId, pl.TenantId, pl.PaymentId, pl.ClaimLineId,
    pl.AllowedAmount, pl.PaidAmount,

    p.PaymentNumber, p.Source, p.Method, p.ReferenceNumber, p.PayerName,
    p.PostedDate,
    -- Derived: a payment is voided when it has a void date. Nothing keeps a
    -- separate flag in step with the date.
    CAST(CASE WHEN p.VoidedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS IsVoided,

    cl.ClaimId, cl.Hcpcs, cl.ItemName, cl.Modifier, cl.Units, cl.Charge, cl.RentalId,
    c.ClaimNumber, c.CustomerId,

    -- Derived: the payer's own reasons, totalled by group.
    -- CO and PI are the supplier's write-off. PR is the patient's to pay. OA is
    -- everything else (sequestration and the like). They are summed separately
    -- because collapsing them is exactly what makes "denied" meaningless.
    (SELECT ISNULL(SUM(a.Amount),0) FROM dbo.DmePaymentLineAdjustments a
      WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')) AS ContractualAdjustment,
    (SELECT ISNULL(SUM(a.Amount),0) FROM dbo.DmePaymentLineAdjustments a
      WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode = 'PR')          AS PatientResponsibility,
    (SELECT ISNULL(SUM(a.Amount),0) FROM dbo.DmePaymentLineAdjustments a
      WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode = 'OA')          AS OtherAdjustment,

    -- Derived: DENIED. Paid nothing, and the payer gave a CO or PI reason for
    -- keeping it. A line paid nothing because it all went to the patient's
    -- deductible carries a PR reason instead and is correctly NOT a denial: the
    -- money is still collectable, it is just collectable from the customer.
    CAST(CASE WHEN pl.PaidAmount = 0 AND EXISTS (
            SELECT 1 FROM dbo.DmePaymentLineAdjustments a
            WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI'))
         THEN 1 ELSE 0 END AS BIT) AS IsDenied,

    -- Derived: the reason to report for a denied line. Largest adjustment wins
    -- when a payer stacks several, because that is the one the biller has to
    -- work. Null unless the line is actually denied, so a paid line carrying a
    -- routine CO-45 discount never shows up as a denial reason.
    --
    -- Group and reason are exposed SEPARATELY as well as joined for display.
    -- The reporting query needs the bare reason code to join DmeCarcCodes, and
    -- pulling it back out of "CO-50" with string arithmetic is the kind of
    -- thing that works until a code list changes shape.
    CASE WHEN pl.PaidAmount = 0 THEN (
        SELECT TOP 1 a.GroupCode
        FROM dbo.DmePaymentLineAdjustments a
        WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')
        ORDER BY a.Amount DESC, a.AdjustmentId)
    END AS DenialGroup,
    CASE WHEN pl.PaidAmount = 0 THEN (
        SELECT TOP 1 a.ReasonCode
        FROM dbo.DmePaymentLineAdjustments a
        WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')
        ORDER BY a.Amount DESC, a.AdjustmentId)
    END AS DenialReasonCode,
    CASE WHEN pl.PaidAmount = 0 THEN (
        SELECT TOP 1 a.GroupCode + '-' + a.ReasonCode
        FROM dbo.DmePaymentLineAdjustments a
        WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')
        ORDER BY a.Amount DESC, a.AdjustmentId)
    END AS DenialCode
FROM dbo.DmePaymentLines pl
JOIN dbo.DmePayments   p  ON p.PaymentId   = pl.PaymentId
JOIN dbo.DmeClaimLines cl ON cl.ClaimLineId = pl.ClaimLineId
JOIN dbo.DmeClaims     c  ON c.ClaimId      = cl.ClaimId;
GO

/* One receipt. AppliedAmount and UnappliedAmount are the pair that catches the
   commonest posting mistake there is: a check entered but not fully allocated
   to lines. Neither is stored; the difference is arithmetic. */
CREATE OR ALTER VIEW dbo.vDmePayments AS
SELECT
    p.PaymentId, p.TenantId, p.PaymentNumber, p.Source, p.PayerName, p.CustomerId,
    p.PostedDate, p.Method, p.ReferenceNumber,
    p.Amount,                       -- face value as received: an outside fact
    p.Note, p.CreatedAt, p.CreatedBy, p.VoidedAt, p.VoidedBy, p.VoidReason,
    CAST(CASE WHEN p.VoidedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS IsVoided,
    (SELECT ISNULL(SUM(pl.PaidAmount),0) FROM dbo.DmePaymentLines pl
      WHERE pl.PaymentId = p.PaymentId) AS AppliedAmount,
    p.Amount - (SELECT ISNULL(SUM(pl.PaidAmount),0) FROM dbo.DmePaymentLines pl
      WHERE pl.PaymentId = p.PaymentId) AS UnappliedAmount,
    (SELECT COUNT(*) FROM dbo.DmePaymentLines pl WHERE pl.PaymentId = p.PaymentId) AS LineCount,
    -- The customer name as filed on the claims this receipt touched. Encrypted,
    -- like every other customer name: the application decrypts it.
    (SELECT TOP 1 c.CustomerName
       FROM dbo.DmePaymentLines pl
       JOIN dbo.DmeClaimLines cl ON cl.ClaimLineId = pl.ClaimLineId
       JOIN dbo.DmeClaims     c  ON c.ClaimId = cl.ClaimId
      WHERE pl.PaymentId = p.PaymentId) AS CustomerName
FROM dbo.DmePayments p;
GO

/* One claim line with its adjudication rolled up, so the CMS-1500 screen and
   the claim detail can show what happened to each line without every caller
   re-deriving it. Voided payments are excluded, which is the whole mechanism
   by which a void undoes a posting. */
CREATE OR ALTER VIEW dbo.vDmeClaimLines AS
SELECT
    cl.ClaimLineId, cl.TenantId, cl.ClaimId, cl.Hcpcs, cl.ItemName,
    cl.Modifier, cl.Units, cl.Charge, cl.RentalId,
    (SELECT ISNULL(SUM(v.AllowedAmount),0)          FROM dbo.vDmePaymentLines v WHERE v.ClaimLineId = cl.ClaimLineId AND v.IsVoided = 0) AS AllowedAmount,
    (SELECT ISNULL(SUM(v.PaidAmount),0)             FROM dbo.vDmePaymentLines v WHERE v.ClaimLineId = cl.ClaimLineId AND v.IsVoided = 0) AS PaidAmount,
    (SELECT ISNULL(SUM(v.ContractualAdjustment),0)  FROM dbo.vDmePaymentLines v WHERE v.ClaimLineId = cl.ClaimLineId AND v.IsVoided = 0) AS ContractualAdjustment,
    (SELECT ISNULL(SUM(v.PatientResponsibility),0)  FROM dbo.vDmePaymentLines v WHERE v.ClaimLineId = cl.ClaimLineId AND v.IsVoided = 0) AS PatientResponsibility,
    (SELECT ISNULL(SUM(v.OtherAdjustment),0)        FROM dbo.vDmePaymentLines v WHERE v.ClaimLineId = cl.ClaimLineId AND v.IsVoided = 0) AS OtherAdjustment,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.vDmePaymentLines v
                           WHERE v.ClaimLineId = cl.ClaimLineId AND v.IsVoided = 0 AND v.IsDenied = 1)
         THEN 1 ELSE 0 END AS BIT) AS IsDenied,
    (SELECT TOP 1 v.DenialCode FROM dbo.vDmePaymentLines v
      WHERE v.ClaimLineId = cl.ClaimLineId AND v.IsVoided = 0 AND v.IsDenied = 1
      ORDER BY v.PostedDate DESC, v.PaymentLineId DESC) AS DenialCode
FROM dbo.DmeClaimLines cl;
GO

/* The claim, with the money.

   Status is UNCHANGED and still means the submission lifecycle, so the Submit
   button and every existing filter keep working. PaymentStatus is the new,
   entirely derived answer to "did we get paid", and the two are separate
   because they are separate questions: a submitted claim can be paid, denied
   or still silent. */
CREATE OR ALTER VIEW dbo.vDmeClaims AS
SELECT
    c.ClaimId, c.TenantId, c.ClaimNumber, c.OrderId, c.CustomerId,
    c.CustomerName,         -- as filed: a submitted claim is a document
    c.PayerName,            -- as filed: customer may switch insurer later
    c.Status,               -- submission lifecycle only: ready | submitted
    c.ServiceDate, c.CreatedAt,

    -- Derived, never stored: a claim total that disagrees with its own lines is
    -- never the right answer, so there is nothing to disagree with.
    ch.ChargeTotal AS Total,
    ch.LineCount,

    -- Derived: the adjudication, rolled up from the lines.
    a.AllowedTotal, a.PaidTotal, a.PayerPaidTotal, a.CustomerPaidTotal,
    a.ContractualTotal, a.PatientResponsibilityTotal, a.OtherAdjustmentTotal,
    a.DeniedCharge, a.PaymentCount,

    -- Derived: what is still owed, split by who owes it. Chasing a payer and
    -- chasing a customer are different jobs for a biller, so one combined
    -- number would be unusable on its own.
    b.InsuranceBalance,
    b.PatientBalance,
    b.InsuranceBalance + b.PatientBalance AS Balance,

    -- Derived: the payment outcome. Order matters, and it is ordered by what
    -- the biller has to DO about it rather than by how much money arrived.
    --
    -- Denial is tested before payment because a fully denied claim also has a
    -- zero balance (the whole charge is written off), and calling that "paid"
    -- would be the worst possible answer.
    --
    -- Part denied is tested before paid for the same reason one step down. A
    -- claim where three lines paid and one was refused settles to a zero
    -- balance, so it would read as "paid" and the refused line would never be
    -- appealed. Appeal windows expire, so a denial that is invisible is a
    -- denial that becomes permanent.
    CASE
        WHEN a.PaymentCount = 0                                        THEN 'unpaid'
        WHEN a.DeniedCharge >= ch.ChargeTotal AND ch.ChargeTotal > 0   THEN 'denied'
        WHEN a.DeniedCharge > 0                                        THEN 'part-denied'
        WHEN b.InsuranceBalance + b.PatientBalance <= 0                THEN 'paid'
        WHEN b.InsuranceBalance <= 0 AND b.PatientBalance > 0          THEN 'patient-due'
        ELSE 'partial'
    END AS PaymentStatus
FROM dbo.DmeClaims c
CROSS APPLY (
    SELECT ChargeTotal = ISNULL(SUM(cl.Charge),0), LineCount = COUNT(*)
    FROM dbo.DmeClaimLines cl
    WHERE cl.ClaimId = c.ClaimId
) ch
CROSS APPLY (
    -- Voided payments are excluded here, and that single WHERE clause is the
    -- entire mechanism by which a void undoes a posting. No reversal rows, no
    -- subtraction to get wrong.
    SELECT
        AllowedTotal               = ISNULL(SUM(v.AllowedAmount),0),
        PaidTotal                  = ISNULL(SUM(v.PaidAmount),0),
        PayerPaidTotal             = ISNULL(SUM(CASE WHEN v.Source = 'payer'    THEN v.PaidAmount ELSE 0 END),0),
        CustomerPaidTotal          = ISNULL(SUM(CASE WHEN v.Source = 'customer' THEN v.PaidAmount ELSE 0 END),0),
        ContractualTotal           = ISNULL(SUM(v.ContractualAdjustment),0),
        PatientResponsibilityTotal = ISNULL(SUM(v.PatientResponsibility),0),
        OtherAdjustmentTotal       = ISNULL(SUM(v.OtherAdjustment),0),
        DeniedCharge               = ISNULL(SUM(CASE WHEN v.IsDenied = 1 THEN v.Charge ELSE 0 END),0),
        PaymentCount               = COUNT(v.PaymentLineId)
    FROM dbo.vDmePaymentLines v
    WHERE v.ClaimId = c.ClaimId AND v.IsVoided = 0
) a
CROSS APPLY (
    SELECT
        -- Still expected from the payer: the charge, less everything the payer
        -- has already accounted for, less what it handed to the patient.
        InsuranceBalance = ch.ChargeTotal - a.PayerPaidTotal - a.ContractualTotal
                           - a.OtherAdjustmentTotal - a.PatientResponsibilityTotal,
        -- Still expected from the customer: what the payer made theirs, less
        -- what they have actually handed over.
        PatientBalance   = a.PatientResponsibilityTotal - a.CustomerPaidTotal
) b;
GO

/* ---------------------------------------------------------------------------
   8. Demo seed

   One real remittance and one real denial, so the dashboard demonstrates the
   feature instead of showing three zeroes. Runs only on a database that has
   the seed claims and no payments at all, so it never touches live data.

   Dated inside the CURRENT month on purpose: the tiles are keyed on posting
   date, and a seed dated when the migration was written would show an empty
   dashboard forever after.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.DmePayments)
   AND EXISTS (SELECT 1 FROM dbo.DmeClaims WHERE ClaimNumber = 'CLM-02001')
BEGIN
    -- Five days ago, unless that falls into last month.
    DECLARE @pd DATE = CASE WHEN DAY(GETDATE()) >= 6
                            THEN DATEADD(day, -5, CAST(GETDATE() AS DATE))
                            ELSE DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1) END;

    DECLARE @line1 INT = (SELECT TOP 1 cl.ClaimLineId FROM dbo.DmeClaimLines cl
                          JOIN dbo.DmeClaims c ON c.ClaimId = cl.ClaimId
                          WHERE c.ClaimNumber = 'CLM-02001' ORDER BY cl.ClaimLineId);
    DECLARE @line2 INT = (SELECT TOP 1 cl.ClaimLineId FROM dbo.DmeClaimLines cl
                          JOIN dbo.DmeClaims c ON c.ClaimId = cl.ClaimId
                          WHERE c.ClaimNumber = 'CLM-02002' ORDER BY cl.ClaimLineId);

    IF @line1 IS NOT NULL AND @line2 IS NOT NULL
    BEGIN
        /* Medicare allows 142.40 of a 178.00 charge, pays 80%, leaves the rest
           with the patient. The CO-45 here is the case the model exists to tell
           apart: it reduces what we collect and is NOT a denial. */
        INSERT INTO dbo.DmePayments (TenantId,PaymentNumber,Source,PayerName,PostedDate,Method,ReferenceNumber,Amount,Note)
        VALUES (1,'PMT-01001','payer','Medicare (Railroad/Part B)',@pd,'eft','EFT-4471903',113.92,'Demo remittance');
        DECLARE @dp1 INT = SCOPE_IDENTITY();
        INSERT INTO dbo.DmePaymentLines (TenantId,PaymentId,ClaimLineId,AllowedAmount,PaidAmount)
        VALUES (1,@dp1,@line1,142.40,113.92);
        DECLARE @dl1 INT = SCOPE_IDENTITY();
        INSERT INTO dbo.DmePaymentLineAdjustments (TenantId,PaymentLineId,GroupCode,ReasonCode,Amount) VALUES
            (1,@dl1,'CO','45',35.60),
            (1,@dl1,'PR','2',28.48);

        /* The following month is refused for want of a current CMN. A denial
           arrives as a remittance worth nothing that explains itself. */
        INSERT INTO dbo.DmePayments (TenantId,PaymentNumber,Source,PayerName,PostedDate,Method,ReferenceNumber,Amount,Note)
        VALUES (1,'PMT-01002','payer','Medicare (Railroad/Part B)',DATEADD(day,1,@pd),'eft','EFT-4472118',0.00,'Demo denial');
        DECLARE @dp2 INT = SCOPE_IDENTITY();
        INSERT INTO dbo.DmePaymentLines (TenantId,PaymentId,ClaimLineId,AllowedAmount,PaidAmount)
        VALUES (1,@dp2,@line2,0,0);
        DECLARE @dl2 INT = SCOPE_IDENTITY();
        INSERT INTO dbo.DmePaymentLineAdjustments (TenantId,PaymentLineId,GroupCode,ReasonCode,Amount)
        VALUES (1,@dl2,'CO','50',178.00);

        -- The sequence has to move past the numbers just used, or the next
        -- payment posted in the app collides with the demo data.
        IF NOT EXISTS (SELECT 1 FROM dbo.DmeSeq WHERE Name='PMT' AND TenantId=1)
            INSERT INTO dbo.DmeSeq (Name,TenantId,Val) VALUES ('PMT',1,1002);
        ELSE
            UPDATE dbo.DmeSeq SET Val = CASE WHEN Val < 1002 THEN 1002 ELSE Val END
             WHERE Name='PMT' AND TenantId=1;

        PRINT 'Seeded a demo remittance and a demo denial';
    END
END
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT ClaimNumber, Status, PaymentStatus, Total, PaidTotal, DeniedCharge, Balance
FROM dbo.vDmeClaims ORDER BY ClaimId;

SELECT COUNT(*) AS CarcCodesSeeded FROM dbo.DmeCarcCodes;

SELECT t.name AS TenantScopedTable,
       SUM(CASE WHEN sp.predicate_type_desc = 'FILTER' THEN 1 ELSE 0 END) AS FilterPredicates,
       SUM(CASE WHEN sp.predicate_type_desc = 'BLOCK'  THEN 1 ELSE 0 END) AS BlockPredicates
FROM sys.security_predicates sp
JOIN sys.security_policies p ON p.object_id = sp.object_id
JOIN sys.tables t ON t.object_id = sp.target_object_id
WHERE p.name = 'TenantIsolationPolicy' AND t.name LIKE 'DmePayment%'
GROUP BY t.name;
GO
