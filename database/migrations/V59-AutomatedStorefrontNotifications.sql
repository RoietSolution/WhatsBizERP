/* V59: tenant-scoped storefront restock and retailer order notification outbox. */
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'NewOrderEmailNotificationsEnabled') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD NewOrderEmailNotificationsEnabled bit NOT NULL
        CONSTRAINT DF_StorefrontConfigurations_NewOrderEmailNotificationsEnabled DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'NewOrderNotificationEmail') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD NewOrderNotificationEmail nvarchar(256) NULL;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'RestockWhatsAppNotificationsEnabled') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD RestockWhatsAppNotificationsEnabled bit NOT NULL
        CONSTRAINT DF_StorefrontConfigurations_RestockWhatsAppNotificationsEnabled DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'RestockWhatsAppTemplateName') IS NULL
    ALTER TABLE commerce.StorefrontConfigurations ADD RestockWhatsAppTemplateName nvarchar(100) NULL;

IF OBJECT_ID(N'commerce.StorefrontAutomatedNotificationDeliveries', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontAutomatedNotificationDeliveries
    (
        DeliveryId uniqueidentifier NOT NULL CONSTRAINT DF_StorefrontAutomatedNotificationDeliveries_Id DEFAULT NEWSEQUENTIALID(),
        TenantId uniqueidentifier NOT NULL,
        EventType nvarchar(40) NOT NULL,
        Channel nvarchar(12) NOT NULL,
        EntityId uniqueidentifier NOT NULL,
        RecipientReference nvarchar(320) NULL,
        TemplateKey nvarchar(100) NULL,
        Status nvarchar(12) NOT NULL CONSTRAINT DF_StorefrontAutomatedNotificationDeliveries_Status DEFAULT N'PENDING',
        AttemptCount int NOT NULL CONSTRAINT DF_StorefrontAutomatedNotificationDeliveries_Attempts DEFAULT 0,
        NextAttemptAtUtc datetimeoffset NULL,
        LeaseUntilUtc datetimeoffset NULL,
        LastAttemptAtUtc datetimeoffset NULL,
        SentAtUtc datetimeoffset NULL,
        ProviderMessageReference nvarchar(200) NULL,
        ErrorCategory nvarchar(100) NULL,
        ErrorMessage nvarchar(500) NULL,
        CreatedAtUtc datetimeoffset NOT NULL CONSTRAINT DF_StorefrontAutomatedNotificationDeliveries_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_StorefrontAutomatedNotificationDeliveries PRIMARY KEY(DeliveryId),
        CONSTRAINT CK_StorefrontAutomatedNotificationDeliveries_Channel CHECK(Channel IN(N'EMAIL',N'WHATSAPP')),
        CONSTRAINT CK_StorefrontAutomatedNotificationDeliveries_Status CHECK(Status IN(N'PENDING',N'PROCESSING',N'SENT',N'FAILED',N'SKIPPED'))
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontAutomatedNotificationDeliveries_Event' AND object_id=OBJECT_ID(N'commerce.StorefrontAutomatedNotificationDeliveries'))
    CREATE UNIQUE INDEX UX_StorefrontAutomatedNotificationDeliveries_Event ON commerce.StorefrontAutomatedNotificationDeliveries(TenantId,EventType,Channel,EntityId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontAutomatedNotificationDeliveries_Work' AND object_id=OBJECT_ID(N'commerce.StorefrontAutomatedNotificationDeliveries'))
    CREATE INDEX IX_StorefrontAutomatedNotificationDeliveries_Work ON commerce.StorefrontAutomatedNotificationDeliveries(Status,NextAttemptAtUtc,LeaseUntilUtc,CreatedAtUtc);
