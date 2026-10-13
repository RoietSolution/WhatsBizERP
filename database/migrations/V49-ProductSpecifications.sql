/* V49 - Tenant-scoped dynamic product specifications. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'master.Products', N'ShortDescription') IS NULL THROW 52480, N'V49 requires the existing Products.ShortDescription column.', 1;
IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'master.Products') AND name = N'ShortDescription') <> -1
    ALTER TABLE master.Products ALTER COLUMN ShortDescription nvarchar(max) NULL;
GO

IF OBJECT_ID(N'master.ProductSpecifications', N'U') IS NULL
BEGIN
    CREATE TABLE master.ProductSpecifications
    (
        ProductSpecificationId uniqueidentifier NOT NULL CONSTRAINT PK_ProductSpecifications PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        ProductId uniqueidentifier NOT NULL,
        Name nvarchar(100) NOT NULL,
        Value nvarchar(1000) NOT NULL,
        DisplayOrder int NOT NULL CONSTRAINT DF_ProductSpecifications_DisplayOrder DEFAULT (0),
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_ProductSpecifications_CreatedOn DEFAULT (SYSUTCDATETIME()),
        CreatedBy nvarchar(256) NULL,
        ModifiedOn datetimeoffset NULL,
        ModifiedBy nvarchar(256) NULL,
        IsActive bit NOT NULL CONSTRAINT DF_ProductSpecifications_IsActive DEFAULT (1),
        IsDeleted bit NOT NULL CONSTRAINT DF_ProductSpecifications_IsDeleted DEFAULT (0),
        RowVersion rowversion NOT NULL,
        CONSTRAINT FK_ProductSpecifications_Product FOREIGN KEY (ProductId) REFERENCES master.Products(ProductId)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'master.ProductSpecifications') AND name = N'UX_ProductSpecifications_Tenant_Product_Name')
    CREATE UNIQUE INDEX UX_ProductSpecifications_Tenant_Product_Name ON master.ProductSpecifications(TenantId, ProductId, Name) WHERE IsDeleted = 0;
GO
