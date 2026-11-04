SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.CityMarketplaceMedia',N'U') IS NULL
    THROW 76100,N'V76 city marketplace media table is missing.',1;
IF COL_LENGTH(N'commerce.CityMarketplaceMedia',N'ResourceKey') IS NULL
    THROW 76101,N'V76 city media resource key is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'UX_CityMarketplaceMedia_CityResourceKey' AND is_unique=1)
    THROW 76102,N'V76 city/resource-key uniqueness is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'UQ_CityMarketplaceMedia_CityResource' AND is_unique=1)
    THROW 76105,N'V75-compatible city logo/banner uniqueness is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.CityMarketplaceMedia') AND name=N'CK_CityMarketplaceMedia_Resource' AND is_disabled=0 AND is_not_trusted=0)
    THROW 76103,N'V76 city media resource validation is missing or untrusted.',1;
IF EXISTS(SELECT 1 FROM commerce.CityMarketplaceMedia WHERE (ResourceType IN('LOGO','BANNER') AND ResourceKey IS NOT NULL) OR (ResourceType='CATEGORY' AND (ResourceKey IS NULL OR LEN(ResourceKey) NOT BETWEEN 1 AND 160)))
    THROW 76104,N'V76 city media resource keys are invalid.',1;
PRINT N'V76 city category display media validator passed.';
