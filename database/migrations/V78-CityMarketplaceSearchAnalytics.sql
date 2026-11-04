/* V78: privacy-minimized City Marketplace search analytics. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'commerce.Cities',N'U') IS NULL OR OBJECT_ID(N'commerce.ProductSearchEvents',N'U') IS NULL
    THROW 78000,N'V78 requires V74 city and V52 retailer search analytics schema.',1;
BEGIN TRANSACTION;

IF OBJECT_ID(N'commerce.CityMarketplaceSearchEvents',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.CityMarketplaceSearchEvents
    (
        SearchEventId uniqueidentifier NOT NULL,
        CityId uniqueidentifier NOT NULL,
        SearchContext varchar(12) NOT NULL CONSTRAINT DF_CitySearch_Context DEFAULT('CITY'),
        NormalizedKeyword nvarchar(200) NOT NULL,
        ResultCount int NOT NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_CitySearch_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_CityMarketplaceSearchEvents PRIMARY KEY(SearchEventId),
        CONSTRAINT FK_CitySearch_City FOREIGN KEY(CityId) REFERENCES commerce.Cities(CityId),
        CONSTRAINT CK_CitySearch_Context CHECK(SearchContext='CITY'),
        CONSTRAINT CK_CitySearch_ResultCount CHECK(ResultCount>=0)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceSearchEvents') AND name=N'IX_CitySearch_CityDate')
    CREATE INDEX IX_CitySearch_CityDate ON commerce.CityMarketplaceSearchEvents(CityId,CreatedAtUtc) INCLUDE(NormalizedKeyword,ResultCount);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityMarketplaceSearchEvents') AND name=N'IX_CitySearch_CityKeywordDate')
    CREATE INDEX IX_CitySearch_CityKeywordDate ON commerce.CityMarketplaceSearchEvents(CityId,NormalizedKeyword,CreatedAtUtc);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'IX_ProductSearchEvents_OwnerDate')
    CREATE INDEX IX_ProductSearchEvents_OwnerDate ON commerce.ProductSearchEvents(CreatedAtUtc) INCLUDE(TenantId,NormalizedKeyword,Outcome);
COMMIT TRANSACTION;
