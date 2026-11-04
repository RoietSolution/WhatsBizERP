/* V83: tenant-scoped supplier catalogue import profiles and externally hosted media references. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'core.Tenants',N'U') IS NULL OR OBJECT_ID(N'purchase.Suppliers',N'U') IS NULL OR OBJECT_ID(N'master.Products',N'U') IS NULL
 OR OBJECT_ID(N'master.Brands',N'U') IS NULL OR OBJECT_ID(N'master.UnitsOfMeasure',N'U') IS NULL
    THROW 83000,N'V83 requires canonical tenant, supplier, product, brand and unit tables.',1;

BEGIN TRANSACTION;
ALTER TABLE master.Products ALTER COLUMN ImageUrl nvarchar(2048) NULL;
IF COL_LENGTH(N'master.Products',N'PackagingCost') IS NULL
    ALTER TABLE master.Products ADD PackagingCost decimal(18,4) NULL;
IF COL_LENGTH(N'master.Products',N'ShippingCost') IS NULL
    ALTER TABLE master.Products ADD ShippingCost decimal(18,4) NULL;
IF COL_LENGTH(N'master.Products',N'CombinedShippingPackagingCost') IS NULL
    ALTER TABLE master.Products ADD CombinedShippingPackagingCost decimal(18,4) NULL;

IF OBJECT_ID(N'master.ProductExternalImages',N'U') IS NULL
BEGIN
    CREATE TABLE master.ProductExternalImages(
        ExternalImageId uniqueidentifier NOT NULL CONSTRAINT PK_ProductExternalImages PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        SupplierId uniqueidentifier NULL,
        ImageUrl nvarchar(2048) NOT NULL,
        DisplayOrder int NOT NULL,
        IsPrimary bit NOT NULL CONSTRAINT DF_ProductExternalImages_Primary DEFAULT(0),
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_ProductExternalImages_CreatedOn DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_ProductExternalImages_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_ProductExternalImages_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId),
        CONSTRAINT FK_ProductExternalImages_Supplier FOREIGN KEY(SupplierId) REFERENCES purchase.Suppliers(SupplierId),
        CONSTRAINT CK_ProductExternalImages_Https CHECK(ImageUrl LIKE N'https://%' AND LEN(ImageUrl)<=2048),
        CONSTRAINT CK_ProductExternalImages_Order CHECK(DisplayOrder>=0)
    );
    CREATE INDEX IX_ProductExternalImages_TenantProduct ON master.ProductExternalImages(TenantId,ProductId,DisplayOrder);
END;

IF OBJECT_ID(N'purchase.SupplierCatalogImportProfiles',N'U') IS NULL
BEGIN
    CREATE TABLE purchase.SupplierCatalogImportProfiles(
        TenantId uniqueidentifier NOT NULL,
        SupplierId uniqueidentifier NOT NULL,
        Format nvarchar(20) NOT NULL,
        FieldMappingsJson nvarchar(max) NOT NULL,
        SupplierOwnedFieldsJson nvarchar(max) NOT NULL CONSTRAINT DF_SupplierCatalogProfiles_Ownership DEFAULT(N'{"productName":true,"cost":true,"description":true,"images":true}'),
        AutoCreateCategories bit NOT NULL CONSTRAINT DF_SupplierCatalogProfiles_AutoCreate DEFAULT(0),
        DefaultUnitId uniqueidentifier NULL,
        DefaultBrandId uniqueidentifier NULL,
        DefaultGstPercentage decimal(5,2) NULL,
        ModifiedOn datetimeoffset NOT NULL CONSTRAINT DF_SupplierCatalogProfiles_ModifiedOn DEFAULT(SYSUTCDATETIME()),
        ModifiedBy nvarchar(256) NULL,
        Version rowversion NOT NULL,
        CONSTRAINT PK_SupplierCatalogImportProfiles PRIMARY KEY(TenantId,SupplierId),
        CONSTRAINT FK_SupplierCatalogProfiles_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_SupplierCatalogProfiles_Supplier FOREIGN KEY(SupplierId) REFERENCES purchase.Suppliers(SupplierId),
        CONSTRAINT FK_SupplierCatalogProfiles_Unit FOREIGN KEY(DefaultUnitId) REFERENCES master.UnitsOfMeasure(UnitId),
        CONSTRAINT FK_SupplierCatalogProfiles_Brand FOREIGN KEY(DefaultBrandId) REFERENCES master.Brands(BrandId),
        CONSTRAINT CK_SupplierCatalogProfiles_Format CHECK(Format IN(N'SHOPIFY',N'ERP_TEMPLATE')),
        CONSTRAINT CK_SupplierCatalogProfiles_Mappings CHECK(ISJSON(FieldMappingsJson)=1),
        CONSTRAINT CK_SupplierCatalogProfiles_Ownership CHECK(ISJSON(SupplierOwnedFieldsJson)=1),
        CONSTRAINT CK_SupplierCatalogProfiles_Gst CHECK(DefaultGstPercentage IS NULL OR DefaultGstPercentage BETWEEN 0 AND 100)
    );
END;

IF OBJECT_ID(N'purchase.SupplierCatalogProducts',N'U') IS NULL
BEGIN
    CREATE TABLE purchase.SupplierCatalogProducts(
        SupplierCatalogProductId uniqueidentifier NOT NULL CONSTRAINT PK_SupplierCatalogProducts PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        SupplierId uniqueidentifier NOT NULL,
        SupplierSku nvarchar(150) NULL,
        SourceHandle nvarchar(250) NULL,
        SourceRowIdentity nvarchar(250) NOT NULL,
        ProductId uniqueidentifier NULL,
        OriginalTagsJson nvarchar(max) NOT NULL,
        ProductCost decimal(18,4) NULL,
        PackagingCost decimal(18,4) NULL,
        ShippingCost decimal(18,4) NULL,
        CombinedShippingPackagingCost decimal(18,4) NULL,
        SupplierAvailableQuantity decimal(18,4) NULL,
        DescriptionHtml nvarchar(max) NULL,
        DescriptionText nvarchar(max) NULL,
        ImageUrlsJson nvarchar(max) NOT NULL,
        LastImportedOn datetimeoffset NOT NULL,
        CONSTRAINT FK_SupplierCatalogProducts_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_SupplierCatalogProducts_Supplier FOREIGN KEY(SupplierId) REFERENCES purchase.Suppliers(SupplierId),
        CONSTRAINT FK_SupplierCatalogProducts_Product FOREIGN KEY(ProductId) REFERENCES master.Products(ProductId),
        CONSTRAINT CK_SupplierCatalogProducts_Tags CHECK(ISJSON(OriginalTagsJson)=1),
        CONSTRAINT CK_SupplierCatalogProducts_Images CHECK(ISJSON(ImageUrlsJson)=1)
    );
    CREATE UNIQUE INDEX UX_SupplierCatalogProducts_TenantSupplierSku ON purchase.SupplierCatalogProducts(TenantId,SupplierId,SupplierSku) WHERE SupplierSku IS NOT NULL;
    CREATE UNIQUE INDEX UX_SupplierCatalogProducts_SourceRow ON purchase.SupplierCatalogProducts(TenantId,SupplierId,SourceRowIdentity);
    CREATE INDEX IX_SupplierCatalogProducts_TenantProduct ON purchase.SupplierCatalogProducts(TenantId,ProductId) WHERE ProductId IS NOT NULL;
END;

IF OBJECT_ID(N'purchase.SupplierCatalogImportHistory',N'U') IS NULL
BEGIN
    CREATE TABLE purchase.SupplierCatalogImportHistory(
        ImportId uniqueidentifier NOT NULL CONSTRAINT PK_SupplierCatalogImportHistory PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        SupplierId uniqueidentifier NOT NULL,
        Format nvarchar(20) NOT NULL,
        SourceFileHash char(64) NOT NULL,
        ImportedRows int NOT NULL,
        ErrorRows int NOT NULL,
        ImportedOn datetimeoffset NOT NULL,
        ImportedBy nvarchar(256) NULL,
        CONSTRAINT FK_SupplierCatalogImportHistory_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_SupplierCatalogImportHistory_Supplier FOREIGN KEY(SupplierId) REFERENCES purchase.Suppliers(SupplierId),
        CONSTRAINT CK_SupplierCatalogImportHistory_Counts CHECK(ImportedRows>=0 AND ErrorRows>=0)
    );
    CREATE INDEX IX_SupplierCatalogImportHistory_TenantSupplierDate ON purchase.SupplierCatalogImportHistory(TenantId,SupplierId,ImportedOn DESC);
END;
COMMIT TRANSACTION;
