SET NOCOUNT ON;
IF DB_NAME() NOT IN(N'WhatsBizERP_QA',N'WhatsBizERP_PROD') THROW 52400,N'Unexpected database for V47 validation.',1;
IF COL_LENGTH(N'sales.Customers',N'StorefrontPasswordHash') IS NULL OR COL_LENGTH(N'commerce.AnalyticsEvents',N'VisitorSessionId') IS NULL THROW 52401,N'V47 columns are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'IX_AnalyticsEvents_TenantVisitorCreated') THROW 52402,N'V47 visitor activity index is missing.',1;
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'sales.Customers'),OBJECT_ID(N'commerce.AnalyticsEvents')) AND is_disabled=1) THROW 52403,N'V47 constraints are disabled.',1;
SELECT N'V47_VALID' AS ValidationResult;