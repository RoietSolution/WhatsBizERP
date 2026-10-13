SET NOCOUNT ON;

IF DB_NAME() NOT IN (N'WhatsBizERP_QA', N'WhatsBizERP_PROD')
    THROW 52501, N'V50 validator may run only against an approved database.', 1;
IF COL_LENGTH(N'core.Tenants', N'DisplayName') IS NULL
    THROW 52502, N'V50 validation failed: core.Tenants.DisplayName is missing.', 1;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'core.Tenants') AND name = N'DisplayName' AND (max_length <> 400 OR is_nullable <> 1))
    THROW 52503, N'V50 validation failed: DisplayName must be nullable nvarchar(200).', 1;
IF EXISTS (SELECT 1 FROM core.Tenants WHERE DisplayName IS NOT NULL AND (LEN(DisplayName) = 0 OR LEN(DisplayName) > 200 OR DisplayName <> LTRIM(RTRIM(DisplayName))))
    THROW 52504, N'V50 validation failed: tenant display names must be trimmed and no longer than 200 characters.', 1;
SELECT N'V50_VALID' AS ValidationResult;
