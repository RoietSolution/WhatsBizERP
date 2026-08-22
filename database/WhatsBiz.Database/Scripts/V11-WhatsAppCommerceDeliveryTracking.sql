SET XACT_ABORT ON;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'DeliveryStatus') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveryStatus nvarchar(30) NOT NULL CONSTRAINT DF_WhatsAppCommerceOrders_DeliveryStatus DEFAULT(N'PENDING') WITH VALUES;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'CourierName') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD CourierName nvarchar(120) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'TrackingNumber') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD TrackingNumber nvarchar(120) NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'DispatchedOn') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DispatchedOn datetimeoffset NULL;
IF COL_LENGTH(N'integration.WhatsAppCommerceOrders',N'DeliveredOn') IS NULL
    ALTER TABLE integration.WhatsAppCommerceOrders ADD DeliveredOn datetimeoffset NULL;
GO
