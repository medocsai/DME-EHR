/* ============================================================================
   DME Core Schema + Seed  (DMEEHR database)
   Clean DME-native model on the converted app shell.

   RUN ORDER (a fresh database needs all four, in this order):
     1. 2026-06-19_DME_Core_Schema.sql            <- this file
     2. 2026-08-25_DME_Tenant_Isolation.sql
     3. 2026-08-25_DME_Single_Source_Of_Truth.sql
     4. 2026-08-25_DME_Customer_PHI_Encryption.sql
   Then POST /Dme/BackfillPhi once, as an admin, to encrypt the seeded customer
   rows. Verified end to end on a scratch database on 2026-08-25.

   IDEMPOTENCY, precisely:
   Safe to re-run on its own. NOT safe to re-run after step 3, which drops
   DmeRentals.MonthsBilled, DmeOrders.CustomerName/DoctorName, DmeClaims.Total
   and HcpcsCodes.OnHand. The seed block below still names those columns, and
   SQL Server binds column names when it COMPILES a batch, so the seed fails to
   compile even though its IF guard would have skipped it. No data is harmed
   (the guard means nothing is inserted) but the script reports errors.

   Migrations are an ordered chain, so re-running an earlier one after a later
   one is not a normal operation. Stated here because the file used to claim
   plain "idempotent", which was not true once step 3 existed.

   Seed data is plaintext at this point; step 4 plus the backfill is what
   encrypts it.
   ============================================================================ */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE [DMEEHR];
GO

/* ---------- Customers (DME customer record — replaces clinical Patient) ---------- */
IF OBJECT_ID('dbo.DmeCustomers','U') IS NULL
CREATE TABLE dbo.DmeCustomers (
    CustomerId    INT IDENTITY(1,1) PRIMARY KEY,
    AccountNo     NVARCHAR(20)  NOT NULL,
    FirstName     NVARCHAR(80)  NOT NULL,
    LastName      NVARCHAR(80)  NOT NULL,
    Dob           DATE          NULL,
    Gender        NVARCHAR(10)  NULL,
    SsnLast4      NVARCHAR(4)   NULL,
    HeightInches  INT           NULL,
    WeightLbs     INT           NULL,
    Phone         NVARCHAR(30)  NULL,
    Email         NVARCHAR(120) NULL,
    AddressLine1  NVARCHAR(120) NULL,
    City          NVARCHAR(60)  NULL,
    State         NVARCHAR(4)   NULL,
    Zip           NVARCHAR(12)  NULL,
    EmergencyName NVARCHAR(80)  NULL,
    EmergencyRel  NVARCHAR(40)  NULL,
    EmergencyPhone NVARCHAR(30) NULL,
    Status        NVARCHAR(20)  NOT NULL CONSTRAINT DF_DmeCust_Status DEFAULT 'active',
    TenantId      INT           NOT NULL CONSTRAINT DF_DmeCust_Tenant DEFAULT 1,
    CreatedAt     DATETIME2     NOT NULL CONSTRAINT DF_DmeCust_Created DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('dbo.DmeCustomerInsurances','U') IS NULL
CREATE TABLE dbo.DmeCustomerInsurances (
    InsuranceId   INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId    INT NOT NULL,
    Kind          NVARCHAR(12) NOT NULL,           -- primary | secondary
    PayerName     NVARCHAR(120) NULL,
    PayerId       NVARCHAR(20)  NULL,
    MemberId      NVARCHAR(40)  NULL,
    GroupNumber   NVARCHAR(40)  NULL,
    Copay         DECIMAL(10,2) NOT NULL DEFAULT 0,
    Coinsurance   INT           NOT NULL DEFAULT 0,
    Deductible    DECIMAL(10,2) NOT NULL DEFAULT 0,
    SubscriberRel NVARCHAR(20)  NULL,
    EligStatus    NVARCHAR(20)  NULL
);
GO

IF OBJECT_ID('dbo.DmeCustomerDiagnoses','U') IS NULL
CREATE TABLE dbo.DmeCustomerDiagnoses (
    DiagnosisId INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId  INT NOT NULL,
    IcdCode     NVARCHAR(12) NOT NULL,
    Description NVARCHAR(200) NULL,
    IsPrimary   BIT NOT NULL DEFAULT 0
);
GO

/* ---------- Reference: Doctors (referring physicians) + Payers ---------- */
IF OBJECT_ID('dbo.DmeDoctors','U') IS NULL
CREATE TABLE dbo.DmeDoctors (
    DoctorId  INT IDENTITY(1,1) PRIMARY KEY,
    FirstName NVARCHAR(80) NOT NULL,
    LastName  NVARCHAR(80) NOT NULL,
    Npi       NVARCHAR(15) NULL,
    Specialty NVARCHAR(60) NULL,
    Phone     NVARCHAR(30) NULL
);
GO

IF OBJECT_ID('dbo.DmePayers','U') IS NULL
CREATE TABLE dbo.DmePayers (
    PayerId   INT IDENTITY(1,1) PRIMARY KEY,
    Name      NVARCHAR(120) NOT NULL,
    PayerCode NVARCHAR(20)  NULL,
    PayerType NVARCHAR(30)  NULL
);
GO

/* ---------- Catalog: HCPCS item master ----------
   ADDED 2026-08-25. This table existed in the DMEEHR database but its CREATE
   was in no migration in the repository, so a fresh checkout could not rebuild
   the database: every DME screen that reads the catalog failed. Scripted from
   the live schema and seeded with the same 22 items.

   The catalog is TENANT data, not a shared national reference table: each DME
   supplier maintains its own item master and its own contract pricing.

   OnHand is present here because it is what the table originally had. The
   later 2026-08-25_DME_Single_Source_Of_Truth.sql migration converts it into
   an opening balance in DmeStockMovements and DROPS it, because a stored stock
   counter drifts (it said 14 units of E1390 when one was in stock). Keeping the
   column here means the migration chain reproduces the real history rather than
   pretending the mistake never happened, and a fresh build ends up in exactly
   the same state as the existing database. Do not read OnHand from this table:
   read it from vHcpcsCatalog. */
IF OBJECT_ID('dbo.HcpcsCodes','U') IS NULL
CREATE TABLE dbo.HcpcsCodes (
    HcpcsCodeId        INT IDENTITY(1,1) PRIMARY KEY,
    Hcpcs              NVARCHAR(10)  NOT NULL,
    Name               NVARCHAR(200) NOT NULL,
    Category           NVARCHAR(60)  NOT NULL,
    IsSerialized       BIT           NOT NULL CONSTRAINT DF_Hcpcs_Serial   DEFAULT 0,
    Rentable           BIT           NOT NULL CONSTRAINT DF_Hcpcs_Rentable DEFAULT 0,
    Purchasable        BIT           NOT NULL CONSTRAINT DF_Hcpcs_Purch    DEFAULT 1,
    PurchasePrice      DECIMAL(10,2) NOT NULL CONSTRAINT DF_Hcpcs_Price    DEFAULT 0,
    MonthlyRate        DECIMAL(10,2) NOT NULL CONSTRAINT DF_Hcpcs_Rate     DEFAULT 0,
    CappedRentalMonths INT           NOT NULL CONSTRAINT DF_Hcpcs_Cap      DEFAULT 0,
    Modifiers          NVARCHAR(50)  NULL,
    ReorderPoint       INT           NOT NULL CONSTRAINT DF_Hcpcs_Reorder  DEFAULT 5,
    OnHand             INT           NOT NULL CONSTRAINT DF_Hcpcs_OnHand   DEFAULT 0,   -- dropped by the 2026-08-25 SSOT migration
    TenantId           INT           NOT NULL CONSTRAINT DF_Hcpcs_Tenant   DEFAULT 1
);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.HcpcsCodes)
BEGIN
    INSERT INTO dbo.HcpcsCodes (Hcpcs,Name,Category,IsSerialized,Rentable,Purchasable,PurchasePrice,MonthlyRate,CappedRentalMonths,Modifiers,ReorderPoint,OnHand) VALUES
    ('E0250','Hospital Bed, Full-Electric','Beds & Support',1,1,0,0.00,165.00,13,'RR',5,4),
    ('E0260','Hospital Bed, Semi-Electric','Beds & Support',1,1,0,0.00,135.00,13,'RR',5,7),
    ('E0277','Powered Pressure-Reducing Mattress','Beds & Support',1,1,0,0.00,168.00,13,'RR',5,5),
    ('A4253','Blood Glucose Test Strips (50 ct)','Diabetic',0,0,1,26.50,0.00,0,'NU',5,120),
    ('A5500','Diabetic Custom Shoe (per shoe)','Diabetic',0,0,1,64.00,0.00,0,'KX',5,24),
    ('E0607','Blood Glucose Monitor, Home','Diabetic',0,0,1,38.00,0.00,0,'NU',5,30),
    ('E0114','Crutches, Underarm, Aluminum (pair)','Mobility',0,0,1,28.00,0.00,0,'NU',5,35),
    ('E0143','Walker, Folding, Wheeled','Mobility',0,0,1,58.00,0.00,0,'NU',5,40),
    ('E0163','Commode Chair, Stationary','Mobility',0,0,1,92.00,0.00,0,'NU',5,16),
    ('K0001','Standard Wheelchair','Mobility',1,1,1,420.00,45.00,13,'NU,RR',5,18),
    ('K0005','Ultralightweight Wheelchair','Mobility',1,0,1,2380.00,0.00,0,'NU',5,3),
    ('K0823','Power Wheelchair, Group 2 Std','Mobility',1,1,0,0.00,489.00,13,'RR,KX',5,4),
    ('E0730','TENS Unit, 4-Lead','Orthotics',0,1,1,148.00,34.00,0,'NU,RR',5,12),
    ('L0650','Lumbar-Sacral Orthosis (LSO)','Orthotics',0,0,1,312.00,0.00,0,'NU',5,10),
    ('A7030','CPAP Full Face Mask','Respiratory',0,0,1,96.50,0.00,0,'NU',5,60),
    ('A7034','Nasal CPAP Mask','Respiratory',0,0,1,78.00,0.00,0,'NU',5,75),
    ('E0431','Portable Gaseous Oxygen System','Respiratory',1,1,0,0.00,32.00,36,'RR',5,9),
    ('E0470','Respiratory Assist Device (BiPAP)','Respiratory',1,1,0,0.00,210.00,13,'RR',5,6),
    ('E0570','Nebulizer, with compressor','Respiratory',0,1,1,64.00,18.00,0,'NU,RR',5,22),
    ('E0601','CPAP Device','Respiratory',1,1,1,685.00,92.00,13,'RR,NU',5,11),
    ('E1390','Oxygen Concentrator, single delivery','Respiratory',1,1,0,0.00,178.00,36,'RR',5,14),
    ('A6402','Sterile Gauze Pad (box)','Wound Care',0,0,1,14.00,0.00,0,'NU',5,90);
END
GO

/* ---------- Inventory: serialized units ---------- */
IF OBJECT_ID('dbo.DmeSerializedUnits','U') IS NULL
CREATE TABLE dbo.DmeSerializedUnits (
    UnitId        INT IDENTITY(1,1) PRIMARY KEY,
    Hcpcs         NVARCHAR(10) NOT NULL,
    ItemName      NVARCHAR(200) NULL,
    SerialNumber  NVARCHAR(40) NOT NULL,
    Status        NVARCHAR(20) NOT NULL DEFAULT 'in-stock',  -- in-stock | rented | sold | maintenance | recalled
    CustomerId    INT NULL,
    InServiceDate DATE NULL,
    LotNumber     NVARCHAR(40) NULL
);
GO

/* ---------- CMN / RX ---------- */
IF OBJECT_ID('dbo.DmeCmns','U') IS NULL
CREATE TABLE dbo.DmeCmns (
    CmnId       INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId  INT NOT NULL,
    DoctorId    INT NULL,
    Hcpcs       NVARCHAR(10) NULL,
    InitialDate DATE NULL,
    RecertDate  DATE NULL,
    Status      NVARCHAR(20) NOT NULL DEFAULT 'on-file'
);
GO

/* ---------- Orders + Lines (delivery tickets) ---------- */
IF OBJECT_ID('dbo.DmeOrders','U') IS NULL
CREATE TABLE dbo.DmeOrders (
    OrderId      INT IDENTITY(1,1) PRIMARY KEY,
    OrderNumber  NVARCHAR(20) NOT NULL,
    CustomerId   INT NOT NULL,
    CustomerName NVARCHAR(160) NULL,
    Status       NVARCHAR(20) NOT NULL DEFAULT 'draft',   -- draft | confirmed | delivered | billed
    Stage        NVARCHAR(30) NULL,
    DoctorId     INT NULL,
    DoctorName   NVARCHAR(160) NULL,
    DeliveryDate DATE NULL,
    DeliveryAddress NVARCHAR(240) NULL,
    Deposit      DECIMAL(10,2) NOT NULL DEFAULT 0,
    PodSignedBy  NVARCHAR(160) NULL,
    PodSignedAt  DATETIME2 NULL,
    PodSignature NVARCHAR(MAX) NULL,
    TenantId     INT NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_DmeOrders_Created DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('dbo.DmeOrderLines','U') IS NULL
CREATE TABLE dbo.DmeOrderLines (
    LineId      INT IDENTITY(1,1) PRIMARY KEY,
    OrderId     INT NOT NULL,
    Hcpcs       NVARCHAR(10) NOT NULL,
    ItemName    NVARCHAR(200) NULL,
    Category    NVARCHAR(60) NULL,
    Mode        NVARCHAR(12) NOT NULL DEFAULT 'purchase', -- purchase | rental
    Qty         INT NOT NULL DEFAULT 1,
    UnitPrice   DECIMAL(10,2) NOT NULL DEFAULT 0,
    MonthlyRate DECIMAL(10,2) NOT NULL DEFAULT 0,
    Modifiers   NVARCHAR(50) NULL,
    CmnOnFile   BIT NOT NULL DEFAULT 0,
    SerialNumber NVARCHAR(40) NULL,
    IsSerialized BIT NOT NULL DEFAULT 0
);
GO

/* ---------- Rentals (recurring next-bill-date cycle) ---------- */
IF OBJECT_ID('dbo.DmeRentals','U') IS NULL
CREATE TABLE dbo.DmeRentals (
    RentalId     INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId   INT NOT NULL,
    CustomerName NVARCHAR(160) NULL,
    OrderId      INT NULL,
    Hcpcs        NVARCHAR(10) NULL,
    ItemName     NVARCHAR(200) NULL,
    Serial       NVARCHAR(40) NULL,
    MonthlyRate  DECIMAL(10,2) NOT NULL DEFAULT 0,
    StartDate    DATE NULL,
    NextBillDate DATE NULL,
    MonthsBilled INT NOT NULL DEFAULT 0,
    CapMonths    INT NULL,
    AbnOnFile    BIT NOT NULL DEFAULT 0,
    Status       NVARCHAR(20) NOT NULL DEFAULT 'active',  -- active | ended
    TenantId     INT NOT NULL DEFAULT 1
);
GO

/* ---------- Claims (DME billing) + Lines ---------- */
IF OBJECT_ID('dbo.DmeClaims','U') IS NULL
CREATE TABLE dbo.DmeClaims (
    ClaimId      INT IDENTITY(1,1) PRIMARY KEY,
    ClaimNumber  NVARCHAR(20) NOT NULL,
    OrderId      INT NULL,
    CustomerId   INT NOT NULL,
    CustomerName NVARCHAR(160) NULL,
    PayerName    NVARCHAR(120) NULL,
    Status       NVARCHAR(20) NOT NULL DEFAULT 'ready',   -- ready | submitted | paid | denied
    ServiceDate  DATE NULL,
    Total        DECIMAL(10,2) NOT NULL DEFAULT 0,
    TenantId     INT NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_DmeClaims_Created DEFAULT SYSUTCDATETIME()
);
GO

IF OBJECT_ID('dbo.DmeClaimLines','U') IS NULL
CREATE TABLE dbo.DmeClaimLines (
    ClaimLineId INT IDENTITY(1,1) PRIMARY KEY,
    ClaimId     INT NOT NULL,
    Hcpcs       NVARCHAR(10) NOT NULL,
    ItemName    NVARCHAR(200) NULL,
    Modifier    NVARCHAR(20) NULL,
    Units       INT NOT NULL DEFAULT 1,
    Charge      DECIMAL(10,2) NOT NULL DEFAULT 0
);
GO

/* ---------- Sequences (human-friendly numbers) ---------- */
IF OBJECT_ID('dbo.DmeSeq','U') IS NULL
CREATE TABLE dbo.DmeSeq ( Name NVARCHAR(20) PRIMARY KEY, Val INT NOT NULL );
GO
MERGE dbo.DmeSeq AS t USING (VALUES ('LMS',1005),('ORD',1010),('CLM',2005)) AS s(Name,Val)
ON t.Name = s.Name WHEN NOT MATCHED THEN INSERT(Name,Val) VALUES(s.Name,s.Val);
GO

/* ============================================================================
   SEED  (only if Customers empty)
   ============================================================================ */
IF NOT EXISTS (SELECT 1 FROM dbo.DmeCustomers)
BEGIN
    DECLARE @d0 DATE = CAST(GETDATE() AS DATE);

    INSERT INTO dbo.DmeDoctors (FirstName,LastName,Npi,Specialty,Phone) VALUES
    ('James','Harrington','1457382910','Internal Medicine','(214) 555-0142'),
    ('Aisha','Patel','1093847562','Pulmonology','(214) 555-0177'),
    ('Robert','Cole','1820394756','Family Medicine','(972) 555-0190');

    INSERT INTO dbo.DmePayers (Name,PayerCode,PayerType) VALUES
    ('Medicare (Railroad/Part B)','00123','Medicare'),
    ('BCBS of Texas','84980','Commercial'),
    ('Aetna','60054','Commercial'),
    ('Texas Medicaid','MCDTX','Medicaid');

    INSERT INTO dbo.DmeCustomers (AccountNo,FirstName,LastName,Dob,Gender,SsnLast4,HeightInches,WeightLbs,Phone,Email,AddressLine1,City,State,Zip,EmergencyName,EmergencyRel,EmergencyPhone,Status) VALUES
    ('LMS-1001','John','Doe','1958-04-12','Male','4821',70,198,'(214) 555-0101','john.doe@example.com','482 Cedar Springs Rd','Dallas','TX','75219','Mary Doe','Spouse','(214) 555-0102','active'),
    ('LMS-1002','Margaret','Ellis','1944-09-03','Female','7723',64,151,'(214) 555-0144','margaret.ellis@example.com','17 Lakeview Terrace','Dallas','TX','75214','Tom Ellis','Son','(214) 555-0145','active'),
    ('LMS-1003','Robert','Nguyen','1962-12-21','Male','3310',68,224,'(972) 555-0188','robert.nguyen@example.com','903 Maple Ave','Richardson','TX','75081','Linh Nguyen','Spouse','(972) 555-0189','active'),
    ('LMS-1004','Dolores','Fairbanks','1939-06-30','Female','9056',62,138,'(214) 555-0166','','55 Bishop Arts Ct','Dallas','TX','75208','Carol Fairbanks','Daughter','(214) 555-0167','active'),
    ('LMS-1005','Frank','Marsh','1951-02-17','Male','6644',71,240,'(469) 555-0133','frank.marsh@example.com','700 Greenville Ave','Dallas','TX','75206','Susan Marsh','Spouse','(469) 555-0134','active');

    DECLARE @john INT=(SELECT CustomerId FROM dbo.DmeCustomers WHERE AccountNo='LMS-1001');
    DECLARE @marg INT=(SELECT CustomerId FROM dbo.DmeCustomers WHERE AccountNo='LMS-1002');
    DECLARE @rob  INT=(SELECT CustomerId FROM dbo.DmeCustomers WHERE AccountNo='LMS-1003');
    DECLARE @dol  INT=(SELECT CustomerId FROM dbo.DmeCustomers WHERE AccountNo='LMS-1004');
    DECLARE @frank INT=(SELECT CustomerId FROM dbo.DmeCustomers WHERE AccountNo='LMS-1005');

    INSERT INTO dbo.DmeCustomerInsurances (CustomerId,Kind,PayerName,PayerId,MemberId,GroupNumber,Copay,Coinsurance,Deductible,SubscriberRel,EligStatus) VALUES
    (@john,'primary','Medicare (Railroad/Part B)','00123','7G42-RR-118','',0,20,240,'Self','active'),
    (@marg,'primary','Medicare (Railroad/Part B)','00123','4M81-RR-552','',0,20,0,'Self','active'),
    (@marg,'secondary','BCBS of Texas','84980','BCX9920113','TXGRP44',0,0,0,'Self','active'),
    (@rob,'primary','Aetna','60054','AET5540982','AETTX21',30,10,500,'Self','active'),
    (@dol,'primary','Texas Medicaid','MCDTX','TXM0049217','',0,0,0,'Self','active'),
    (@frank,'primary','Medicare (Railroad/Part B)','00123','9C20-RR-771','',0,20,0,'Self','active');

    INSERT INTO dbo.DmeCustomerDiagnoses (CustomerId,IcdCode,Description,IsPrimary) VALUES
    (@marg,'J96.11','Chronic respiratory failure with hypoxia',1),
    (@marg,'J44.9','COPD, unspecified',0),
    (@rob,'E11.9','Type 2 diabetes mellitus without complications',1),
    (@rob,'G47.33','Obstructive sleep apnea',0),
    (@dol,'M62.81','Muscle weakness (generalized)',1),
    (@frank,'I69.354','Hemiplegia following cerebral infarction',1);

    INSERT INTO dbo.DmeSerializedUnits (Hcpcs,ItemName,SerialNumber,Status,CustomerId,InServiceDate) VALUES
    ('E1390','Oxygen Concentrator','OXC-44871','rented',@marg,DATEADD(day,-95,@d0)),
    ('E0260','Hospital Bed, Semi-Electric','BED-10233','rented',@dol,DATEADD(day,-58,@d0)),
    ('K0823','Power Wheelchair, Group 2','PWC-30781','rented',@frank,DATEADD(day,-118,@d0)),
    ('E1390','Oxygen Concentrator','OXC-44902','in-stock',NULL,NULL),
    ('E0601','CPAP Device','CPP-22104','sold',@rob,DATEADD(day,-40,@d0)),
    ('K0001','Standard Wheelchair','WCH-55011','in-stock',NULL,NULL),
    ('E0470','Respiratory Assist Device','BIP-77231','in-stock',NULL,NULL);

    INSERT INTO dbo.DmeCmns (CustomerId,DoctorId,Hcpcs,InitialDate,RecertDate,Status) VALUES
    (@marg,2,'E1390',DATEADD(day,-95,@d0),DATEADD(day,270,@d0),'on-file'),
    (@frank,3,'K0823',DATEADD(day,-118,@d0),DATEADD(day,247,@d0),'on-file');

    /* Orders */
    INSERT INTO dbo.DmeOrders (OrderNumber,CustomerId,CustomerName,Status,Stage,DoctorId,DoctorName,DeliveryDate,Deposit,PodSignedBy,PodSignedAt) VALUES
    ('ORD-01001',@marg,'Margaret Ellis','delivered','delivered',2,'Dr. Aisha Patel',DATEADD(day,-95,@d0),0,'Margaret Ellis',DATEADD(day,-95,@d0)),
    ('ORD-01007',@rob,'Robert Nguyen','confirmed','order-ship',1,'Dr. James Harrington',DATEADD(day,1,@d0),25.00,NULL,NULL),
    ('ORD-01008',@john,'John Doe','draft','receive-order',NULL,NULL,NULL,0,NULL,NULL);

    DECLARE @o1 INT=(SELECT OrderId FROM dbo.DmeOrders WHERE OrderNumber='ORD-01001');
    DECLARE @o7 INT=(SELECT OrderId FROM dbo.DmeOrders WHERE OrderNumber='ORD-01007');
    INSERT INTO dbo.DmeOrderLines (OrderId,Hcpcs,ItemName,Category,Mode,Qty,UnitPrice,MonthlyRate,Modifiers,CmnOnFile,SerialNumber,IsSerialized) VALUES
    (@o1,'E1390','Oxygen Concentrator, single delivery','Respiratory','rental',1,0,178.00,'RR',1,'OXC-44871',1),
    (@o7,'A4253','Blood Glucose Test Strips (50 ct)','Diabetic','purchase',3,26.50,0,'NU',0,NULL,0),
    (@o7,'E0607','Blood Glucose Monitor, Home','Diabetic','purchase',1,38.00,0,'NU',0,NULL,0);

    /* Rentals */
    INSERT INTO dbo.DmeRentals (CustomerId,CustomerName,OrderId,Hcpcs,ItemName,Serial,MonthlyRate,StartDate,NextBillDate,MonthsBilled,CapMonths,AbnOnFile,Status) VALUES
    (@marg,'Margaret Ellis',@o1,'E1390','Oxygen Concentrator','OXC-44871',178.00,DATEADD(day,-95,@d0),DATEADD(day,4,@d0),3,36,0,'active'),
    (@dol,'Dolores Fairbanks',NULL,'E0260','Hospital Bed, Semi-Electric','BED-10233',135.00,DATEADD(day,-58,@d0),DATEADD(day,2,@d0),2,13,0,'active'),
    (@frank,'Frank Marsh',NULL,'K0823','Power Wheelchair, Group 2','PWC-30781',489.00,DATEADD(day,-118,@d0),DATEADD(day,6,@d0),4,13,1,'active');

    /* Claims */
    INSERT INTO dbo.DmeClaims (ClaimNumber,OrderId,CustomerId,CustomerName,PayerName,Status,ServiceDate,Total) VALUES
    ('CLM-02001',@o1,@marg,'Margaret Ellis','Medicare (Railroad/Part B)','submitted',DATEADD(day,-4,@d0),178.00),
    ('CLM-02002',@o1,@marg,'Margaret Ellis','Medicare (Railroad/Part B)','ready',@d0,178.00);
    DECLARE @c1 INT=(SELECT ClaimId FROM dbo.DmeClaims WHERE ClaimNumber='CLM-02001');
    DECLARE @c2 INT=(SELECT ClaimId FROM dbo.DmeClaims WHERE ClaimNumber='CLM-02002');
    INSERT INTO dbo.DmeClaimLines (ClaimId,Hcpcs,ItemName,Modifier,Units,Charge) VALUES
    (@c1,'E1390','Oxygen Concentrator','RR',1,178.00),
    (@c2,'E1390','Oxygen Concentrator','RR',1,178.00);
END
GO

SELECT 'DmeCustomers' AS TableName, COUNT(*) AS Rows FROM dbo.DmeCustomers
UNION ALL SELECT 'DmeOrders', COUNT(*) FROM dbo.DmeOrders
UNION ALL SELECT 'DmeRentals', COUNT(*) FROM dbo.DmeRentals
UNION ALL SELECT 'DmeClaims', COUNT(*) FROM dbo.DmeClaims
UNION ALL SELECT 'DmeSerializedUnits', COUNT(*) FROM dbo.DmeSerializedUnits
UNION ALL SELECT 'HcpcsCodes', COUNT(*) FROM dbo.HcpcsCodes;
GO
