SET XACT_ABORT ON;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'DeliveryAddress') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryAddress nvarchar(1000) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'FulfillmentMethod') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD FulfillmentMethod nvarchar(30) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'PaymentType') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD PaymentType nvarchar(20) NULL;
GO
