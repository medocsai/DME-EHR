/* ============================================================================
   DME Core Schema + Seed  (DMEEHR database)
   Clean DME-native model on the converted app shell. Plaintext demo data.
   Idempotent: safe to re-run.
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

/* ---------- Inventory: serialized units (HcpcsCodes already holds the catalog) ---------- */
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
