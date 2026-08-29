CREATE TABLE [master].[ProductCategories] (
    [ProductCategoryId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [CategoryCode] NVARCHAR(50) NOT NULL, [CategoryName] NVARCHAR(200) NOT NULL, [Description] NVARCHAR(1000) NULL, [DisplayOrder] INT NOT NULL, [ParentCategoryId] UNIQUEIDENTIFIER NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductCategories_Parent] FOREIGN KEY ([ParentCategoryId]) REFERENCES [master].[ProductCategories]([ProductCategoryId]));
GO
CREATE UNIQUE INDEX [UX_ProductCategories_CategoryCode] ON [master].[ProductCategories]([CategoryCode]) WHERE [IsDeleted] = 0;
GO
CREATE TABLE [master].[Brands] (
    [BrandId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [BrandCode] NVARCHAR(50) NOT NULL, [BrandName] NVARCHAR(200) NOT NULL, [Description] NVARCHAR(1000) NULL, [Logo] NVARCHAR(500) NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL);
GO
CREATE UNIQUE INDEX [UX_Brands_BrandCode] ON [master].[Brands]([BrandCode]) WHERE [IsDeleted] = 0;
GO
CREATE TABLE [master].[UnitsOfMeasure] (
    [UnitId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [UnitCode] NVARCHAR(50) NOT NULL, [UnitName] NVARCHAR(200) NOT NULL, [ShortName] NVARCHAR(20) NOT NULL, [DecimalPlaces] TINYINT NOT NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL);
GO
CREATE UNIQUE INDEX [UX_UnitsOfMeasure_UnitCode] ON [master].[UnitsOfMeasure]([UnitCode]) WHERE [IsDeleted] = 0;
GO
CREATE TABLE [master].[Products] (
    [ProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductCode] NVARCHAR(50) NOT NULL, [Barcode] NVARCHAR(100) NULL, [BarcodeType] NVARCHAR(20) NOT NULL CONSTRAINT [DF_Products_BarcodeType] DEFAULT ('CODE128'), [ProductName] NVARCHAR(250) NOT NULL, [ShortDescription] NVARCHAR(500) NULL, [LongDescription] NVARCHAR(MAX) NULL,
    [CategoryId] UNIQUEIDENTIFIER NOT NULL, [BrandId] UNIQUEIDENTIFIER NOT NULL, [UnitId] UNIQUEIDENTIFIER NOT NULL, [HSNCode] NVARCHAR(20) NULL, [SACCode] NVARCHAR(20) NULL, [GSTPercentage] DECIMAL(5,2) NOT NULL,
    [PurchasePrice] DECIMAL(18,4) NOT NULL, [SellingPrice] DECIMAL(18,4) NOT NULL, [MRP] DECIMAL(18,4) NOT NULL, [MinimumStock] DECIMAL(18,4) NOT NULL, [MaximumStock] DECIMAL(18,4) NOT NULL, [ReorderLevel] DECIMAL(18,4) NOT NULL,
    [Weight] DECIMAL(18,4) NULL, [Length] DECIMAL(18,4) NULL, [Width] DECIMAL(18,4) NULL, [Height] DECIMAL(18,4) NULL, [ImageUrl] NVARCHAR(500) NULL, [IsBatchManaged] BIT NOT NULL, [IsSerialManaged] BIT NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_Products_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [master].[ProductCategories]([ProductCategoryId]), CONSTRAINT [FK_Products_Brands] FOREIGN KEY ([BrandId]) REFERENCES [master].[Brands]([BrandId]), CONSTRAINT [FK_Products_Units] FOREIGN KEY ([UnitId]) REFERENCES [master].[UnitsOfMeasure]([UnitId]), CONSTRAINT [FK_Products_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants]([TenantId]), CONSTRAINT [CK_Products_BarcodeType] CHECK ([BarcodeType] IN ('CODE128','EAN13','EAN8','UPC','UPCA','UPCE','CODE39','QR','CUSTOM')));
GO
CREATE UNIQUE INDEX [UX_Products_ProductCode] ON [master].[Products]([ProductCode]) WHERE [IsDeleted] = 0;
GO
CREATE UNIQUE INDEX [UX_Products_Tenant_Barcode] ON [master].[Products]([TenantId], [Barcode]) WHERE [Barcode] IS NOT NULL AND [IsDeleted] = 0;
GO
CREATE INDEX [IX_Products_Search] ON [master].[Products]([ProductName], [CategoryId], [BrandId], [IsActive]);
GO
CREATE INDEX [IX_Products_Tenant] ON [master].[Products]([TenantId], [IsDeleted], [IsActive]);
GO
CREATE UNIQUE INDEX [UX_Products_Tenant_ProductId] ON [master].[Products]([TenantId], [ProductId]);
GO
CREATE TABLE [master].[ProductImages] (
    [ProductImageId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductId] UNIQUEIDENTIFIER NOT NULL, [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [FileName] NVARCHAR(255) NOT NULL, [ContentType] NVARCHAR(100) NOT NULL, [ImageData] VARBINARY(MAX) NOT NULL,
    [ThumbnailData] VARBINARY(MAX) NOT NULL, [ThumbnailContentType] NVARCHAR(100) NOT NULL,
    [Width] INT NOT NULL, [Height] INT NOT NULL, [ThumbnailWidth] INT NOT NULL, [ThumbnailHeight] INT NOT NULL,
    [StorageProvider] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ProductImages_StorageProvider] DEFAULT(N'DATABASE'),
    [ObjectKey] NVARCHAR(1024) NULL, [ThumbnailObjectKey] NVARCHAR(1024) NULL,
    [CatalogSizeBytes] BIGINT NOT NULL CONSTRAINT [DF_ProductImages_CatalogSize] DEFAULT(0),
    [ThumbnailSizeBytes] BIGINT NOT NULL CONSTRAINT [DF_ProductImages_ThumbnailSize] DEFAULT(0),
    [ContentHash] VARCHAR(64) NULL, [IsPrimary] BIT NOT NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_ProductImages_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]),
    CONSTRAINT [FK_ProductImages_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants]([TenantId]),
    CONSTRAINT [CK_ProductImages_StorageProvider] CHECK([StorageProvider] IN(N'DATABASE',N'LOCAL',N'S3')),
    CONSTRAINT [CK_ProductImages_ExternalKeys] CHECK([StorageProvider]=N'DATABASE' OR ([ObjectKey] IS NOT NULL AND [ThumbnailObjectKey] IS NOT NULL)),
    CONSTRAINT [CK_ProductImages_StorageSizes] CHECK([CatalogSizeBytes]>=0 AND [ThumbnailSizeBytes]>=0));
GO
CREATE INDEX [IX_ProductImages_TenantProduct] ON [master].[ProductImages]([TenantId], [ProductId], [IsDeleted], [IsActive]);
GO
CREATE INDEX [IX_ProductImages_StorageProvider] ON [master].[ProductImages]([StorageProvider], [TenantId], [IsDeleted]) INCLUDE([ObjectKey], [ThumbnailObjectKey], [CatalogSizeBytes], [ThumbnailSizeBytes]);
GO
CREATE TABLE [master].[ProductBarcodes] ([ProductBarcodeId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [TenantId] UNIQUEIDENTIFIER NOT NULL, [ProductId] UNIQUEIDENTIFIER NOT NULL, [Barcode] NVARCHAR(450) NOT NULL, [BarcodeType] NVARCHAR(20) NOT NULL CONSTRAINT [DF_ProductBarcodes_BarcodeType] DEFAULT ('CUSTOM'), [IsPrimary] BIT NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_ProductBarcodes_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]), CONSTRAINT [FK_ProductBarcodes_Tenants] FOREIGN KEY ([TenantId]) REFERENCES [core].[Tenants]([TenantId]), CONSTRAINT [FK_ProductBarcodes_TenantProduct] FOREIGN KEY ([TenantId], [ProductId]) REFERENCES [master].[Products]([TenantId], [ProductId]), CONSTRAINT [CK_ProductBarcodes_BarcodeType] CHECK ([BarcodeType] IN ('CODE128','EAN13','EAN8','UPC','UPCA','UPCE','CODE39','QR','CUSTOM')));
GO
CREATE UNIQUE INDEX [UX_ProductBarcodes_Tenant_Barcode] ON [master].[ProductBarcodes]([TenantId], [Barcode]) WHERE [IsActive] = 1 AND [IsDeleted] = 0;
GO
CREATE INDEX [IX_ProductBarcodes_Tenant_Product] ON [master].[ProductBarcodes]([TenantId], [ProductId]) INCLUDE([Barcode], [BarcodeType], [IsActive], [IsDeleted]);
GO
CREATE TABLE [master].[ProductPrices] ([ProductPriceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductId] UNIQUEIDENTIFIER NOT NULL, [PriceType] NVARCHAR(50) NOT NULL, [Amount] DECIMAL(18,4) NOT NULL, [EffectiveFrom] DATETIMEOFFSET NOT NULL, [EffectiveTo] DATETIMEOFFSET NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_ProductPrices_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]));
GO
CREATE TABLE [master].[ProductTaxMappings] ([ProductTaxMappingId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductId] UNIQUEIDENTIFIER NOT NULL, [TaxCode] NVARCHAR(50) NOT NULL, [TaxPercentage] DECIMAL(5,2) NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_ProductTaxMappings_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]));
