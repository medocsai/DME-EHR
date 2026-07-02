-- Add 4 eligibility columns to Insurances table
-- Run on SSMS against the production database

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Insurances') AND name = 'PlanName')
BEGIN
    ALTER TABLE [Insurances] ADD [PlanName] nvarchar(max) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Insurances') AND name = 'InNetwork')
BEGIN
    ALTER TABLE [Insurances] ADD [InNetwork] bit NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Insurances') AND name = 'OutOfPocketMax')
BEGIN
    ALTER TABLE [Insurances] ADD [OutOfPocketMax] decimal(18,2) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Insurances') AND name = 'LastEligibilityResponseJson')
BEGIN
    ALTER TABLE [Insurances] ADD [LastEligibilityResponseJson] nvarchar(max) NULL;
END
GO
