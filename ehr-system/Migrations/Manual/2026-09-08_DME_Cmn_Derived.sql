SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   The certificate of medical necessity becomes derived, and expiry becomes
   visible.

   WHAT WAS THERE

   Two places held the same fact and neither knew about the other.

   dbo.DmeCmns holds real certificates: which customer, which doctor, which
   HCPCS, the initial date, the RECERT date, and a status. Two rows in this
   database. Nothing in the product reads it. Not one C# file, not one view.

   dbo.DmeOrderLines.CmnOnFile is a BIT that the order screen renders as a green
   "on file" or an amber "missing" chip. Nothing in the product writes it. It
   was set when the demo data was seeded and has never moved since.

   So the screen that tells a biller whether the paperwork exists is reading a
   flag that no code maintains, while the table that actually records the
   certificate is read by nobody. That is the stock problem with the two halves
   in different rooms.

   WHY IT IS WORSE THAN AN ORDINARY DUPLICATE

   A CMN EXPIRES. RecertDate is the whole point of the record: the certificate
   covers a period, and a claim dated after it lapses is denied for want of
   documentation the supplier genuinely had, once.

   A stored boolean cannot expire. It says "on file" on the day it was set and
   it says "on file" for ever, including the month after the certificate ran
   out. The screen is at its most confident exactly when it is wrong.

   WHAT REPLACES IT

   vDmeOrderLines derives three states rather than two, because "missing" and
   "expired" are different problems with different remedies: one needs the
   doctor to sign something, the other needs them to sign it AGAIN.

       missing  - no certificate for this customer and this code
       expired  - there is one, and its recert date has passed
       on-file  - there is one and it is current

   Evaluated against TODAY, not against the order's delivery date, because the
   question the screen answers is operational: can this be billed now. An audit
   asks a different question, about validity at the date of service, and the
   recert date is exposed so that answer is available too.

   The RecertDate itself is exposed, so the screen can say WHEN it lapses rather
   than only that it has.

   dbo.DmeCmns is inside the row level security policy, so the join is scoped
   like every other read.

   Safe to re-run.
   =========================================================================== */

/* ---- the view ------------------------------------------------------------ */
IF OBJECT_ID('dbo.vDmeOrderLines') IS NOT NULL
    DROP VIEW dbo.vDmeOrderLines;
GO

CREATE VIEW dbo.vDmeOrderLines
AS
SELECT  l.LineId,
        l.TenantId,
        l.OrderId,
        l.Hcpcs,
        l.ItemName,
        l.Category,
        l.Mode,
        l.Qty,
        l.UnitPrice,
        l.MonthlyRate,
        l.Modifiers,
        l.SerialNumber,
        l.IsSerialized,
        l.DistributorId,
        l.DistributorRef,

        -- Derived from the certificate itself, never stored. A CMN covers one
        -- customer for one HCPCS code for a period, so the match is on both,
        -- and the period is what decides between expired and current.
        cmn.CmnId          AS CmnId,
        cmn.RecertDate     AS CmnRecertDate,
        CASE
            WHEN cmn.CmnId IS NULL THEN 'missing'
            WHEN cmn.RecertDate IS NOT NULL
             AND cmn.RecertDate < CAST(GETDATE() AS DATE) THEN 'expired'
            ELSE 'on-file'
        END                AS CmnStatus
FROM        dbo.DmeOrderLines l
JOIN        dbo.DmeOrders     o   ON o.OrderId = l.OrderId
OUTER APPLY (
    -- The most recently issued certificate for this customer and code. There is
    -- normally one; ordering makes the choice deterministic if a recertification
    -- was filed as a second row rather than an update.
    SELECT TOP 1 c.CmnId, c.RecertDate
    FROM   dbo.DmeCmns c
    WHERE  c.CustomerId = o.CustomerId
      AND  c.Hcpcs      = l.Hcpcs
      AND  c.Status     = 'on-file'
    ORDER BY c.InitialDate DESC, c.CmnId DESC
) cmn;
GO

/* ---- drop the stored copy ------------------------------------------------ */
/* sp_executesql because a batch naming a column being dropped is validated at
   COMPILE time, so an IF guard around it does not protect it. */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DmeOrderLines') AND name = 'CmnOnFile')
BEGIN
    -- The column carries an auto named DEFAULT constraint, and a column cannot
    -- be dropped while one depends on it. The name is generated
    -- (DF__DmeOrderL__CmnOn__1ADEEA9C here) so it differs per database and has
    -- to be looked up rather than written down.
    DECLARE @df SYSNAME = (
        SELECT dc.name FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id
                          AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID('dbo.DmeOrderLines')
          AND c.name = 'CmnOnFile');

    -- Built as a string, NOT passed as a parameter. An object name in DDL
    -- cannot be parameterised: ALTER TABLE ... DROP CONSTRAINT @c is a syntax
    -- error, and because it is inside sp_executesql it fails at run time with a
    -- message about the constraint rather than about the parameter. QUOTENAME
    -- because the name is read from a catalog view.
    IF @df IS NOT NULL
    BEGIN
        DECLARE @drop NVARCHAR(MAX) =
            N'ALTER TABLE dbo.DmeOrderLines DROP CONSTRAINT ' + QUOTENAME(@df);
        EXEC sp_executesql @drop;
    END

    EXEC sp_executesql N'ALTER TABLE dbo.DmeOrderLines DROP COLUMN CmnOnFile;';
    PRINT 'Dropped DmeOrderLines.CmnOnFile; the certificate is the source now.';
END
ELSE
    PRINT 'DmeOrderLines.CmnOnFile already gone.';
GO

/* --------------------------------------------------------------- verification */
DECLARE @missing NVARCHAR(MAX) = N'';

IF OBJECT_ID('dbo.vDmeOrderLines') IS NULL
    SET @missing = @missing + N'vDmeOrderLines; ';

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DmeOrderLines') AND name = 'CmnOnFile')
    SET @missing = @missing + N'DmeOrderLines.CmnOnFile still stored; ';

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.vDmeOrderLines') AND name = 'CmnStatus')
    SET @missing = @missing + N'vDmeOrderLines.CmnStatus; ';

IF @missing <> N''
    RAISERROR('CMN derivation incomplete: %s', 16, 1, @missing);
ELSE
    PRINT 'Verified: the CMN chip now reads the certificate, and can expire.';
GO
