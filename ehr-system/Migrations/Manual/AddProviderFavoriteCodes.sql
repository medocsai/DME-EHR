-- =====================================================================
-- ProviderFavoriteCodes — per-user (per-provider) favorite billing codes
-- =====================================================================
-- Stores ICD-10 and CPT codes that a clinician/provider has starred from
-- the Dx & CPT step. Strict per-user isolation via UserId — no clinic-wide
-- visibility, no cross-provider read.
--
-- The Description column stores the description AT THE TIME OF FAVORITING,
-- so future edits to the central Icdcodes/Cptcodes table do not silently
-- change what providers see in their favorites list.
--
-- Units are NOT stored — when a provider clicks "Add" on a CPT favorite,
-- the code is added to Selected Codes with units=1 and they set the real
-- units inline on the visit row (same as a manual add).
-- =====================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProviderFavoriteCodes')
BEGIN
    CREATE TABLE ProviderFavoriteCodes
    (
        Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProviderFavoriteCodes PRIMARY KEY,
        UserId      INT              NOT NULL,
        CodeType    NVARCHAR(10)     NOT NULL,    -- 'ICD10' or 'CPT'
        Code        NVARCHAR(20)     NOT NULL,
        Description NVARCHAR(500)    NOT NULL,
        CreatedAt   DATETIME2        NOT NULL CONSTRAINT DF_ProviderFavoriteCodes_CreatedAt DEFAULT (GETUTCDATE()),

        CONSTRAINT FK_ProviderFavoriteCodes_UserId
            FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,

        CONSTRAINT CK_ProviderFavoriteCodes_CodeType
            CHECK (CodeType IN ('ICD10','CPT'))
    );

    -- Unique per (user, codeType, code) — prevents duplicate favorites
    CREATE UNIQUE INDEX UX_ProviderFavoriteCodes_User_Type_Code
        ON ProviderFavoriteCodes (UserId, CodeType, Code);

    -- Fast lookup of all favorites for a user + type (typical query)
    CREATE INDEX IX_ProviderFavoriteCodes_User_Type
        ON ProviderFavoriteCodes (UserId, CodeType)
        INCLUDE (Code, Description, CreatedAt);

    PRINT 'ProviderFavoriteCodes table created.';
END
ELSE
BEGIN
    PRINT 'ProviderFavoriteCodes table already exists. Skipping.';
END
