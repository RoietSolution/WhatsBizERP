SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.CityMarketplaceMedia',N'U') IS NULL
    THROW 76000,N'V76 requires the V75 city marketplace media table.',1;

BEGIN TRANSACTION;
IF COL_LENGTH(N'commerce.CityMarketplaceMedia',N'ResourceKey') IS NULL
    ALTER TABLE commerce.CityMarketplaceMedia ADD ResourceKey nvarchar(160) NULL;
GO

IF EXISTS(SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'UQ_CityMarketplaceMedia_CityResource')
    ALTER TABLE commerce.CityMarketplaceMedia DROP CONSTRAINT UQ_CityMarketplaceMedia_CityResource;
ELSE IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'UQ_CityMarketplaceMedia_CityResource')
    DROP INDEX UQ_CityMarketplaceMedia_CityResource ON commerce.CityMarketplaceMedia;

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'CK_CityMarketplaceMedia_Resource')
    ALTER TABLE commerce.CityMarketplaceMedia DROP CONSTRAINT CK_CityMarketplaceMedia_Resource;

ALTER TABLE commerce.CityMarketplaceMedia WITH CHECK ADD CONSTRAINT CK_CityMarketplaceMedia_Resource
CHECK ((ResourceType IN('LOGO','BANNER') AND ResourceKey IS NULL)
    OR (ResourceType='CATEGORY' AND ResourceKey IS NOT NULL AND LEN(ResourceKey) BETWEEN 1 AND 160));
ALTER TABLE commerce.CityMarketplaceMedia CHECK CONSTRAINT CK_CityMarketplaceMedia_Resource;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'UX_CityMarketplaceMedia_CityResourceKey')
    CREATE UNIQUE INDEX UX_CityMarketplaceMedia_CityResourceKey
        ON commerce.CityMarketplaceMedia(CityId,ResourceType,ResourceKey);
-- Keep the V75 validator contract intact for its original LOGO/BANNER uniqueness rule.
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'UQ_CityMarketplaceMedia_CityResource')
    CREATE UNIQUE INDEX UQ_CityMarketplaceMedia_CityResource
        ON commerce.CityMarketplaceMedia(CityId,ResourceType) WHERE ResourceType IN('LOGO','BANNER');

COMMIT TRANSACTION;
