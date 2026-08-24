SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* One KhataDhari Meta app. Secrets are Data Protection ciphertext only. */
IF OBJECT_ID(N'integration.WhatsAppPlatformConfiguration',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppPlatformConfiguration
    (
        PlatformConfigurationId tinyint NOT NULL
            CONSTRAINT PK_WhatsAppPlatformConfiguration PRIMARY KEY
            CONSTRAINT CK_WhatsAppPlatformConfiguration_Singleton CHECK(PlatformConfigurationId=1),
        MetaAppId nvarchar(50) NOT NULL,
        AppSecretProtected nvarchar(max) NOT NULL,
        WebhookVerifyTokenProtected nvarchar(max) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_WhatsAppPlatformConfiguration_Enabled DEFAULT(0),
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppPlatformConfiguration_Created DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(256) NULL,
        ModifiedOn datetimeoffset NULL,
        ModifiedBy nvarchar(256) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_WhatsAppPlatformConfiguration_AppId CHECK(MetaAppId NOT LIKE N'%[^0-9]%')
    );
END;

/* A retailer owns one connection; a Meta phone and WABA cannot belong to two retailers. */
IF EXISTS
(
    SELECT PhoneNumberId FROM integration.WhatsAppConfigurations
    WHERE PhoneNumberId IS NOT NULL GROUP BY PhoneNumberId HAVING COUNT(*)>1
)
    THROW 51150,N'Duplicate WhatsApp Phone Number IDs must be resolved before Option 3 migration.',1;

IF EXISTS
(
    SELECT WhatsAppBusinessAccountId FROM integration.WhatsAppConfigurations
    WHERE WhatsAppBusinessAccountId IS NOT NULL GROUP BY WhatsAppBusinessAccountId HAVING COUNT(*)>1
)
    THROW 51151,N'Duplicate WhatsApp Business Account IDs must be resolved before Option 3 migration.',1;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppConfigurations') AND name=N'UX_WhatsAppConfigurations_PhoneNumberId')
    CREATE UNIQUE INDEX UX_WhatsAppConfigurations_PhoneNumberId
        ON integration.WhatsAppConfigurations(PhoneNumberId) WHERE PhoneNumberId IS NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppConfigurations') AND name=N'UX_WhatsAppConfigurations_WabaId')
    CREATE UNIQUE INDEX UX_WhatsAppConfigurations_WabaId
        ON integration.WhatsAppConfigurations(WhatsAppBusinessAccountId) WHERE WhatsAppBusinessAccountId IS NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppConfigurations') AND name=N'IX_WhatsAppConfigurations_WebhookResolution')
    CREATE INDEX IX_WhatsAppConfigurations_WebhookResolution
        ON integration.WhatsAppConfigurations(PhoneNumberId,WhatsAppBusinessAccountId,IsEnabled)
        INCLUDE(TenantId,ProviderMode,ConnectionStatus)
        WHERE PhoneNumberId IS NOT NULL;

COMMIT TRANSACTION;
