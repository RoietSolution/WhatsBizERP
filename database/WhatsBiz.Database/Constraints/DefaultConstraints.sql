ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_EmailConfirmed] DEFAULT (0) FOR [EmailConfirmed];
GO
ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_PhoneNumberConfirmed] DEFAULT (0) FOR [PhoneNumberConfirmed];
GO
ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_TwoFactorEnabled] DEFAULT (0) FOR [TwoFactorEnabled];
GO
ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_LockoutEnabled] DEFAULT (0) FOR [LockoutEnabled];
GO
ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_AccessFailedCount] DEFAULT (0) FOR [AccessFailedCount];
GO
ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [core].[Users] ADD CONSTRAINT [DF_Users_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [core].[RefreshTokens] ADD CONSTRAINT [DF_RefreshTokens_Id] DEFAULT (NEWSEQUENTIALID()) FOR [Id];
GO
ALTER TABLE [core].[RefreshTokens] ADD CONSTRAINT [DF_RefreshTokens_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [core].[RefreshTokens] ADD CONSTRAINT [DF_RefreshTokens_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [core].[RefreshTokens] ADD CONSTRAINT [DF_RefreshTokens_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[ProductCategories] ADD CONSTRAINT [DF_ProductCategories_Id] DEFAULT (NEWSEQUENTIALID()) FOR [ProductCategoryId];
GO
ALTER TABLE [master].[ProductCategories] ADD CONSTRAINT [DF_ProductCategories_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[ProductCategories] ADD CONSTRAINT [DF_ProductCategories_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[ProductCategories] ADD CONSTRAINT [DF_ProductCategories_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[Brands] ADD CONSTRAINT [DF_Brands_Id] DEFAULT (NEWSEQUENTIALID()) FOR [BrandId];
GO
ALTER TABLE [master].[Brands] ADD CONSTRAINT [DF_Brands_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[Brands] ADD CONSTRAINT [DF_Brands_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[Brands] ADD CONSTRAINT [DF_Brands_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[UnitsOfMeasure] ADD CONSTRAINT [DF_UnitsOfMeasure_Id] DEFAULT (NEWSEQUENTIALID()) FOR [UnitId];
GO
ALTER TABLE [master].[UnitsOfMeasure] ADD CONSTRAINT [DF_UnitsOfMeasure_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[UnitsOfMeasure] ADD CONSTRAINT [DF_UnitsOfMeasure_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[UnitsOfMeasure] ADD CONSTRAINT [DF_UnitsOfMeasure_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[Products] ADD CONSTRAINT [DF_Products_Id] DEFAULT (NEWSEQUENTIALID()) FOR [ProductId];
GO
ALTER TABLE [master].[Products] ADD CONSTRAINT [DF_Products_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[Products] ADD CONSTRAINT [DF_Products_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[Products] ADD CONSTRAINT [DF_Products_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[ProductImages] ADD CONSTRAINT [DF_ProductImages_Id] DEFAULT (NEWSEQUENTIALID()) FOR [ProductImageId];
GO
ALTER TABLE [master].[ProductImages] ADD CONSTRAINT [DF_ProductImages_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[ProductImages] ADD CONSTRAINT [DF_ProductImages_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[ProductImages] ADD CONSTRAINT [DF_ProductImages_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[ProductBarcodes] ADD CONSTRAINT [DF_ProductBarcodes_Id] DEFAULT (NEWSEQUENTIALID()) FOR [ProductBarcodeId];
GO
ALTER TABLE [master].[ProductBarcodes] ADD CONSTRAINT [DF_ProductBarcodes_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[ProductBarcodes] ADD CONSTRAINT [DF_ProductBarcodes_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[ProductBarcodes] ADD CONSTRAINT [DF_ProductBarcodes_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[ProductPrices] ADD CONSTRAINT [DF_ProductPrices_Id] DEFAULT (NEWSEQUENTIALID()) FOR [ProductPriceId];
GO
ALTER TABLE [master].[ProductPrices] ADD CONSTRAINT [DF_ProductPrices_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[ProductPrices] ADD CONSTRAINT [DF_ProductPrices_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[ProductPrices] ADD CONSTRAINT [DF_ProductPrices_IsDeleted] DEFAULT (0) FOR [IsDeleted];
GO
ALTER TABLE [master].[ProductTaxMappings] ADD CONSTRAINT [DF_ProductTaxMappings_Id] DEFAULT (NEWSEQUENTIALID()) FOR [ProductTaxMappingId];
GO
ALTER TABLE [master].[ProductTaxMappings] ADD CONSTRAINT [DF_ProductTaxMappings_CreatedOn] DEFAULT (SYSUTCDATETIME()) FOR [CreatedOn];
GO
ALTER TABLE [master].[ProductTaxMappings] ADD CONSTRAINT [DF_ProductTaxMappings_IsActive] DEFAULT (1) FOR [IsActive];
GO
ALTER TABLE [master].[ProductTaxMappings] ADD CONSTRAINT [DF_ProductTaxMappings_IsDeleted] DEFAULT (0) FOR [IsDeleted];
