SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
GO

/* ===========================================================================
   Remove the eligibility status that was never checked.

   dbo.DmeCustomerInsurances.EligStatus was written as the literal 'active' on
   every insert and read by nothing, anywhere. Every row in the database holds
   the same constant.

   That is worse than an unused column. It is a fact-shaped field asserting that
   this customer's coverage is active, sitting next to real member IDs and real
   deductibles, in a table a future 270/271 eligibility feature would obviously
   reach for. The first person to write that feature would find the column
   already there, already populated, and already agreeing with them.

   Nothing in this product has ever contacted a payer to ask. Live eligibility
   is not connected, so the honest state is ABSENT, not 'active'.

   The two screens that displayed the same claim are cleaned in the same commit:
   an unconditional green "Eligible" chip on Customer.cshtml, and a Validate
   Insurance button on NewCustomer.cshtml that waited 1.1 seconds and printed
   "Active - DME covered - Deductible met - 20% coinsurance" from a string
   literal.

   Copay, Coinsurance and Deductible STAY. Those are entered by staff from the
   payer's portal or the member's card, so they are real recorded facts with a
   known source. Only the assertion nobody made is going.

   Safe to re-run. Verified: no view, no index, no constraint and no C# reads
   the column; the only reference was the INSERT that wrote the literal.
   =========================================================================== */

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DmeCustomerInsurances')
             AND name = 'EligStatus')
BEGIN
    -- No default constraint exists on this column (checked), but drop any that
    -- appears later rather than failing on it.
    DECLARE @df SYSNAME = (
        SELECT dc.name FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id
                          AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID('dbo.DmeCustomerInsurances')
          AND c.name = 'EligStatus');

    -- Built as a string, NOT passed as a parameter. An object name in DDL
    -- cannot be parameterised: ALTER TABLE ... DROP CONSTRAINT @c is a syntax
    -- error. This column happens to carry no default, so the branch never ran
    -- and the mistake was invisible here; it was found when the same shape was
    -- used on a column that did have one.
    IF @df IS NOT NULL
    BEGIN
        DECLARE @drop NVARCHAR(MAX) =
            N'ALTER TABLE dbo.DmeCustomerInsurances DROP CONSTRAINT ' + QUOTENAME(@df);
        EXEC sp_executesql @drop;
    END

    -- sp_executesql because a batch naming a column being dropped is validated
    -- at COMPILE time, so an IF guard around it does not protect it. Same trap
    -- the earlier DME migrations documented.
    EXEC sp_executesql N'ALTER TABLE dbo.DmeCustomerInsurances DROP COLUMN EligStatus;';

    PRINT 'Dropped DmeCustomerInsurances.EligStatus (was the constant ''active'', read by nothing).';
END
ELSE
    PRINT 'DmeCustomerInsurances.EligStatus already gone.';
GO

/* --------------------------------------------------------------- verification */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.DmeCustomerInsurances')
             AND name = 'EligStatus')
    RAISERROR('EligStatus is still present on dbo.DmeCustomerInsurances.', 16, 1);
ELSE
    PRINT 'Verified: dbo.DmeCustomerInsurances no longer claims an eligibility status.';
GO
