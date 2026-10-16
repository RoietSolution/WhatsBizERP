/* V60: explicit storefront delivery coverage mode and PAN-India exclusions. */
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'DeliveryCoverageMode') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD DeliveryCoverageMode varchar(20) NOT NULL
        CONSTRAINT DF_StorefrontConfigurations_DeliveryCoverageMode DEFAULT('ALLOWLIST') WITH VALUES;

IF COL_LENGTH(N'commerce.StorefrontServiceablePincodes', N'IsExcluded') IS NULL
    ALTER TABLE commerce.StorefrontServiceablePincodes ADD IsExcluded bit NOT NULL
        CONSTRAINT DF_StorefrontServiceablePincodes_IsExcluded DEFAULT(0) WITH VALUES;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_StorefrontConfigurations_DeliveryCoverageMode'
              AND parent_object_id = OBJECT_ID(N'commerce.StorefrontConfigurations'))
    EXEC sys.sp_executesql N'ALTER TABLE commerce.StorefrontConfigurations ADD CONSTRAINT CK_StorefrontConfigurations_DeliveryCoverageMode CHECK (DeliveryCoverageMode IN (''ALLOWLIST'',''PAN_INDIA''));';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StorefrontServiceablePincodes_Coverage'
              AND object_id = OBJECT_ID(N'commerce.StorefrontServiceablePincodes'))
    EXEC sys.sp_executesql N'CREATE INDEX IX_StorefrontServiceablePincodes_Coverage ON commerce.StorefrontServiceablePincodes(TenantId, IsExcluded, IsActive, Pincode);';
