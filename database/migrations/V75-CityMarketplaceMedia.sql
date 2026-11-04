SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'commerce.Cities',N'U') IS NULL
    THROW 75000,N'V75 requires the V74 marketplace schema.',1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'commerce.CityMarketplaceMedia',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.CityMarketplaceMedia(
        MediaId uniqueidentifier NOT NULL,
        CityId uniqueidentifier NOT NULL,
        ResourceType varchar(12) NOT NULL,
        FileName nvarchar(255) NOT NULL,
        ContentType nvarchar(100) NOT NULL,
        ThumbnailContentType nvarchar(100) NOT NULL,
        StorageProvider nvarchar(20) NOT NULL,
        ObjectKey nvarchar(1024) NULL,
        ThumbnailObjectKey nvarchar(1024) NULL,
        ImageData varbinary(max) NULL,
        ThumbnailData varbinary(max) NULL,
        ContentHash varchar(64) NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_CityMarketplaceMedia_Created DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(256) NOT NULL,
        CONSTRAINT PK_CityMarketplaceMedia PRIMARY KEY(MediaId),
        CONSTRAINT UQ_CityMarketplaceMedia_CityResource UNIQUE(CityId,ResourceType),
        CONSTRAINT FK_CityMarketplaceMedia_City FOREIGN KEY(CityId) REFERENCES commerce.Cities(CityId),
        CONSTRAINT CK_CityMarketplaceMedia_Resource CHECK(ResourceType IN('LOGO','BANNER')),
        CONSTRAINT CK_CityMarketplaceMedia_Storage CHECK(StorageProvider IN('DATABASE','LOCAL','S3')),
        CONSTRAINT CK_CityMarketplaceMedia_Content CHECK(ContentType='image/webp' AND ThumbnailContentType='image/webp' AND LEN(ContentHash)=64)
    );
    CREATE INDEX IX_CityMarketplaceMedia_City ON commerce.CityMarketplaceMedia(CityId,ResourceType) INCLUDE(MediaId,ContentType,StorageProvider);
END;
COMMIT TRANSACTION;
