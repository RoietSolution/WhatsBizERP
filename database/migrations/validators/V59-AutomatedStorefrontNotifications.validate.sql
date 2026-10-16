IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'NewOrderEmailNotificationsEnabled') IS NULL
 OR COL_LENGTH(N'commerce.StorefrontConfigurations',N'NewOrderNotificationEmail') IS NULL
 OR COL_LENGTH(N'commerce.StorefrontConfigurations',N'RestockWhatsAppNotificationsEnabled') IS NULL
 OR COL_LENGTH(N'commerce.StorefrontConfigurations',N'RestockWhatsAppTemplateName') IS NULL
    THROW 59001,N'V59 storefront notification configuration columns are missing.',1;
IF OBJECT_ID(N'commerce.StorefrontAutomatedNotificationDeliveries',N'U') IS NULL
    THROW 59002,N'V59 automated notification delivery table is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontAutomatedNotificationDeliveries_Event' AND object_id=OBJECT_ID(N'commerce.StorefrontAutomatedNotificationDeliveries'))
    THROW 59003,N'V59 automated notification idempotency index is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontAutomatedNotificationDeliveries_Work' AND object_id=OBJECT_ID(N'commerce.StorefrontAutomatedNotificationDeliveries'))
    THROW 59004,N'V59 automated notification work index is missing.',1;
