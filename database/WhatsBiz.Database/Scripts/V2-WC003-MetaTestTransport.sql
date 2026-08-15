SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH(N'integration.WhatsAppConfigurations',N'MetaAppId') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD MetaAppId nvarchar(50) NULL;

IF OBJECT_ID(N'integration.WhatsAppWebhookEvents',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppWebhookEvents
    (
        WhatsAppWebhookEventId uniqueidentifier NOT NULL CONSTRAINT PK_WhatsAppWebhookEvents PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        ProviderMode nvarchar(20) NOT NULL,
        EventKey nvarchar(300) NOT NULL,
        MetaMessageId nvarchar(250) NULL,
        EventType nvarchar(40) NOT NULL,
        Direction nvarchar(20) NOT NULL,
        PhoneNumberId nvarchar(50) NULL,
        ContactNumber nvarchar(50) NULL,
        MessageType nvarchar(30) NULL,
        MessageStatus nvarchar(30) NULL,
        EventTimestamp datetimeoffset NOT NULL,
        ProcessingStatus nvarchar(30) NOT NULL,
        ReceivedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppWebhookEvents_ReceivedOn DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_WhatsAppWebhookEvents_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT UQ_WhatsAppWebhookEvents_TenantEvent UNIQUE(TenantId,EventKey),
        CONSTRAINT CK_WhatsAppWebhookEvents_Provider CHECK(ProviderMode IN(N'META_TEST',N'LIVE')),
        CONSTRAINT CK_WhatsAppWebhookEvents_Direction CHECK(Direction IN(N'INBOUND',N'OUTBOUND'))
    );
    CREATE INDEX IX_WhatsAppWebhookEvents_TenantReceived
        ON integration.WhatsAppWebhookEvents(TenantId,ReceivedOn DESC);
    CREATE INDEX IX_WhatsAppWebhookEvents_MetaMessage
        ON integration.WhatsAppWebhookEvents(MetaMessageId,EventType) WHERE MetaMessageId IS NOT NULL;
END;

COMMIT TRANSACTION;
