SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'master.ProductImages',N'StorageProvider') IS NULL ALTER TABLE master.ProductImages ADD StorageProvider nvarchar(20) NULL;
IF COL_LENGTH(N'master.ProductImages',N'ObjectKey') IS NULL ALTER TABLE master.ProductImages ADD ObjectKey nvarchar(1024) NULL;
IF COL_LENGTH(N'master.ProductImages',N'ThumbnailObjectKey') IS NULL ALTER TABLE master.ProductImages ADD ThumbnailObjectKey nvarchar(1024) NULL;
IF COL_LENGTH(N'master.ProductImages',N'CatalogSizeBytes') IS NULL ALTER TABLE master.ProductImages ADD CatalogSizeBytes bigint NULL;
IF COL_LENGTH(N'master.ProductImages',N'ThumbnailSizeBytes') IS NULL ALTER TABLE master.ProductImages ADD ThumbnailSizeBytes bigint NULL;
IF COL_LENGTH(N'master.ProductImages',N'ContentHash') IS NULL ALTER TABLE master.ProductImages ADD ContentHash varchar(64) NULL;

EXEC(N'UPDATE master.ProductImages SET StorageProvider=ISNULL(StorageProvider,N''DATABASE''),CatalogSizeBytes=ISNULL(CatalogSizeBytes,DATALENGTH(ImageData)),ThumbnailSizeBytes=ISNULL(ThumbnailSizeBytes,DATALENGTH(ThumbnailData)),ContentHash=ISNULL(ContentHash,CONVERT(varchar(64),HASHBYTES(''SHA2_256'',ImageData),2));');
EXEC(N'ALTER TABLE master.ProductImages ALTER COLUMN StorageProvider nvarchar(20) NOT NULL;');
EXEC(N'ALTER TABLE master.ProductImages ALTER COLUMN CatalogSizeBytes bigint NOT NULL;');
EXEC(N'ALTER TABLE master.ProductImages ALTER COLUMN ThumbnailSizeBytes bigint NOT NULL;');

IF NOT EXISTS(SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'master.ProductImages') AND name=N'DF_ProductImages_StorageProvider')
    EXEC(N'ALTER TABLE master.ProductImages ADD CONSTRAINT DF_ProductImages_StorageProvider DEFAULT(N''DATABASE'') FOR StorageProvider;');
IF NOT EXISTS(SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'master.ProductImages') AND name=N'DF_ProductImages_CatalogSize')
    EXEC(N'ALTER TABLE master.ProductImages ADD CONSTRAINT DF_ProductImages_CatalogSize DEFAULT(0) FOR CatalogSizeBytes;');
IF NOT EXISTS(SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'master.ProductImages') AND name=N'DF_ProductImages_ThumbnailSize')
    EXEC(N'ALTER TABLE master.ProductImages ADD CONSTRAINT DF_ProductImages_ThumbnailSize DEFAULT(0) FOR ThumbnailSizeBytes;');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'master.ProductImages') AND name=N'CK_ProductImages_StorageProvider')
    EXEC(N'ALTER TABLE master.ProductImages ADD CONSTRAINT CK_ProductImages_StorageProvider CHECK(StorageProvider IN(N''DATABASE'',N''LOCAL'',N''S3''));');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'master.ProductImages') AND name=N'CK_ProductImages_ExternalKeys')
    EXEC(N'ALTER TABLE master.ProductImages ADD CONSTRAINT CK_ProductImages_ExternalKeys CHECK(StorageProvider=N''DATABASE'' OR (ObjectKey IS NOT NULL AND ThumbnailObjectKey IS NOT NULL));');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'master.ProductImages') AND name=N'CK_ProductImages_StorageSizes')
    EXEC(N'ALTER TABLE master.ProductImages ADD CONSTRAINT CK_ProductImages_StorageSizes CHECK(CatalogSizeBytes>=0 AND ThumbnailSizeBytes>=0);');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'IX_ProductImages_StorageProvider')
    EXEC(N'CREATE INDEX IX_ProductImages_StorageProvider ON master.ProductImages(StorageProvider,TenantId,IsDeleted) INCLUDE(ObjectKey,ThumbnailObjectKey,CatalogSizeBytes,ThumbnailSizeBytes);');

COMMIT TRANSACTION;
