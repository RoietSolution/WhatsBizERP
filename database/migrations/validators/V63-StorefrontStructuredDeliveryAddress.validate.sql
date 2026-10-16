IF OBJECT_ID(N'integration.WhatsAppCommerceOrders', N'U') IS NULL
    THROW 63101, N'V63 validation failed: Storefront order table is missing.', 1;

IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'DeliveryRecipientName',250),(N'DeliveryMobile',20),(N'DeliveryAddressLine1',500),
        (N'DeliveryAddressLine2',500),(N'DeliveryLandmark',250),(N'DeliveryCity',150),
        (N'DeliveryDistrict',150),(N'DeliveryState',100),(N'DeliveryCountry',100)
    ) r(Name, MaxLength)
    LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'integration.WhatsAppCommerceOrders') AND c.name=r.Name
    LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.column_id IS NULL OR t.name NOT IN (N'nvarchar',N'nchar') OR c.max_length < r.MaxLength*2
)
    THROW 63102, N'V63 validation failed: structured delivery address columns are missing or undersized.', 1;
