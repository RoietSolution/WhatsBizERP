SET NOCOUNT ON;
IF DB_NAME() NOT IN (N'WhatsBizERP_QA', N'WhatsBizERP_PROD') THROW 52490, N'V49 validator may run only against an approved database.', 1;
IF (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'master.Products') AND name = N'ShortDescription') <> -1 THROW 52494, N'V49 validation failed: ShortDescription is not wide enough for formatted content.', 1;
IF OBJECT_ID(N'master.ProductSpecifications', N'U') IS NULL THROW 52491, N'V49 validation failed: ProductSpecifications table is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'master.ProductSpecifications') AND name = N'UX_ProductSpecifications_Tenant_Product_Name' AND is_unique = 1) THROW 52492, N'V49 validation failed: tenant-scoped specification index is missing.', 1;
IF EXISTS (SELECT 1 FROM master.ProductSpecifications WHERE IsDeleted = 0 AND (TenantId IS NULL OR ProductId IS NULL OR NULLIF(LTRIM(RTRIM(Name)), N'') IS NULL OR NULLIF(LTRIM(RTRIM(Value)), N'') IS NULL)) THROW 52493, N'V49 validation failed: specification rows are incomplete.', 1;
SELECT N'V49_VALID' AS ValidationResult;
