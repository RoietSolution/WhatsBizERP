IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'FeaturedCategory1Id') IS NULL OR COL_LENGTH(N'commerce.StorefrontConfigurations', N'FeaturedCategory2Id') IS NULL
    THROW 68001, N'V68 validation failed: featured category columns are missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StorefrontConfigurations_FeaturedCategory1') OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StorefrontConfigurations_FeaturedCategory2')
    THROW 68002, N'V68 validation failed: featured category foreign keys are missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_StorefrontConfigurations_FeaturedCategoriesDistinct')
    THROW 68003, N'V68 validation failed: featured category distinctness constraint is missing.', 1;
SELECT N'V68_STOREFRONT_FEATURED_CATEGORIES_VALID' AS ValidationResult;
