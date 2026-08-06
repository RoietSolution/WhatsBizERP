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
    [ProductId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductCode] NVARCHAR(50) NOT NULL, [Barcode] NVARCHAR(100) NULL, [ProductName] NVARCHAR(250) NOT NULL, [ShortDescription] NVARCHAR(500) NULL, [LongDescription] NVARCHAR(MAX) NULL,
    [CategoryId] UNIQUEIDENTIFIER NOT NULL, [BrandId] UNIQUEIDENTIFIER NOT NULL, [UnitId] UNIQUEIDENTIFIER NOT NULL, [HSNCode] NVARCHAR(20) NULL, [SACCode] NVARCHAR(20) NULL, [GSTPercentage] DECIMAL(5,2) NOT NULL,
    [PurchasePrice] DECIMAL(18,4) NOT NULL, [SellingPrice] DECIMAL(18,4) NOT NULL, [MRP] DECIMAL(18,4) NOT NULL, [MinimumStock] DECIMAL(18,4) NOT NULL, [MaximumStock] DECIMAL(18,4) NOT NULL, [ReorderLevel] DECIMAL(18,4) NOT NULL,
    [Weight] DECIMAL(18,4) NULL, [Length] DECIMAL(18,4) NULL, [Width] DECIMAL(18,4) NULL, [Height] DECIMAL(18,4) NULL, [ImageUrl] NVARCHAR(500) NULL, [IsBatchManaged] BIT NOT NULL, [IsSerialManaged] BIT NOT NULL,
    [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [FK_Products_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [master].[ProductCategories]([ProductCategoryId]), CONSTRAINT [FK_Products_Brands] FOREIGN KEY ([BrandId]) REFERENCES [master].[Brands]([BrandId]), CONSTRAINT [FK_Products_Units] FOREIGN KEY ([UnitId]) REFERENCES [master].[UnitsOfMeasure]([UnitId]));
GO
CREATE UNIQUE INDEX [UX_Products_ProductCode] ON [master].[Products]([ProductCode]) WHERE [IsDeleted] = 0;
GO
CREATE UNIQUE INDEX [UX_Products_Barcode] ON [master].[Products]([Barcode]) WHERE [Barcode] IS NOT NULL AND [IsDeleted] = 0;
GO
CREATE INDEX [IX_Products_Search] ON [master].[Products]([ProductName], [CategoryId], [BrandId], [IsActive]);
GO
CREATE TABLE [master].[ProductImages] ([ProductImageId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductId] UNIQUEIDENTIFIER NOT NULL, [FileName] NVARCHAR(255) NOT NULL, [ContentType] NVARCHAR(100) NOT NULL, [ImageData] VARBINARY(MAX) NOT NULL, [IsPrimary] BIT NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_ProductImages_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]));
GO
CREATE TABLE [master].[ProductBarcodes] ([ProductBarcodeId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductId] UNIQUEIDENTIFIER NOT NULL, [Barcode] NVARCHAR(100) NOT NULL, [IsPrimary] BIT NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_ProductBarcodes_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]));
GO
CREATE UNIQUE INDEX [UX_ProductBarcodes_Barcode] ON [master].[ProductBarcodes]([Barcode]) WHERE [IsDeleted] = 0;
GO
CREATE TABLE [master].[ProductPrices] ([ProductPriceId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductId] UNIQUEIDENTIFIER NOT NULL, [PriceType] NVARCHAR(50) NOT NULL, [Amount] DECIMAL(18,4) NOT NULL, [EffectiveFrom] DATETIMEOFFSET NOT NULL, [EffectiveTo] DATETIMEOFFSET NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_ProductPrices_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]));
GO
CREATE TABLE [master].[ProductTaxMappings] ([ProductTaxMappingId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, [ProductId] UNIQUEIDENTIFIER NOT NULL, [TaxCode] NVARCHAR(50) NOT NULL, [TaxPercentage] DECIMAL(5,2) NOT NULL, [CreatedOn] DATETIMEOFFSET NOT NULL, [CreatedBy] NVARCHAR(256) NULL, [ModifiedOn] DATETIMEOFFSET NULL, [ModifiedBy] NVARCHAR(256) NULL, [IsActive] BIT NOT NULL, [IsDeleted] BIT NOT NULL, [RowVersion] ROWVERSION NOT NULL, CONSTRAINT [FK_ProductTaxMappings_Products] FOREIGN KEY ([ProductId]) REFERENCES [master].[Products]([ProductId]));
