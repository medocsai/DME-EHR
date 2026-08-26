/* ============================================================================
   DME Supplier identity and clearinghouse credentials  (DMEEHR database)

   WHY
   The CMS-1500 billing provider was a hardcoded string in a Razor view:

       33 Billing provider   Lakeview Medical Supply  NPI 1980000000

   Every claim this product produced therefore carried a made up NPI, and there
   was nowhere in DME to put the real one. A clearinghouse login is worthless
   while that is true: it would upload a correctly formatted claim billed under
   a provider that does not exist.

   So this adds the supplier's own identity first, and the clearinghouse
   credential second, because the credential belongs to the identity.

   WHY TENANT SCOPED AND NOT LOCATION SCOPED
   RehabDox looks location based and is not: OfficeAllySftpAccounts is owned by
   the TENANT, and Locations.OfficeAllySftpAccountId is only a pointer, because
   Office Ally issues one account per BILLING ENTITY and a billing entity is
   "a tenant, or a location with its own NPI".

   DME has no location dimension at all. Not one DME table carries LocationId:
   customers, orders, rentals, claims and inventory are scoped by tenant and
   nothing else. Introducing locations to hold one credential would mean
   touching every one of those tables and the tenant isolation story with them,
   for a supplier that has one billing entity and no clearinghouse account to
   test against.

   Medicare DMEPOS does require each location that furnishes equipment to be
   separately enrolled and accredited, so a supplier CAN eventually have two
   billing identities. That is why the credential points at the supplier profile
   rather than at the tenant directly: the day it is needed, a second profile
   row and a pointer column is the change, not a rewrite.

   THE SINGLE SOURCE OF TRUTH TEST, RUN BEFORE ADDING THE TABLE
   Where does each field of a "billing provider" already live?

     legal name, address, city, state, zip, phone   dbo.Tenants   already there
     Tax ID (CMS-1500 box 25)                       dbo.Tenants   already there
     NPI     (CMS-1500 box 33a)                     dbo.Tenants   already there
     PTAN / supplier number                         NOWHERE
     Taxonomy code (box 33b)                        NOWHERE
     Accepts assignment (box 27)                    NOWHERE

   Six of nine already have a home, so a DmeBillingProvider table holding all
   nine would be six duplicated columns waiting to disagree with Tenants. Only
   the three with no home are stored. vDmeBillingProvider joins the rest.

   Idempotent: safe to re-run.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* ---------------------------------------------------------------------------
   1. The three supplier facts that have no other home
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmeSupplierProfile','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeSupplierProfile (
        -- TenantId is the primary key, not an identity column. One tenant has
        -- exactly one supplier identity today, and a surrogate key would invite
        -- a second row that nothing chooses between.
        TenantId         INT          NOT NULL PRIMARY KEY,
        Ptan             NVARCHAR(20) NULL,   -- Medicare supplier number
        TaxonomyCode     NVARCHAR(15) NULL,   -- CMS-1500 box 33b
        AcceptsAssignment BIT         NOT NULL CONSTRAINT DF_DmeSupplier_Assign DEFAULT 1,
        UpdatedAt        DATETIME2    NOT NULL CONSTRAINT DF_DmeSupplier_Updated DEFAULT SYSUTCDATETIME(),
        UpdatedBy        INT          NULL
    );
    PRINT 'Created DmeSupplierProfile';
END
GO

/* ---------------------------------------------------------------------------
   2. The clearinghouse credential

   Username AND password are both encrypted. A username is half a credential and
   is protected like the other half. They are never returned to any client and
   never logged: see Services/DmeSftpAccountService.cs, which is the only code
   that reads them.

   IsTestMode DEFAULTS TO 1, and that default is a safety property rather than a
   convenience. Test versus production belongs to the account, not to the
   deployment, which is how clearinghouse onboarding actually runs: you send test
   files until they pass, then you are switched live. A freshly entered account
   therefore cannot transmit a live claim by accident, and going live is a
   deliberate edit somebody has to make.

   IsActive exists separately from the row because the row must not be deleted
   once anything has been submitted through it. Deleting it would destroy the
   record of what a past claim was submitted under.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DmeSftpAccounts','U') IS NULL
BEGIN
    CREATE TABLE dbo.DmeSftpAccounts (
        SftpAccountId INT IDENTITY(1,1) PRIMARY KEY,
        TenantId      INT           NOT NULL CONSTRAINT DF_DmeSftp_Tenant DEFAULT 1,
        -- Human label. Needed because Username is ciphertext and cannot be
        -- shown, so a list of accounts would otherwise have nothing to tell
        -- rows apart by.
        Label         NVARCHAR(60)  NOT NULL,
        Host          NVARCHAR(120) NOT NULL,
        Port          INT           NOT NULL CONSTRAINT DF_DmeSftp_Port DEFAULT 22,
        Username      NVARCHAR(400) NOT NULL,   -- AES-GCM ciphertext
        Password      NVARCHAR(400) NOT NULL,   -- AES-GCM ciphertext
        IsTestMode    BIT           NOT NULL CONSTRAINT DF_DmeSftp_Test DEFAULT 1,
        IsActive      BIT           NOT NULL CONSTRAINT DF_DmeSftp_Active DEFAULT 1,
        CreatedAt     DATETIME2     NOT NULL CONSTRAINT DF_DmeSftp_Created DEFAULT SYSUTCDATETIME(),
        CreatedBy     INT           NULL,
        UpdatedAt     DATETIME2     NULL,
        UpdatedBy     INT           NULL
    );
    CREATE INDEX IX_DmeSftpAccounts_TenantId ON dbo.DmeSftpAccounts(TenantId);
    PRINT 'Created DmeSftpAccounts';
END
GO

/* ---------------------------------------------------------------------------
   3. Row level security on both

   Via sp_executesql for the same compile-time reason as every other migration
   here: ALTER SECURITY POLICY is validated when the batch is compiled, so an
   IF NOT EXISTS guard around a bare ALTER does not survive a re-run.

   These two tables matter more than most. A FILTER-only policy would stop one
   supplier reading another's clearinghouse password but would still let a write
   plant a row in their tenant, and a planted SFTP account is a credential
   substitution attack.
   --------------------------------------------------------------------------- */
DECLARE @rls SYSNAME, @rlsSql NVARCHAR(MAX);
DECLARE rlscur CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES ('DmeSupplierProfile'), ('DmeSftpAccounts')) AS x(name);

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
   4. Read models
   --------------------------------------------------------------------------- */

/* The billing provider as the CMS-1500 needs it, assembled rather than stored.
   Correcting the supplier's address on the tenant record fixes every claim form
   at once, which is the whole point of not copying it.

   INNER JOIN on purpose, and it is what scopes this view. dbo.Tenants is the
   platform tenant registry and is not covered by TenantIsolationPolicy, but
   DmeSupplierProfile IS, so the join returns only the caller's row. A LEFT JOIN
   from Tenants would have handed back every tenant's name and NPI. */
CREATE OR ALTER VIEW dbo.vDmeBillingProvider AS
SELECT
    t.TenantId,
    BillingName = t.Name,
    t.Address, t.City, t.State, t.ZipCode, t.Phone,
    TaxId = t.TaxId,
    Npi   = t.NPI,
    s.Ptan, s.TaxonomyCode, s.AcceptsAssignment,
    -- Derived: is this supplier ready to have a claim filed under its name?
    -- A claim needs a name, an NPI and a tax ID at minimum. Computing the answer
    -- here means the CMS-1500 screen, the Submit guard and the settings page
    -- cannot each decide it differently.
    IsComplete = CAST(CASE
        WHEN NULLIF(LTRIM(RTRIM(t.Name)),'')  IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(t.NPI)),'')   IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(t.TaxId)),'') IS NOT NULL
        THEN 1 ELSE 0 END AS BIT)
FROM dbo.DmeSupplierProfile s
JOIN dbo.Tenants t ON t.TenantId = s.TenantId;
GO

/* The SFTP accounts WITHOUT the credentials.

   The view exists so that no screen, list or report has to remember to leave
   Username and Password out. Selecting * from this view is safe; selecting * from
   the base table is the mistake it removes. Only DmeSftpAccountService reads the
   base table, and only to decrypt for a transmission. */
CREATE OR ALTER VIEW dbo.vDmeSftpAccounts AS
SELECT
    a.SftpAccountId, a.TenantId, a.Label, a.Host, a.Port,
    a.IsTestMode, a.IsActive,
    a.CreatedAt, a.CreatedBy, a.UpdatedAt, a.UpdatedBy,
    -- Derived: proof a credential is stored, without revealing any part of it.
    HasCredentials = CAST(CASE WHEN LEN(a.Username) > 0 AND LEN(a.Password) > 0
                               THEN 1 ELSE 0 END AS BIT)
FROM dbo.DmeSftpAccounts a;
GO

/* vDmeClaims gains the ORDERING PHYSICIAN.

   CMS-1500 box 17 is the referring or ordering provider. On a professional
   claim it is often optional; on DMEPOS it is mandatory, because the whole
   claim rests on a physician having ordered the equipment. The form was
   rendering no box 17 at all.

   The data was already there and unused: DmeClaims.OrderId -> DmeOrders.DoctorId
   -> DmeDoctors. Derived by join, never stored, so correcting a doctor's NPI
   fixes every claim form that names them.

   This view is redefined here rather than edited in place in the migration that
   created it. Migrations are an ordered chain: the last definition wins, and
   editing history would mean a database built from the repo differed from one
   that was migrated. */
CREATE OR ALTER VIEW dbo.vDmeClaims AS
SELECT
    c.ClaimId, c.TenantId, c.ClaimNumber, c.OrderId, c.CustomerId,
    c.CustomerName,         -- as filed: a submitted claim is a document
    c.PayerName,            -- as filed: customer may switch insurer later
    c.Status,               -- submission lifecycle only: ready | submitted
    c.ServiceDate, c.CreatedAt,

    ch.ChargeTotal AS Total,
    ch.LineCount,

    a.AllowedTotal, a.PaidTotal, a.PayerPaidTotal, a.CustomerPaidTotal,
    a.ContractualTotal, a.PatientResponsibilityTotal, a.OtherAdjustmentTotal,
    a.DeniedCharge, a.PaymentCount,

    b.InsuranceBalance,
    b.PatientBalance,
    b.InsuranceBalance + b.PatientBalance AS Balance,

    -- Derived: the ordering physician, for CMS-1500 box 17 and 17b. Mandatory
    -- on DMEPOS. Doctor names are not patient PHI and are not encrypted, so
    -- unlike the customer name this one is composed in SQL.
    dr.OrderingDoctorName,
    dr.OrderingDoctorNpi,

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
    -- entire mechanism by which a void undoes a posting.
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
        InsuranceBalance = ch.ChargeTotal - a.PayerPaidTotal - a.ContractualTotal
                           - a.OtherAdjustmentTotal - a.PatientResponsibilityTotal,
        PatientBalance   = a.PatientResponsibilityTotal - a.CustomerPaidTotal
) b
OUTER APPLY (
    SELECT TOP 1
        OrderingDoctorName = 'Dr. ' + d.FirstName + ' ' + d.LastName,
        OrderingDoctorNpi  = d.Npi
    FROM dbo.DmeOrders o
    JOIN dbo.DmeDoctors d ON d.DoctorId = o.DoctorId
    WHERE o.OrderId = c.OrderId
) dr;
GO

/* ---------------------------------------------------------------------------
   5. Seed

   Every existing tenant gets a supplier profile row, because without one
   vDmeBillingProvider returns nothing and the CMS-1500 has no billing provider
   at all. Blank is the right starting state: it is honest, and the settings
   screen reports exactly what is missing.
   --------------------------------------------------------------------------- */
INSERT INTO dbo.DmeSupplierProfile (TenantId)
SELECT t.TenantId FROM dbo.Tenants t
WHERE NOT EXISTS (SELECT 1 FROM dbo.DmeSupplierProfile s WHERE s.TenantId = t.TenantId);
GO

/* Demo tenant only. The demo database still carries the clinical tenant name
   this product was forked from, which is why the CMS-1500 had a hardcoded
   supplier name in the first place: the real one was wrong. Fixed at the source
   rather than papered over in the view, and guarded so it can never touch a
   tenant that has been named properly. */
IF EXISTS (SELECT 1 FROM dbo.Tenants WHERE TenantId = 1 AND Name = 'MedGroup Internal Medicine')
BEGIN
    UPDATE dbo.Tenants
       SET Name    = 'Lakeview Medical Supply',
           TaxId   = ISNULL(NULLIF(TaxId,''), '75-1839204'),
           NPI     = ISNULL(NULLIF(NPI,''),   '1982741630'),
           Address = ISNULL(NULLIF(Address,''), '4120 Harry Hines Blvd, Suite 210'),
           City    = ISNULL(NULLIF(City,''),  'Dallas'),
           State   = ISNULL(NULLIF(State,''), 'TX'),
           ZipCode = ISNULL(NULLIF(ZipCode,''), '75219'),
           Phone   = ISNULL(NULLIF(Phone,''), '(214) 555-0180')
     WHERE TenantId = 1;

    UPDATE dbo.DmeSupplierProfile
       SET Ptan = ISNULL(Ptan, '0123456789'), TaxonomyCode = ISNULL(TaxonomyCode, '332B00000X')
     WHERE TenantId = 1;

    PRINT 'Demo tenant renamed to the DME supplier it actually is';
END
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT TenantId, BillingName, Npi, TaxId, Ptan, TaxonomyCode, IsComplete
FROM dbo.vDmeBillingProvider ORDER BY TenantId;

SELECT ClaimNumber, OrderingDoctorName, OrderingDoctorNpi, PaymentStatus
FROM dbo.vDmeClaims ORDER BY ClaimId;

SELECT SftpAccountCount = COUNT(*) FROM dbo.vDmeSftpAccounts;
GO
