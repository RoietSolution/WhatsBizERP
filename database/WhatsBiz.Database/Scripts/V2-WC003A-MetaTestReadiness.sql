SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF COL_LENGTH(N'integration.WhatsAppConfigurations',N'TestRecipientNumber') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD TestRecipientNumber nvarchar(30) NULL;
IF COL_LENGTH(N'integration.WhatsAppConfigurations',N'LastWebhookVerifiedOn') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD LastWebhookVerifiedOn datetimeoffset NULL;
IF COL_LENGTH(N'integration.WhatsAppConfigurations',N'LastWebhookReceivedOn') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD LastWebhookReceivedOn datetimeoffset NULL;
IF COL_LENGTH(N'integration.WhatsAppConfigurations',N'LastWebhookEventType') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD LastWebhookEventType nvarchar(40) NULL;
IF COL_LENGTH(N'integration.WhatsAppConfigurations',N'LastWebhookMetaMessageId') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD LastWebhookMetaMessageId nvarchar(250) NULL;
IF COL_LENGTH(N'integration.WhatsAppConfigurations',N'DuplicateWebhookCount') IS NULL
    ALTER TABLE integration.WhatsAppConfigurations ADD DuplicateWebhookCount bigint NOT NULL
        CONSTRAINT DF_WhatsAppConfigurations_DuplicateWebhookCount DEFAULT(0) WITH VALUES;

COMMIT TRANSACTION;
