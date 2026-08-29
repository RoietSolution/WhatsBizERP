SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ProductsObjectId INT = OBJECT_ID(N'master.Products');
    DECLARE @ProductBarcodesObjectId INT = OBJECT_ID(N'master.ProductBarcodes');
    IF @ProductsObjectId IS NULL OR @ProductBarcodesObjectId IS NULL
        THROW 51000, 'The product identifier tables do not exist.', 1;

    IF EXISTS (
        SELECT 1
        FROM (
            SELECT p.TenantId, p.ProductId, p.Barcode
            FROM master.Products p
            WHERE p.Barcode IS NOT NULL AND p.IsDeleted = 0
            UNION ALL
            SELECT p.TenantId, b.ProductId, b.Barcode
            FROM master.ProductBarcodes b
            JOIN master.Products p ON p.ProductId = b.ProductId
            WHERE b.IsDeleted = 0 AND p.IsDeleted = 0
        ) identifiers
        GROUP BY identifiers.TenantId, identifiers.Barcode
        HAVING COUNT(DISTINCT identifiers.ProductId) > 1
    )
        THROW 51000, 'Duplicate product identifiers exist within a tenant. Resolve them before rerunning V22.', 1;

    IF EXISTS (
        SELECT 1
        FROM master.ProductBarcodes b
        JOIN master.Products p ON p.ProductId = b.ProductId
        WHERE b.IsDeleted = 0 AND p.IsDeleted = 0
        GROUP BY p.TenantId, b.Barcode
        HAVING COUNT(*) > 1
    )
        THROW 51000, 'Duplicate ProductBarcodes rows exist within a tenant. Resolve them before rerunning V22.', 1;

    IF COL_LENGTH(N'master.ProductBarcodes', N'TenantId') IS NULL
        ALTER TABLE master.ProductBarcodes ADD TenantId UNIQUEIDENTIFIER NULL;

    UPDATE b
    SET TenantId = p.TenantId
    FROM master.ProductBarcodes b
    JOIN master.Products p ON p.ProductId = b.ProductId
    WHERE b.TenantId IS NULL;

    IF EXISTS (SELECT 1 FROM master.ProductBarcodes WHERE TenantId IS NULL)
        THROW 51000, 'One or more ProductBarcodes rows have no owning tenant.', 1;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = @ProductBarcodesObjectId AND [name] = N'TenantId' AND [is_nullable] = 1)
        ALTER TABLE master.ProductBarcodes ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;

    IF COL_LENGTH(N'master.ProductBarcodes', N'BarcodeType') IS NULL
        ALTER TABLE master.ProductBarcodes ADD BarcodeType NVARCHAR(20) NULL;

    UPDATE master.ProductBarcodes SET BarcodeType = N'CUSTOM' WHERE BarcodeType IS NULL OR LTRIM(RTRIM(BarcodeType)) = N'';
    IF EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = @ProductBarcodesObjectId AND [name] = N'BarcodeType' AND [is_nullable] = 1)
        ALTER TABLE master.ProductBarcodes ALTER COLUMN BarcodeType NVARCHAR(20) NOT NULL;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = @ProductBarcodesObjectId AND [name] = N'UX_ProductBarcodes_Barcode')
        DROP INDEX UX_ProductBarcodes_Barcode ON master.ProductBarcodes;

    IF COL_LENGTH(N'master.ProductBarcodes', N'Barcode') <> 900
        ALTER TABLE master.ProductBarcodes ALTER COLUMN Barcode NVARCHAR(450) NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE [parent_object_id] = @ProductBarcodesObjectId AND [name] = N'DF_ProductBarcodes_BarcodeType')
        ALTER TABLE master.ProductBarcodes ADD CONSTRAINT DF_ProductBarcodes_BarcodeType DEFAULT (N'CUSTOM') FOR BarcodeType;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE [parent_object_id] = @ProductBarcodesObjectId AND [name] = N'CK_ProductBarcodes_BarcodeType')
        ALTER TABLE master.ProductBarcodes WITH CHECK ADD CONSTRAINT CK_ProductBarcodes_BarcodeType CHECK (BarcodeType IN (N'CODE128',N'EAN13',N'EAN8',N'UPC',N'UPCA',N'UPCE',N'CODE39',N'QR',N'CUSTOM'));

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE [parent_object_id] = @ProductsObjectId AND [name] = N'CK_Products_BarcodeType')
        ALTER TABLE master.Products DROP CONSTRAINT CK_Products_BarcodeType;
    ALTER TABLE master.Products WITH CHECK ADD CONSTRAINT CK_Products_BarcodeType CHECK (BarcodeType IN (N'CODE128',N'EAN13',N'EAN8',N'UPC',N'UPCA',N'UPCE',N'CODE39',N'QR',N'CUSTOM'));

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = @ProductsObjectId AND [name] = N'UX_Products_Tenant_ProductId')
        CREATE UNIQUE INDEX UX_Products_Tenant_ProductId ON master.Products(TenantId, ProductId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [parent_object_id] = @ProductBarcodesObjectId AND [name] = N'FK_ProductBarcodes_Tenants')
        ALTER TABLE master.ProductBarcodes WITH CHECK ADD CONSTRAINT FK_ProductBarcodes_Tenants FOREIGN KEY (TenantId) REFERENCES core.Tenants(TenantId);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [parent_object_id] = @ProductBarcodesObjectId AND [name] = N'FK_ProductBarcodes_TenantProduct')
        ALTER TABLE master.ProductBarcodes WITH CHECK ADD CONSTRAINT FK_ProductBarcodes_TenantProduct FOREIGN KEY (TenantId, ProductId) REFERENCES master.Products(TenantId, ProductId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = @ProductBarcodesObjectId AND [name] = N'UX_ProductBarcodes_Tenant_Barcode')
        CREATE UNIQUE INDEX UX_ProductBarcodes_Tenant_Barcode ON master.ProductBarcodes(TenantId, Barcode) WHERE IsDeleted = 0;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = @ProductBarcodesObjectId AND [name] = N'IX_ProductBarcodes_Tenant_Product')
        CREATE INDEX IX_ProductBarcodes_Tenant_Product ON master.ProductBarcodes(TenantId, ProductId) INCLUDE(Barcode, BarcodeType, IsActive, IsDeleted);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
