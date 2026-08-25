/* ============================================================================
   Drop the clinical EHR schema  (DMEEHR database)

   WHY
   MEDOCS DME is a standalone DME application. It was built by copying the whole
   IMEHR EHR and adding DME tables beside it, and nobody removed the EHR. Of the
   100 tables in this database, DME used 23.

   The clinical half was never wired to the DME half: not one row crossed
   between them. So this is not repairing corruption, it is removing weight the
   product was carrying and could not use, including 634 patient records and
   1,745 clinical claims that a DME supplier has no business holding.

   The application code for all of it was removed first (commit dbebb5b) and the
   product verified working without it. This script removes the tables those
   controllers and services used to read.

   WHAT SURVIVES
     DME:      DmeCustomers, DmeOrders, DmeOrderLines, DmeRentals, DmeClaims,
               DmeClaimLines, DmeSerializedUnits, DmeCmns, DmeDoctors,
               DmePayers, DmeSeq, DmeCustomerInsurances, DmeCustomerDiagnoses,
               DmeStockMovements, DmeCustomerSearchTokens, HcpcsCodes
     Platform: Users, Tenants, Locations, AuditLogs, TrustedDevices

   Everything else goes.

   SAFETY
   Foreign keys are dropped first, so table order does not matter and the script
   cannot fail halfway on a dependency. Only tables NOT in the keep list are
   touched, so adding a DME table later needs no edit here.

   AuditLogs is deliberately kept and deliberately NOT emptied. It is the HIPAA
   record of who did what, including everything that happened while the clinical
   half existed, and deleting audit history to tidy up is precisely what an audit
   trail exists to prevent.

   Idempotent: safe to re-run. Take a backup first; this deletes patient data.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* The tables MEDOCS DME actually uses. Anything absent from this list is
   clinical leftover and is dropped below. */
IF OBJECT_ID('tempdb..#Keep') IS NOT NULL DROP TABLE #Keep;
CREATE TABLE #Keep (name SYSNAME PRIMARY KEY);

INSERT INTO #Keep (name) VALUES
    -- DME product
    ('DmeCustomers'), ('DmeCustomerInsurances'), ('DmeCustomerDiagnoses'),
    ('DmeCustomerSearchTokens'), ('DmeDoctors'), ('DmePayers'),
    ('DmeSerializedUnits'), ('DmeCmns'), ('DmeOrders'), ('DmeOrderLines'),
    ('DmeRentals'), ('DmeClaims'), ('DmeClaimLines'), ('DmeStockMovements'),
    ('DmeSeq'), ('HcpcsCodes'),
    -- Platform the product needs
    ('Users'), ('Tenants'), ('Locations'), ('AuditLogs'), ('TrustedDevices'),
    -- EF's own bookkeeping
    ('__EFMigrationsHistory');
GO

/* ---------------------------------------------------------------------------
   1. Drop foreign keys pointing INTO or OUT OF the tables being removed

   Done as a separate pass so the drop order below is irrelevant. A keeper
   table can hold an FK to a clinical table (Locations -> StripeConnectAccounts,
   Users -> Providers); those constraints must go too or the drop fails.
   --------------------------------------------------------------------------- */
DECLARE @sql NVARCHAR(MAX) = N'';

SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.'
            + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10)
FROM sys.foreign_keys fk
JOIN sys.tables t  ON t.object_id  = fk.parent_object_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
WHERE t.name  NOT IN (SELECT name FROM #Keep)
   OR rt.name NOT IN (SELECT name FROM #Keep);

IF LEN(@sql) > 0
BEGIN
    EXEC sp_executesql @sql;
    PRINT 'Dropped foreign keys involving clinical tables';
END
GO

/* ---------------------------------------------------------------------------
   2. Drop row level security predicates on tables about to disappear

   A security policy holds a schema-bound reference, so the drop fails while the
   predicate exists. Only clinical tables are touched; the DME predicates stay.
   --------------------------------------------------------------------------- */
DECLARE @rls NVARCHAR(MAX) = N'';

SELECT @rls = @rls + N'ALTER SECURITY POLICY ' + QUOTENAME(SCHEMA_NAME(p.schema_id)) + N'.'
            + QUOTENAME(p.name) + N' DROP FILTER PREDICATE ON '
            + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.security_predicates sp
JOIN sys.security_policies p ON p.object_id = sp.object_id
JOIN sys.tables t ON t.object_id = sp.target_object_id
WHERE sp.predicate_type_desc = 'FILTER'
  AND t.name NOT IN (SELECT name FROM #Keep);

IF LEN(@rls) > 0
BEGIN
    EXEC sp_executesql @rls;
    PRINT 'Removed row level security predicates from clinical tables';
END
GO

/* ---------------------------------------------------------------------------
   3. Drop views that reference clinical tables

   Dropped by name rather than by dependency because a view over a missing table
   is not an error in SQL Server, just a broken object that fails at query time.
   The DME views (vDme*, vHcpcsCatalog) are kept.
   --------------------------------------------------------------------------- */
DECLARE @views NVARCHAR(MAX) = N'';

SELECT @views = @views + N'DROP VIEW ' + QUOTENAME(SCHEMA_NAME(v.schema_id)) + N'.'
              + QUOTENAME(v.name) + N';' + CHAR(10)
FROM sys.views v
WHERE v.name NOT LIKE 'vDme%'
  AND v.name <> 'vHcpcsCatalog';

IF LEN(@views) > 0
BEGIN
    EXEC sp_executesql @views;
    PRINT 'Dropped clinical views';
END
GO

/* ---------------------------------------------------------------------------
   4. Drop the clinical tables
   --------------------------------------------------------------------------- */
DECLARE @drop NVARCHAR(MAX) = N'';
DECLARE @count INT;

SELECT @drop = @drop + N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.'
             + QUOTENAME(t.name) + N';' + CHAR(10)
FROM sys.tables t
WHERE t.name NOT IN (SELECT name FROM #Keep);

SELECT @count = COUNT(*) FROM sys.tables WHERE name NOT IN (SELECT name FROM #Keep);

IF LEN(@drop) > 0
BEGIN
    EXEC sp_executesql @drop;
    PRINT CONCAT('Dropped ', @count, ' clinical table(s)');
END
ELSE
    PRINT 'No clinical tables remain; nothing to do';
GO

/* ---------------------------------------------------------------------------
   5. Drop the columns on kept tables that only existed for the clinical side

   Users.ProviderId linked a login to a treating clinician. DME has no
   clinicians, and the application no longer reads or writes it.
   --------------------------------------------------------------------------- */
DECLARE @df SYSNAME, @ix SYSNAME, @stmt NVARCHAR(MAX);

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'ProviderId')
BEGIN
    SELECT @df = dc.name FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Users') AND c.name = 'ProviderId';
    IF @df IS NOT NULL
    BEGIN
        SET @stmt = N'ALTER TABLE dbo.Users DROP CONSTRAINT ' + QUOTENAME(@df) + N';';
        EXEC sp_executesql @stmt;
    END

    SELECT @ix = i.name FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID('dbo.Users') AND c.name = 'ProviderId' AND i.is_primary_key = 0;
    IF @ix IS NOT NULL
    BEGIN
        SET @stmt = N'DROP INDEX ' + QUOTENAME(@ix) + N' ON dbo.Users;';
        EXEC sp_executesql @stmt;
    END

    ALTER TABLE dbo.Users DROP COLUMN ProviderId;
    PRINT 'Dropped Users.ProviderId';
END
GO

/* Locations.StripeConnectAccountId pointed at the clinic card-payment
   integration, which is gone. Declared outside the IF because SQL Server hoists
   DECLARE to batch scope: declaring it inside a conditional block still fails to
   parse if the same name is declared twice in the batch. */
DECLARE @ix2 SYSNAME, @stmt2 NVARCHAR(MAX);

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Locations') AND name = 'StripeConnectAccountId')
BEGIN
    SELECT @ix2 = i.name FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID('dbo.Locations') AND c.name = 'StripeConnectAccountId' AND i.is_primary_key = 0;

    IF @ix2 IS NOT NULL
    BEGIN
        SET @stmt2 = N'DROP INDEX ' + QUOTENAME(@ix2) + N' ON dbo.Locations;';
        EXEC sp_executesql @stmt2;
    END

    ALTER TABLE dbo.Locations DROP COLUMN StripeConnectAccountId;
    PRINT 'Dropped Locations.StripeConnectAccountId';
END
GO

DROP TABLE #Keep;
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT COUNT(*) AS TablesRemaining FROM sys.tables;
SELECT name AS TableName FROM sys.tables ORDER BY name;
SELECT name AS ViewName FROM sys.views ORDER BY name;
GO
