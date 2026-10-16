IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'DeliveryCoverageMode') IS NULL
    THROW 60001, N'V60 delivery coverage mode column is missing.', 1;
IF COL_LENGTH(N'commerce.StorefrontServiceablePincodes', N'IsExcluded') IS NULL
    THROW 60002, N'V60 pincode exclusion column is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
               WHERE dc.parent_object_id=OBJECT_ID(N'commerce.StorefrontConfigurations') AND c.name=N'DeliveryCoverageMode'
                 AND REPLACE(REPLACE(dc.definition,N'(',N''),N')',N'') LIKE N'%ALLOWLIST%')
    THROW 60005, N'V60 delivery coverage default is missing or incorrect.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints dc JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
               WHERE dc.parent_object_id=OBJECT_ID(N'commerce.StorefrontServiceablePincodes') AND c.name=N'IsExcluded'
                 AND REPLACE(REPLACE(dc.definition,N'(',N''),N')',N'') LIKE N'%0%')
    THROW 60006, N'V60 pincode exclusion default is missing or incorrect.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_StorefrontConfigurations_DeliveryCoverageMode'
              AND parent_object_id = OBJECT_ID(N'commerce.StorefrontConfigurations'))
    THROW 60003, N'V60 delivery coverage constraint is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StorefrontServiceablePincodes_Coverage'
              AND object_id = OBJECT_ID(N'commerce.StorefrontServiceablePincodes'))
    THROW 60004, N'V60 delivery coverage index is missing.', 1;
