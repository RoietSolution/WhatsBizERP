/* V68: persist two tenant-configurable storefront featured category slots. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.StorefrontConfigurations', N'U') IS NULL
    THROW 68000, N'V68 requires commerce.StorefrontConfigurations.', 1;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'FeaturedCategory1Id') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD FeaturedCategory1Id uniqueidentifier NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'FeaturedCategory2Id') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD FeaturedCategory2Id uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StorefrontConfigurations_FeaturedCategory1')
    ALTER TABLE commerce.StorefrontConfigurations ADD CONSTRAINT FK_StorefrontConfigurations_FeaturedCategory1 FOREIGN KEY(FeaturedCategory1Id) REFERENCES master.ProductCategories(ProductCategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StorefrontConfigurations_FeaturedCategory2')
    ALTER TABLE commerce.StorefrontConfigurations ADD CONSTRAINT FK_StorefrontConfigurations_FeaturedCategory2 FOREIGN KEY(FeaturedCategory2Id) REFERENCES master.ProductCategories(ProductCategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_StorefrontConfigurations_FeaturedCategoriesDistinct')
    ALTER TABLE commerce.StorefrontConfigurations ADD CONSTRAINT CK_StorefrontConfigurations_FeaturedCategoriesDistinct CHECK(FeaturedCategory1Id IS NULL OR FeaturedCategory2Id IS NULL OR FeaturedCategory1Id <> FeaturedCategory2Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontConfigurations_FeaturedCategory1Id' AND object_id=OBJECT_ID(N'commerce.StorefrontConfigurations'))
    CREATE INDEX IX_StorefrontConfigurations_FeaturedCategory1Id ON commerce.StorefrontConfigurations(FeaturedCategory1Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontConfigurations_FeaturedCategory2Id' AND object_id=OBJECT_ID(N'commerce.StorefrontConfigurations'))
    CREATE INDEX IX_StorefrontConfigurations_FeaturedCategory2Id ON commerce.StorefrontConfigurations(FeaturedCategory2Id);
