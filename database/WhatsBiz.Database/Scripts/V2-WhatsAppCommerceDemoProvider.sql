SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'integration.WhatsAppConfigurations', N'ProviderMode') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD ProviderMode nvarchar(20) NOT NULL
        CONSTRAINT DF_WhatsAppConfigurations_ProviderMode DEFAULT(N'LIVE');

IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppConfigurations') AND name=N'UX_WhatsAppConfigurations_PhoneNumberId')
    DROP INDEX UX_WhatsAppConfigurations_PhoneNumberId ON integration.WhatsAppConfigurations;

ALTER TABLE integration.WhatsAppConfigurations ALTER COLUMN WhatsAppBusinessAccountId nvarchar(50) NULL;
ALTER TABLE integration.WhatsAppConfigurations ALTER COLUMN PhoneNumberId nvarchar(50) NULL;
ALTER TABLE integration.WhatsAppConfigurations ALTER COLUMN AccessTokenProtected nvarchar(max) NULL;
ALTER TABLE integration.WhatsAppConfigurations ALTER COLUMN WebhookVerifyTokenProtected nvarchar(max) NULL;
ALTER TABLE integration.WhatsAppConfigurations ALTER COLUMN AppSecretProtected nvarchar(max) NULL;
ALTER TABLE integration.WhatsAppConfigurations ALTER COLUMN ApiVersion nvarchar(20) NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppConfigurations') AND name=N'UX_WhatsAppConfigurations_PhoneNumberId')
    CREATE UNIQUE INDEX UX_WhatsAppConfigurations_PhoneNumberId
        ON integration.WhatsAppConfigurations(PhoneNumberId) WHERE PhoneNumberId IS NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'integration.WhatsAppConfigurations') AND name=N'CK_WhatsAppConfigurations_ProviderMode')
    EXEC(N'ALTER TABLE integration.WhatsAppConfigurations ADD CONSTRAINT CK_WhatsAppConfigurations_ProviderMode CHECK(ProviderMode IN(N''MOCK'',N''META_TEST'',N''LIVE''))');

IF OBJECT_ID(N'integration.WhatsAppCommerceOrders', N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppCommerceOrders
    (
        WhatsAppCommerceOrderId uniqueidentifier NOT NULL CONSTRAINT PK_WhatsAppCommerceOrders PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        InvoiceId uniqueidentifier NOT NULL,
        SourceChannel nvarchar(30) NOT NULL,
        ProviderMode nvarchar(20) NOT NULL,
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppCommerceOrders_CreatedOn DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(256) NULL,
        CONSTRAINT UQ_WhatsAppCommerceOrders_Invoice UNIQUE(InvoiceId),
        CONSTRAINT FK_WhatsAppCommerceOrders_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_WhatsAppCommerceOrders_Invoices FOREIGN KEY(InvoiceId) REFERENCES sales.SalesInvoices(InvoiceId),
        CONSTRAINT CK_WhatsAppCommerceOrders_Source CHECK(SourceChannel IN(N'WHATSAPP_DEMO',N'WHATSAPP')),
        CONSTRAINT CK_WhatsAppCommerceOrders_Mode CHECK(ProviderMode IN(N'MOCK',N'META_TEST',N'LIVE'))
    );
    CREATE INDEX IX_WhatsAppCommerceOrders_TenantCreated ON integration.WhatsAppCommerceOrders(TenantId,CreatedOn DESC);
END;

COMMIT TRANSACTION;
