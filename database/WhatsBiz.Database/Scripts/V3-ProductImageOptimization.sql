/* Adds optimized image variants while preserving the existing SQL-backed image contract. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH(N'master.Products', N'TenantId') IS NULL
    ALTER TABLE master.Products ADD TenantId uniqueidentifier NULL;
GO
UPDATE p SET TenantId = (SELECT TOP (1) TenantId FROM core.Tenants ORDER BY CreatedOn, TenantId)
FROM master.Products p WHERE p.TenantId IS NULL;
IF EXISTS (SELECT 1 FROM master.Products WHERE TenantId IS NULL)
    THROW 51000, 'Cannot migrate Products: no tenant exists for legacy product ownership.', 1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.Products') AND name=N'TenantId' AND is_nullable=1)
    ALTER TABLE master.Products ALTER COLUMN TenantId uniqueidentifier NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Products_Tenants')
    ALTER TABLE master.Products ADD CONSTRAINT FK_Products_Tenants FOREIGN KEY (TenantId) REFERENCES core.Tenants(TenantId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_Tenant' AND object_id = OBJECT_ID(N'master.Products'))
    CREATE INDEX IX_Products_Tenant ON master.Products(TenantId, IsDeleted, IsActive);

IF COL_LENGTH(N'master.ProductImages', N'TenantId') IS NULL
    ALTER TABLE master.ProductImages ADD TenantId uniqueidentifier NULL;
GO

/* Existing installations predate tenant ownership on product images. Assign legacy rows to
   the original/default tenant before making ownership mandatory. New rows are always stamped
   from the authenticated tenant context by the application. */
UPDATE img SET TenantId = (SELECT TOP (1) TenantId FROM core.Tenants ORDER BY CreatedOn, TenantId)
FROM master.ProductImages img WHERE img.TenantId IS NULL;
IF EXISTS (SELECT 1 FROM master.ProductImages WHERE TenantId IS NULL)
    THROW 51001, 'Cannot migrate ProductImages: no tenant exists for legacy image ownership.', 1;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'TenantId' AND is_nullable=1)
    ALTER TABLE master.ProductImages ALTER COLUMN TenantId uniqueidentifier NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ProductImages_Tenants')
    ALTER TABLE master.ProductImages ADD CONSTRAINT FK_ProductImages_Tenants FOREIGN KEY (TenantId) REFERENCES core.Tenants(TenantId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProductImages_TenantProduct' AND object_id = OBJECT_ID(N'master.ProductImages'))
    CREATE INDEX IX_ProductImages_TenantProduct ON master.ProductImages(TenantId, ProductId, IsDeleted, IsActive);

IF COL_LENGTH(N'master.ProductImages', N'ThumbnailData') IS NULL
    ALTER TABLE master.ProductImages ADD ThumbnailData varbinary(max) NULL, ThumbnailContentType nvarchar(100) NULL,
        Width int NULL, Height int NULL, ThumbnailWidth int NULL, ThumbnailHeight int NULL;
GO
UPDATE master.ProductImages SET ThumbnailData = ISNULL(ThumbnailData, ImageData), ThumbnailContentType = ISNULL(ThumbnailContentType, ContentType),
    Width = ISNULL(Width, 0), Height = ISNULL(Height, 0), ThumbnailWidth = ISNULL(ThumbnailWidth, 0), ThumbnailHeight = ISNULL(ThumbnailHeight, 0);
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'ThumbnailData' AND is_nullable=1)
    ALTER TABLE master.ProductImages ALTER COLUMN ThumbnailData varbinary(max) NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'ThumbnailContentType' AND is_nullable=1)
    ALTER TABLE master.ProductImages ALTER COLUMN ThumbnailContentType nvarchar(100) NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'Width' AND is_nullable=1)
    ALTER TABLE master.ProductImages ALTER COLUMN Width int NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'Height' AND is_nullable=1)
    ALTER TABLE master.ProductImages ALTER COLUMN Height int NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'ThumbnailWidth' AND is_nullable=1)
    ALTER TABLE master.ProductImages ALTER COLUMN ThumbnailWidth int NOT NULL;
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'master.ProductImages') AND name=N'ThumbnailHeight' AND is_nullable=1)
    ALTER TABLE master.ProductImages ALTER COLUMN ThumbnailHeight int NOT NULL;
