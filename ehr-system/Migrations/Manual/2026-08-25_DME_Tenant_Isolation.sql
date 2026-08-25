/* ============================================================================
   DME Tenant Isolation  (DMEEHR database)

   WHY
   Four of the fourteen DME tables carried a TenantId; the other ten did not,
   and none of them were covered by the TenantIsolationPolicy that already
   protects the clinical tables. A DME query therefore returned every tenant's
   rows, and the product cannot be resold as SaaS until that is closed.

   WHAT THIS DOES
   1. Adds TenantId to every DME table that lacks one, backfilled to 1.
   2. Makes DmeSeq per-tenant so order/claim numbering cannot collide or leak
      volume between tenants.
   3. Extends the existing TenantIsolationPolicy with FILTER (reads) and BLOCK
      (writes) predicates over every DME table.

   SAFETY
   Verified before writing: all existing DME rows are TenantId = 1, so nothing
   already in the database violates the new constraint.
   Idempotent: safe to re-run.

   The predicate function dbo.fn_TenantPredicate already exists (created with
   the clinical policy) and falls through when SESSION_CONTEXT is unset, which
   is how migrations, background jobs and this script itself keep working.
   The application side guarantees the context IS always set for DME requests
   (see Helpers/DmeDb.cs), so that fallback is never reached on a user path.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* ---------------------------------------------------------------------------
   0. Predicate function and policy

   Both normally already exist, created alongside the clinical tables. They are
   created here when missing so this migration set can build a DME database on
   its own: without this, a fresh checkout fails at the first ALTER SECURITY
   POLICY with "cannot find the object", which is a confusing way to discover a
   missing dependency.

   The predicate falls through when SESSION_CONTEXT is unset so migrations,
   background jobs and this script keep working. DmeDb guarantees the context IS
   set on every user-facing connection, so that fallback is never reached on a
   user path.
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.fn_TenantPredicate','IF') IS NULL
    EXEC sp_executesql N'
        CREATE FUNCTION dbo.fn_TenantPredicate(@RowTenantId INT)
        RETURNS TABLE
        WITH SCHEMABINDING
        AS RETURN
            SELECT 1 AS fn_Result
            WHERE @RowTenantId = CAST(SESSION_CONTEXT(N''CurrentTenantId'') AS INT)
               OR SESSION_CONTEXT(N''CurrentTenantId'') IS NULL;';
GO

IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantIsolationPolicy')
BEGIN
    -- A policy needs at least one predicate at creation time, so it is created
    -- against DmeCustomers and every other table is added by the loop below.
    EXEC sp_executesql N'
        CREATE SECURITY POLICY dbo.TenantIsolationPolicy
            ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.DmeCustomers
            WITH (STATE = ON);';
    PRINT 'Created TenantIsolationPolicy';
END
GO

/* ---------------------------------------------------------------------------
   1. TenantId columns
   HcpcsCodes is included deliberately: each DME supplier maintains its own
   item master and its own contract pricing, so the catalog is tenant data,
   not a shared national reference table.
   --------------------------------------------------------------------------- */
DECLARE @t SYSNAME, @sql NVARCHAR(MAX);
DECLARE tbl CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES
        ('DmeCustomerInsurances'), ('DmeCustomerDiagnoses'), ('DmeDoctors'),
        ('DmePayers'), ('DmeSerializedUnits'), ('DmeCmns'),
        ('DmeOrderLines'), ('DmeClaimLines'), ('HcpcsCodes')
    ) AS x(name);

OPEN tbl;
FETCH NEXT FROM tbl INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID('dbo.' + @t) AND name = 'TenantId')
    BEGIN
        SET @sql = 'ALTER TABLE dbo.' + QUOTENAME(@t) +
                   ' ADD TenantId INT NOT NULL CONSTRAINT DF_' + @t + '_Tenant DEFAULT 1;';
        EXEC sp_executesql @sql;
        PRINT 'Added TenantId to ' + @t;
    END
    FETCH NEXT FROM tbl INTO @t;
END
CLOSE tbl; DEALLOCATE tbl;
GO

/* Index the tenant column everywhere it is now filtered on. Without these,
   RLS turns every DME read into a scan once a second tenant has data. */
DECLARE @t2 SYSNAME, @sql2 NVARCHAR(MAX);
DECLARE tbl2 CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES
        ('DmeCustomers'), ('DmeOrders'), ('DmeRentals'), ('DmeClaims'),
        ('DmeCustomerInsurances'), ('DmeCustomerDiagnoses'), ('DmeDoctors'),
        ('DmePayers'), ('DmeSerializedUnits'), ('DmeCmns'),
        ('DmeOrderLines'), ('DmeClaimLines'), ('HcpcsCodes')
    ) AS x(name);

OPEN tbl2;
FETCH NEXT FROM tbl2 INTO @t2;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID('dbo.' + @t2) AND name = 'IX_' + @t2 + '_TenantId')
    BEGIN
        SET @sql2 = 'CREATE INDEX IX_' + @t2 + '_TenantId ON dbo.' + QUOTENAME(@t2) + '(TenantId);';
        EXEC sp_executesql @sql2;
        PRINT 'Indexed TenantId on ' + @t2;
    END
    FETCH NEXT FROM tbl2 INTO @t2;
END
CLOSE tbl2; DEALLOCATE tbl2;
GO

/* ---------------------------------------------------------------------------
   2. Per-tenant sequences
   DmeSeq was keyed on Name alone, so every tenant would have shared one
   ORD / CLM counter. Two tenants would see each other's order volume in the
   gaps, and a busy tenant would push another tenant's numbers forward.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeSeq') AND name = 'TenantId')
BEGIN
    DECLARE @pk SYSNAME = (SELECT name FROM sys.key_constraints
                           WHERE parent_object_id = OBJECT_ID('dbo.DmeSeq') AND type = 'PK');
    IF @pk IS NOT NULL EXEC('ALTER TABLE dbo.DmeSeq DROP CONSTRAINT ' + @pk);

    ALTER TABLE dbo.DmeSeq ADD TenantId INT NOT NULL CONSTRAINT DF_DmeSeq_Tenant DEFAULT 1;
    PRINT 'Added TenantId to DmeSeq';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints
               WHERE parent_object_id = OBJECT_ID('dbo.DmeSeq') AND type = 'PK')
BEGIN
    ALTER TABLE dbo.DmeSeq ADD CONSTRAINT PK_DmeSeq PRIMARY KEY (Name, TenantId);
    PRINT 'Rekeyed DmeSeq on (Name, TenantId)';
END
GO

/* ---------------------------------------------------------------------------
   3. Row Level Security over the DME tables

   FILTER stops a read returning another tenant's rows.
   BLOCK stops a write planting a row in another tenant, which FILTER alone
   does not prevent: without it an INSERT can still specify any TenantId it
   likes and simply become invisible to the tenant that wrote it.
   --------------------------------------------------------------------------- */
DECLARE @t3 SYSNAME, @sql3 NVARCHAR(MAX);
DECLARE tbl3 CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES
        ('DmeCustomers'), ('DmeOrders'), ('DmeRentals'), ('DmeClaims'),
        ('DmeCustomerInsurances'), ('DmeCustomerDiagnoses'), ('DmeDoctors'),
        ('DmePayers'), ('DmeSerializedUnits'), ('DmeCmns'),
        ('DmeOrderLines'), ('DmeClaimLines'), ('HcpcsCodes'), ('DmeSeq')
    ) AS x(name);

OPEN tbl3;
FETCH NEXT FROM tbl3 INTO @t3;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.security_predicates sp
        JOIN sys.security_policies p ON p.object_id = sp.object_id
        WHERE p.name = 'TenantIsolationPolicy'
          AND sp.target_object_id = OBJECT_ID('dbo.' + @t3)
          AND sp.predicate_type_desc = 'FILTER')
    BEGIN
        SET @sql3 = 'ALTER SECURITY POLICY dbo.TenantIsolationPolicy
                     ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.' + QUOTENAME(@t3) + ';';
        EXEC sp_executesql @sql3;
        PRINT 'FILTER predicate added on ' + @t3;
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.security_predicates sp
        JOIN sys.security_policies p ON p.object_id = sp.object_id
        WHERE p.name = 'TenantIsolationPolicy'
          AND sp.target_object_id = OBJECT_ID('dbo.' + @t3)
          AND sp.predicate_type_desc = 'BLOCK')
    BEGIN
        SET @sql3 = 'ALTER SECURITY POLICY dbo.TenantIsolationPolicy
                     ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.' + QUOTENAME(@t3) + ' AFTER INSERT;';
        EXEC sp_executesql @sql3;
        SET @sql3 = 'ALTER SECURITY POLICY dbo.TenantIsolationPolicy
                     ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON dbo.' + QUOTENAME(@t3) + ' AFTER UPDATE;';
        EXEC sp_executesql @sql3;
        PRINT 'BLOCK predicates added on ' + @t3;
    END

    FETCH NEXT FROM tbl3 INTO @t3;
END
CLOSE tbl3; DEALLOCATE tbl3;
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT OBJECT_NAME(sp.target_object_id) AS ProtectedTable,
       sp.predicate_type_desc,
       sp.operation_desc
FROM sys.security_predicates sp
JOIN sys.security_policies p ON p.object_id = sp.object_id
WHERE p.name = 'TenantIsolationPolicy'
  AND OBJECT_NAME(sp.target_object_id) LIKE 'Dme%'
   OR OBJECT_NAME(sp.target_object_id) = 'HcpcsCodes'
ORDER BY ProtectedTable, sp.predicate_type_desc, sp.operation_desc;
GO
