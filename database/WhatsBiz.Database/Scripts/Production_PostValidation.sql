SET NOCOUNT ON;
IF DB_NAME() <> N'WhatsBizERP_PROD'
    THROW 51900, N'Production validation may run only in WhatsBizERP_PROD.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID(N'WhatsBizERP_PROD')
      AND page_verify_option_desc = N'CHECKSUM'
)
    THROW 51907, N'Production database PAGE_VERIFY must be CHECKSUM.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.databases
    WHERE database_id = DB_ID(N'WhatsBizERP_PROD')
      AND target_recovery_time_in_seconds = 60
)
    THROW 51908, N'Production database TARGET_RECOVERY_TIME must be 60 seconds.', 1;

IF OBJECT_ID(N'core.Tenants', N'U') IS NULL
   OR OBJECT_ID(N'integration.WhatsAppConfigurations', N'U') IS NULL
   OR OBJECT_ID(N'integration.WhatsAppPlatformConfiguration', N'U') IS NULL
   OR OBJECT_ID(N'purchase.Suppliers', N'U') IS NULL
    THROW 51901, N'Required production tenant, supplier, or WhatsApp schema is missing.', 1;

IF (SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped=0 AND is_disabled=0 AND name LIKE N'TR[_]%TenantGuard') < 12
    THROW 51902, N'Expected enabled operational tenant guard triggers are missing.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name=N'TR_Suppliers_TenantGuard' AND is_disabled=0)
    THROW 51903, N'The supplier tenant guard is missing or disabled.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppConfigurations') AND name=N'UX_WhatsAppConfigurations_PhoneNumberId' AND is_unique=1 AND is_disabled=0)
    THROW 51904, N'Tenant WhatsApp phone-number uniqueness is missing or disabled.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'purchase.Suppliers') AND name=N'UX_Suppliers_Name' AND is_unique=1 AND is_disabled=0)
    THROW 51905, N'Tenant-scoped active supplier-name uniqueness is missing or disabled.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'core.Users') AND name=N'CK_Users_AccountScope' AND is_disabled=0 AND is_not_trusted=0)
    THROW 51906, N'Application Owner / retailer account-scope constraint is missing or untrusted.', 1;

SELECT N'PRODUCTION_SCHEMA_VALIDATED' AS Result, DB_NAME() AS DatabaseName,
       (SELECT COUNT(*) FROM sys.triggers WHERE is_ms_shipped=0 AND is_disabled=0 AND name LIKE N'TR[_]%TenantGuard') AS EnabledTenantGuardCount;
