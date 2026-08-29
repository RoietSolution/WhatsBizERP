SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF SCHEMA_ID(N'integration') IS NULL EXEC(N'CREATE SCHEMA integration');

IF OBJECT_ID(N'integration.WhatsAppConfigurations', N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppConfigurations
    (
        WhatsAppConfigurationId uniqueidentifier NOT NULL CONSTRAINT PK_WhatsAppConfigurations PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        WhatsAppBusinessAccountId nvarchar(50) NULL,
        PhoneNumberId nvarchar(50) NULL,
        DisplayPhoneNumber nvarchar(50) NULL,
        BusinessDisplayName nvarchar(250) NULL,
        AccessTokenProtected nvarchar(max) NULL,
        WebhookVerifyTokenProtected nvarchar(max) NULL,
        AppSecretProtected nvarchar(max) NULL,
        ApiVersion nvarchar(20) NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_WhatsAppConfigurations_IsEnabled DEFAULT(0),
        ConnectionStatus nvarchar(30) NOT NULL CONSTRAINT DF_WhatsAppConfigurations_Status DEFAULT(N'NOT_CONFIGURED'),
        LastValidatedOn datetimeoffset NULL,
        LastError nvarchar(1000) NULL,
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppConfigurations_CreatedOn DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(256) NULL,
        ModifiedOn datetimeoffset NULL,
        ModifiedBy nvarchar(256) NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT UQ_WhatsAppConfigurations_Tenant UNIQUE(TenantId),
        CONSTRAINT FK_WhatsAppConfigurations_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_WhatsAppConfigurations_Status CHECK(ConnectionStatus IN (N'NOT_CONFIGURED',N'CONFIGURED',N'CONNECTED',N'ERROR',N'DISABLED')),
        CONSTRAINT CK_WhatsAppConfigurations_ApiVersion CHECK(ApiVersion LIKE N'v[0-9]%.%')
    );
    CREATE UNIQUE INDEX UX_WhatsAppConfigurations_PhoneNumberId ON integration.WhatsAppConfigurations(PhoneNumberId) WHERE PhoneNumberId IS NOT NULL;
END;

COMMIT TRANSACTION;
