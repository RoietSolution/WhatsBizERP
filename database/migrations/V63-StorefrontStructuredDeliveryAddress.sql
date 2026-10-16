/* V63: preserve the complete delivery address selected at Storefront checkout. */
SET XACT_ABORT ON;

IF OBJECT_ID(N'integration.WhatsAppCommerceOrders', N'U') IS NULL
    THROW 63001, N'integration.WhatsAppCommerceOrders is required before V63.', 1;

IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryRecipientName') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryRecipientName nvarchar(250) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryMobile') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryMobile nvarchar(20) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryAddressLine1') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryAddressLine1 nvarchar(500) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryAddressLine2') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryAddressLine2 nvarchar(500) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryLandmark') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryLandmark nvarchar(250) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryCity') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryCity nvarchar(150) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryDistrict') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryDistrict nvarchar(150) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryState') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryState nvarchar(100) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders', N'DeliveryCountry') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryCountry nvarchar(100) NULL;
GO
