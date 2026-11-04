SET NOCOUNT ON;
IF OBJECT_ID(N'master.ProductExternalImages',N'U') IS NULL OR OBJECT_ID(N'purchase.SupplierCatalogImportProfiles',N'U') IS NULL OR OBJECT_ID(N'purchase.SupplierCatalogProducts',N'U') IS NULL OR OBJECT_ID(N'purchase.SupplierCatalogImportHistory',N'U') IS NULL
    THROW 83010,N'V83 validation failed: supplier catalogue or external image tables are missing.',1;
IF COL_LENGTH(N'master.Products',N'PackagingCost') IS NULL OR COL_LENGTH(N'master.Products',N'ShippingCost') IS NULL OR COL_LENGTH(N'master.Products',N'CombinedShippingPackagingCost') IS NULL
    THROW 83011,N'V83 validation failed: product cost breakdown columns are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'master.ProductExternalImages') AND name=N'CK_ProductExternalImages_Https')
 OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportProfiles') AND name=N'CK_SupplierCatalogProfiles_Mappings')
 OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'CK_SupplierCatalogProducts_Tags')
    THROW 83012,N'V83 validation failed: external URL and JSON constraints are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'FK_SupplierCatalogProducts_Tenant')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'FK_SupplierCatalogProducts_Supplier')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'master.ProductExternalImages') AND name=N'FK_ProductExternalImages_Product')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'master.ProductExternalImages') AND name=N'FK_ProductExternalImages_Supplier')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportProfiles') AND name=N'FK_SupplierCatalogProfiles_Unit')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportProfiles') AND name=N'FK_SupplierCatalogProfiles_Brand')
    THROW 83013,N'V83 validation failed: tenant/supplier/product foreign keys are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'UX_SupplierCatalogProducts_TenantSupplierSku')
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'UX_SupplierCatalogProducts_SourceRow')
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'purchase.SupplierCatalogImportHistory') AND name=N'IX_SupplierCatalogImportHistory_TenantSupplierDate')
    THROW 83014,N'V83 validation failed: idempotency and tenant-leading indexes are missing.',1;
