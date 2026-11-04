IF OBJECT_ID(N'commerce.CityMarketplaceSearchEvents',N'U') IS NULL
    THROW 78001,N'V78 validation failed: City Marketplace search event table is missing.',1;
IF COL_LENGTH(N'commerce.CityMarketplaceSearchEvents',N'SearchEventId') IS NULL
 OR COL_LENGTH(N'commerce.CityMarketplaceSearchEvents',N'CityId') IS NULL
 OR COL_LENGTH(N'commerce.CityMarketplaceSearchEvents',N'SearchContext') IS NULL
 OR COL_LENGTH(N'commerce.CityMarketplaceSearchEvents',N'NormalizedKeyword') IS NULL
 OR COL_LENGTH(N'commerce.CityMarketplaceSearchEvents',N'ResultCount') IS NULL
 OR COL_LENGTH(N'commerce.CityMarketplaceSearchEvents',N'CreatedAtUtc') IS NULL
    THROW 78002,N'V78 validation failed: required analytics columns are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.CityMarketplaceSearchEvents') AND name=N'FK_CitySearch_City')
    THROW 78003,N'V78 validation failed: city foreign key is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceSearchEvents') AND name=N'IX_CitySearch_CityDate')
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceSearchEvents') AND name=N'IX_CitySearch_CityKeywordDate')
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'IX_ProductSearchEvents_OwnerDate')
    THROW 78004,N'V78 validation failed: analytics indexes are missing.',1;
IF EXISTS(SELECT 1 FROM commerce.CityMarketplaceSearchEvents WHERE SearchContext<>'CITY' OR ResultCount<0 OR LEN(NormalizedKeyword)=0 OR LEN(NormalizedKeyword)>200)
    THROW 78005,N'V78 validation failed: invalid City Marketplace search event data exists.',1;
