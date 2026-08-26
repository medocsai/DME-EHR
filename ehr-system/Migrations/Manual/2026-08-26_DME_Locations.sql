/* ============================================================================
   DME Locations  (DMEEHR database)

   WHY
   A DME supplier with several branches is one business, not several. One owner,
   one set of books, shared administration, staff who work at a branch and an
   owner who wants the whole picture. Making each branch its own TENANT would cut
   the roll-up in half and duplicate the catalog, the payers and the users.

   So: one tenant, many locations. The tenant stays the security boundary. The
   location is a working filter inside it.

   WHERE LocationId GOES, AND WHY IT IS NOT EVERYWHERE
   The obvious design is a LocationId on every table. It is also the one that
   fails, and RehabDox already ran the experiment for us:

     Appointments.LocationId      NULL on 164 of 165 rows
     Patients                     no LocationId at all, only PreferredLocationId

   The copy on the child row drifted to NULL, so the filter had to become a
   fallback chain, repeated in eight or more places:

     a.LocationId == locId ||
       (!a.LocationId.HasValue &&
         (a.Patient.PreferredLocationId == locId || a.Patient.PreferredLocationId == null))

   Where they derive from the patient instead (CareEpisodeServices), there is no
   fallback and no bug.

   So the customer's branch is stored ONCE, on the customer, and orders, rentals,
   claims, claim lines and payments all derive it by join. There is no second
   copy to go stale, and therefore no fallback to write.

   THE ONE EXCEPTION: INVENTORY
   Stock is physical. A concentrator in the Dallas depot is not in the Houston
   depot, and no customer owns it. DmeStockMovements and DmeSerializedUnits
   therefore carry their own LocationId. That is a different fact, not a
   duplicate of the customer's.

   WHAT DELIBERATELY DOES NOT HAPPEN HERE
   Location is NOT added to the row level security policy. Tenant is enforced by
   the database and must stay that way. If location were enforced there too, the
   owner's all-locations view would need a hole punched through the isolation to
   work at all. Location filtering is an application concern, and NULL means
   "every location", which is exactly the roll-up.

   Idempotent: safe to re-run.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* ---------------------------------------------------------------------------
   1. One primary location per tenant

   Checked before constraining, per the standing rule, and the data ALREADY
   violates it: tenant 1 has two locations flagged primary (Main Office and Main
   Clinic). Everything below defaults a row to "the tenant's primary location",
   so an ambiguous primary would make the backfill arbitrary.

   Lowest LocationId wins, because it is the one the tenant was created with.
   The rest are demoted and PRINTed rather than silently changed.
   --------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM dbo.Locations WHERE IsPrimary = 1
           GROUP BY TenantId HAVING COUNT(*) > 1)
BEGIN
    DECLARE @demoted INT;

    UPDATE l SET IsPrimary = 0
    FROM dbo.Locations l
    WHERE l.IsPrimary = 1
      AND l.LocationId > (SELECT MIN(l2.LocationId) FROM dbo.Locations l2
                          WHERE l2.TenantId = l.TenantId AND l2.IsPrimary = 1);
    SET @demoted = @@ROWCOUNT;

    PRINT 'Demoted ' + CAST(@demoted AS NVARCHAR(10)) +
          ' duplicate primary location(s). A tenant has exactly one.';
END
GO

/* A tenant with locations but none marked primary cannot answer "where does a
   new customer go", so give it one. */
UPDATE l SET IsPrimary = 1
FROM dbo.Locations l
WHERE l.LocationId = (SELECT MIN(l2.LocationId) FROM dbo.Locations l2 WHERE l2.TenantId = l.TenantId)
  AND NOT EXISTS (SELECT 1 FROM dbo.Locations l3 WHERE l3.TenantId = l.TenantId AND l3.IsPrimary = 1);
GO

/* Filtered unique index: the guard that stops it happening again. Filtered, so
   it constrains only the primaries and leaves every other location alone.
   Requires QUOTED_IDENTIFIER ON, which is set at the top of this file, and on
   any ad-hoc session that later writes to Locations. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Locations_OnePrimaryPerTenant')
    CREATE UNIQUE INDEX UX_Locations_OnePrimaryPerTenant
        ON dbo.Locations(TenantId) WHERE IsPrimary = 1;
GO

/* ---------------------------------------------------------------------------
   2. The customer's branch: the one place a location is stored for people

   NOT NULL, and that is the whole point. RehabDox's column was nullable and
   drifted to NULL, which is what forced the fallback chain. A column that
   cannot be NULL cannot drift into one.
   --------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.DmeCustomers') AND name = 'LocationId')
BEGIN
    -- Added nullable, backfilled, then tightened. Adding it NOT NULL outright
    -- would need a default, and a default here would be a lie: there is no
    -- sensible constant, the right value depends on the row's tenant.
    ALTER TABLE dbo.DmeCustomers ADD LocationId INT NULL;
    PRINT 'Added DmeCustomers.LocationId';
END
GO

UPDATE c SET LocationId = (SELECT TOP 1 l.LocationId FROM dbo.Locations l
                           WHERE l.TenantId = c.TenantId
                           ORDER BY CASE WHEN l.IsPrimary = 1 THEN 0 ELSE 1 END, l.LocationId)
FROM dbo.DmeCustomers c
WHERE c.LocationId IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DmeCustomers') AND name = 'LocationId' AND is_nullable = 1)
   AND NOT EXISTS (SELECT 1 FROM dbo.DmeCustomers WHERE LocationId IS NULL)
BEGIN
    ALTER TABLE dbo.DmeCustomers ALTER COLUMN LocationId INT NOT NULL;
    PRINT 'DmeCustomers.LocationId is NOT NULL';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DmeCustomers_Location')
   AND NOT EXISTS (SELECT 1 FROM dbo.DmeCustomers WHERE LocationId IS NULL)
BEGIN
    ALTER TABLE dbo.DmeCustomers WITH CHECK
        ADD CONSTRAINT FK_DmeCustomers_Location FOREIGN KEY (LocationId) REFERENCES dbo.Locations(LocationId);
    PRINT 'DmeCustomers.LocationId references Locations';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DmeCustomers_LocationId')
    CREATE INDEX IX_DmeCustomers_LocationId ON dbo.DmeCustomers(TenantId, LocationId);
GO

/* ---------------------------------------------------------------------------
   3. Inventory carries its own location, because stock is physical

   These are NOT a copy of anything. A stock movement happens at a place, and a
   serialised unit sits at one. Neither fact is reachable from a customer: most
   stock has no customer at all.
   --------------------------------------------------------------------------- */
DECLARE @invTable SYSNAME, @invSql NVARCHAR(MAX);
DECLARE invcur CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES ('DmeStockMovements'), ('DmeSerializedUnits')) AS x(name);

OPEN invcur;
FETCH NEXT FROM invcur INTO @invTable;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID('dbo.' + @invTable) AND name = 'LocationId')
    BEGIN
        SET @invSql = 'ALTER TABLE dbo.' + QUOTENAME(@invTable) + ' ADD LocationId INT NULL;';
        EXEC sp_executesql @invSql;
        PRINT 'Added ' + @invTable + '.LocationId';
    END

    SET @invSql = '
        UPDATE t SET LocationId = (SELECT TOP 1 l.LocationId FROM dbo.Locations l
                                   WHERE l.TenantId = t.TenantId
                                   ORDER BY CASE WHEN l.IsPrimary = 1 THEN 0 ELSE 1 END, l.LocationId)
        FROM dbo.' + QUOTENAME(@invTable) + ' t
        WHERE t.LocationId IS NULL;';
    EXEC sp_executesql @invSql;

    FETCH NEXT FROM invcur INTO @invTable;
END
CLOSE invcur; DEALLOCATE invcur;
GO

/* Tightened BEFORE the indexes are created, and separately from the cursor.
   Two reasons, both learned the hard way on the first run:
     1. An index that references the column blocks ALTER COLUMN outright
        ("the index is dependent on column LocationId"), so index first is the
        wrong order.
     2. A tenant with no Locations row at all would leave these NULL, and the
        ALTER failing inside the cursor would abandon the rest of the loop. */
IF NOT EXISTS (SELECT 1 FROM dbo.DmeStockMovements WHERE LocationId IS NULL)
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DmeStockMovements') AND name = 'LocationId' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.DmeStockMovements ALTER COLUMN LocationId INT NOT NULL;
    PRINT 'DmeStockMovements.LocationId is NOT NULL';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.DmeSerializedUnits WHERE LocationId IS NULL)
   AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DmeSerializedUnits') AND name = 'LocationId' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.DmeSerializedUnits ALTER COLUMN LocationId INT NOT NULL;
    PRINT 'DmeSerializedUnits.LocationId is NOT NULL';
END
GO

/* Indexes last, now that the column shape is settled. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DmeStockMovements_LocationId')
    CREATE INDEX IX_DmeStockMovements_LocationId ON dbo.DmeStockMovements(TenantId, LocationId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DmeSerializedUnits_LocationId')
    CREATE INDEX IX_DmeSerializedUnits_LocationId ON dbo.DmeSerializedUnits(TenantId, LocationId);
GO

/* ---------------------------------------------------------------------------
   4. Read models

   Every view below DERIVES the location. Only DmeCustomers and the two
   inventory tables store one.
   --------------------------------------------------------------------------- */

CREATE OR ALTER VIEW dbo.vDmeOrders AS
SELECT
    o.OrderId, o.TenantId, o.OrderNumber, o.CustomerId, o.Status, o.Stage,
    o.DoctorId, o.DeliveryDate, o.DeliveryAddress, o.Deposit, o.PodSignedBy,
    o.PodSignedAt, o.PodSignature, o.CreatedAt,
    cu.FirstName AS CustomerFirstName,
    cu.LastName  AS CustomerLastName,
    -- Derived: an order belongs to the branch its customer belongs to. Storing
    -- it again on the order is what produced NULLs in the reference product.
    cu.LocationId,
    loc.Name AS LocationName,
    CASE WHEN d.DoctorId IS NULL THEN '' ELSE 'Dr. ' + d.FirstName + ' ' + d.LastName END AS DoctorName,
    (SELECT COUNT(*) FROM dbo.DmeOrderLines l WHERE l.OrderId = o.OrderId) AS LineCount,
    (SELECT ISNULL(SUM(CASE WHEN l.Mode = 'purchase' THEN l.UnitPrice * l.Qty ELSE l.MonthlyRate END), 0)
     FROM dbo.DmeOrderLines l WHERE l.OrderId = o.OrderId) AS Total
FROM dbo.DmeOrders o
LEFT JOIN dbo.DmeCustomers cu ON cu.CustomerId = o.CustomerId
LEFT JOIN dbo.Locations    loc ON loc.LocationId = cu.LocationId
LEFT JOIN dbo.DmeDoctors   d  ON d.DoctorId   = o.DoctorId;
GO

CREATE OR ALTER VIEW dbo.vDmeRentals AS
SELECT
    r.RentalId, r.TenantId, r.CustomerId, r.OrderId, r.Hcpcs, r.ItemName, r.Serial,
    r.MonthlyRate, r.StartDate, r.NextBillDate, r.CapMonths, r.AbnOnFile, r.Status,
    (SELECT COUNT(*) FROM dbo.DmeClaimLines cl WHERE cl.RentalId = r.RentalId) AS MonthsBilled,
    -- Names come back as two SEPARATE encrypted columns. Two ciphertexts joined
    -- in SQL are not decryptable by anyone with any key.
    cu.FirstName AS CustomerFirstName,
    cu.LastName  AS CustomerLastName,
    cu.LocationId,
    loc.Name AS LocationName
FROM dbo.DmeRentals r
LEFT JOIN dbo.DmeCustomers cu ON cu.CustomerId = r.CustomerId
LEFT JOIN dbo.Locations    loc ON loc.LocationId = cu.LocationId;
GO

CREATE OR ALTER VIEW dbo.vDmeClaims AS
SELECT
    c.ClaimId, c.TenantId, c.ClaimNumber, c.OrderId, c.CustomerId,
    c.CustomerName, c.PayerName, c.Status, c.ServiceDate, c.CreatedAt,
    cu.LocationId,
    loc.Name AS LocationName,
    ch.ChargeTotal AS Total,
    ch.LineCount,
    a.AllowedTotal, a.PaidTotal, a.PayerPaidTotal, a.CustomerPaidTotal,
    a.ContractualTotal, a.PatientResponsibilityTotal, a.OtherAdjustmentTotal,
    a.DeniedCharge, a.PaymentCount,
    b.InsuranceBalance,
    b.PatientBalance,
    b.InsuranceBalance + b.PatientBalance AS Balance,
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
LEFT JOIN dbo.DmeCustomers cu  ON cu.CustomerId = c.CustomerId
LEFT JOIN dbo.Locations    loc ON loc.LocationId = cu.LocationId
CROSS APPLY (
    SELECT ChargeTotal = ISNULL(SUM(cl.Charge),0), LineCount = COUNT(*)
    FROM dbo.DmeClaimLines cl WHERE cl.ClaimId = c.ClaimId
) ch
CROSS APPLY (
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

/* Payments inherit the branch of the claim they were posted against, which
   inherits it from the customer. Three joins, zero stored copies. */
CREATE OR ALTER VIEW dbo.vDmePayments AS
SELECT
    p.PaymentId, p.TenantId, p.PaymentNumber, p.Source, p.PayerName, p.CustomerId,
    p.PostedDate, p.Method, p.ReferenceNumber,
    p.Amount,
    p.Note, p.CreatedAt, p.CreatedBy, p.VoidedAt, p.VoidedBy, p.VoidReason,
    CAST(CASE WHEN p.VoidedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS IsVoided,
    (SELECT ISNULL(SUM(pl.PaidAmount),0) FROM dbo.DmePaymentLines pl
      WHERE pl.PaymentId = p.PaymentId) AS AppliedAmount,
    p.Amount - (SELECT ISNULL(SUM(pl.PaidAmount),0) FROM dbo.DmePaymentLines pl
      WHERE pl.PaymentId = p.PaymentId) AS UnappliedAmount,
    (SELECT COUNT(*) FROM dbo.DmePaymentLines pl WHERE pl.PaymentId = p.PaymentId) AS LineCount,
    (SELECT TOP 1 c.CustomerName
       FROM dbo.DmePaymentLines pl
       JOIN dbo.DmeClaimLines cl ON cl.ClaimLineId = pl.ClaimLineId
       JOIN dbo.DmeClaims     c  ON c.ClaimId = cl.ClaimId
      WHERE pl.PaymentId = p.PaymentId) AS CustomerName,
    (SELECT TOP 1 cu.LocationId
       FROM dbo.DmePaymentLines pl
       JOIN dbo.DmeClaimLines cl ON cl.ClaimLineId = pl.ClaimLineId
       JOIN dbo.DmeClaims     c  ON c.ClaimId = cl.ClaimId
       JOIN dbo.DmeCustomers  cu ON cu.CustomerId = c.CustomerId
      WHERE pl.PaymentId = p.PaymentId) AS LocationId
FROM dbo.DmePayments p;
GO

CREATE OR ALTER VIEW dbo.vDmePaymentLines AS
SELECT
    pl.PaymentLineId, pl.TenantId, pl.PaymentId, pl.ClaimLineId,
    pl.AllowedAmount, pl.PaidAmount,
    p.PaymentNumber, p.Source, p.Method, p.ReferenceNumber, p.PayerName,
    p.PostedDate,
    CAST(CASE WHEN p.VoidedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS IsVoided,
    cl.ClaimId, cl.Hcpcs, cl.ItemName, cl.Modifier, cl.Units, cl.Charge, cl.RentalId,
    c.ClaimNumber, c.CustomerId,
    cu.LocationId,
    (SELECT ISNULL(SUM(a.Amount),0) FROM dbo.DmePaymentLineAdjustments a
      WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')) AS ContractualAdjustment,
    (SELECT ISNULL(SUM(a.Amount),0) FROM dbo.DmePaymentLineAdjustments a
      WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode = 'PR')          AS PatientResponsibility,
    (SELECT ISNULL(SUM(a.Amount),0) FROM dbo.DmePaymentLineAdjustments a
      WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode = 'OA')          AS OtherAdjustment,
    CAST(CASE WHEN pl.PaidAmount = 0 AND EXISTS (
            SELECT 1 FROM dbo.DmePaymentLineAdjustments a
            WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI'))
         THEN 1 ELSE 0 END AS BIT) AS IsDenied,
    CASE WHEN pl.PaidAmount = 0 THEN (
        SELECT TOP 1 a.GroupCode FROM dbo.DmePaymentLineAdjustments a
        WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')
        ORDER BY a.Amount DESC, a.AdjustmentId)
    END AS DenialGroup,
    CASE WHEN pl.PaidAmount = 0 THEN (
        SELECT TOP 1 a.ReasonCode FROM dbo.DmePaymentLineAdjustments a
        WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')
        ORDER BY a.Amount DESC, a.AdjustmentId)
    END AS DenialReasonCode,
    CASE WHEN pl.PaidAmount = 0 THEN (
        SELECT TOP 1 a.GroupCode + '-' + a.ReasonCode FROM dbo.DmePaymentLineAdjustments a
        WHERE a.PaymentLineId = pl.PaymentLineId AND a.GroupCode IN ('CO','PI')
        ORDER BY a.Amount DESC, a.AdjustmentId)
    END AS DenialCode
FROM dbo.DmePaymentLines pl
JOIN dbo.DmePayments   p  ON p.PaymentId   = pl.PaymentId
JOIN dbo.DmeClaimLines cl ON cl.ClaimLineId = pl.ClaimLineId
JOIN dbo.DmeClaims     c  ON c.ClaimId      = cl.ClaimId
LEFT JOIN dbo.DmeCustomers cu ON cu.CustomerId = c.CustomerId;
GO

/* Stock per branch.
   vHcpcsCatalog.OnHand stays the tenant-wide total, because a view cannot take
   a parameter and the catalog screen wants both numbers: what the company holds
   and what this branch holds. */
CREATE OR ALTER VIEW dbo.vDmeStockByLocation AS
SELECT
    m.TenantId,
    m.LocationId,
    l.Name AS LocationName,
    m.Hcpcs,
    OnHand = SUM(m.Qty)
FROM dbo.DmeStockMovements m
LEFT JOIN dbo.Locations l ON l.LocationId = m.LocationId
GROUP BY m.TenantId, m.LocationId, l.Name, m.Hcpcs;
GO

/* ---------------------------------------------------------------------------
   5. Demo data: spread the seed customers across the branches

   One branch holding everything demonstrates nothing. Only touches the seeded
   demo tenant, and only when every customer is still sitting on one location.
   --------------------------------------------------------------------------- */
IF (SELECT COUNT(DISTINCT LocationId) FROM dbo.DmeCustomers WHERE TenantId = 1) = 1
   AND (SELECT COUNT(*) FROM dbo.Locations WHERE TenantId = 1) > 1
   AND EXISTS (SELECT 1 FROM dbo.DmeCustomers WHERE TenantId = 1 AND AccountNo = 'LMS-1001')
BEGIN
    DECLARE @main INT = (SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId = 1 ORDER BY CASE WHEN IsPrimary = 1 THEN 0 ELSE 1 END, LocationId);
    DECLARE @second INT = (SELECT TOP 1 LocationId FROM dbo.Locations WHERE TenantId = 1 AND LocationId <> @main ORDER BY LocationId);

    UPDATE dbo.DmeCustomers SET LocationId = @second WHERE TenantId = 1 AND AccountNo IN ('LMS-1003', 'LMS-1005');
    PRINT 'Spread demo customers across two branches';
END
GO

/* ---------------------------------------------------------------------------
   Verification
   --------------------------------------------------------------------------- */
SELECT CONCAT('primaries per tenant, max: ',
       (SELECT MAX(n) FROM (SELECT COUNT(*) AS n FROM dbo.Locations WHERE IsPrimary = 1 GROUP BY TenantId) x));

SELECT l.Name AS Location, COUNT(c.CustomerId) AS Customers
FROM dbo.Locations l
LEFT JOIN dbo.DmeCustomers c ON c.LocationId = l.LocationId
WHERE l.TenantId = 1
GROUP BY l.Name ORDER BY l.Name;

SELECT TOP 5 LocationName, Hcpcs, OnHand FROM dbo.vDmeStockByLocation ORDER BY Hcpcs;

SELECT ClaimNumber, LocationName, PaymentStatus, Total FROM dbo.vDmeClaims ORDER BY ClaimId;
GO
