/* Virtual All-category artwork uses existing tenant-owned StorefrontMedia storage. */
SET XACT_ABORT ON;
GO
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'AllCategoryMediaId') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD AllCategoryMediaId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StorefrontConfigurations_AllCategoryMedia')
    ALTER TABLE commerce.StorefrontConfigurations WITH CHECK ADD CONSTRAINT FK_StorefrontConfigurations_AllCategoryMedia
        FOREIGN KEY (TenantId, AllCategoryMediaId) REFERENCES commerce.StorefrontMedia(TenantId, MediaId);
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'commerce.StorefrontMedia') AND name = N'CK_StorefrontMedia_Resource')
    ALTER TABLE commerce.StorefrontMedia DROP CONSTRAINT CK_StorefrontMedia_Resource;
GO
ALTER TABLE commerce.StorefrontMedia WITH CHECK ADD CONSTRAINT CK_StorefrontMedia_Resource
    CHECK (ResourceType IN (N'logo', N'category', N'category-all', N'banner-primary', N'banner-secondary'));
GO
