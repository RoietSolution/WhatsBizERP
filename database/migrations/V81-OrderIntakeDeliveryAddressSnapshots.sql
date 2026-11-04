/* V81: tenant-scoped delivery address draft and immutable quote/order address snapshots.
   Depends on V79 and V80. No customer address is linked by mobile number. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'commerce.OrderIntakeRequests',N'U') IS NULL
   OR OBJECT_ID(N'commerce.OrderIntakeQuotes',N'U') IS NULL
   OR OBJECT_ID(N'commerce.IntakeSalesOrders',N'U') IS NULL
    THROW 81000,N'V81 requires V79 and V80 Order Intake tables.',1;

BEGIN TRANSACTION;
IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'DeliveryAddressDraftJson') IS NULL
    ALTER TABLE commerce.OrderIntakeRequests ADD DeliveryAddressDraftJson nvarchar(max) NULL;
IF COL_LENGTH(N'commerce.OrderIntakeQuotes',N'DeliveryAddressSnapshotJson') IS NULL
    ALTER TABLE commerce.OrderIntakeQuotes ADD DeliveryAddressSnapshotJson nvarchar(max) NULL;
IF COL_LENGTH(N'commerce.IntakeSalesOrders',N'DeliveryAddressSnapshotJson') IS NULL
    ALTER TABLE commerce.IntakeSalesOrders ADD DeliveryAddressSnapshotJson nvarchar(max) NULL;

/* SQL Server compiles later statements in a batch before the conditional ALTERs run.
   Separate column creation from constraints; the canonical runner supports GO batches. */
GO

IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'CK_OrderIntakeRequests_DeliveryAddressJson')
    ALTER TABLE commerce.OrderIntakeRequests ADD CONSTRAINT CK_OrderIntakeRequests_DeliveryAddressJson
        CHECK(DeliveryAddressDraftJson IS NULL OR ISJSON(DeliveryAddressDraftJson)=1);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeQuotes') AND name=N'CK_OrderIntakeQuotes_DeliveryAddressJson')
    ALTER TABLE commerce.OrderIntakeQuotes ADD CONSTRAINT CK_OrderIntakeQuotes_DeliveryAddressJson
        CHECK(DeliveryAddressSnapshotJson IS NULL OR ISJSON(DeliveryAddressSnapshotJson)=1);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeQuotes') AND name=N'CK_OrderIntakeQuotes_DeliveryAddressRequired')
    ALTER TABLE commerce.OrderIntakeQuotes ADD CONSTRAINT CK_OrderIntakeQuotes_DeliveryAddressRequired CHECK
    (
        FulfilmentPreference='PICKUP' OR
        (DeliveryAddressSnapshotJson IS NOT NULL AND ISJSON(DeliveryAddressSnapshotJson)=1
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.RecipientName'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Mobile'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.HouseStreet'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Locality'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.City'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.State'))),N'') IS NOT NULL
         AND JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') IS NOT NULL
         AND LEN(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))=6
         AND JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') NOT LIKE '%[^0-9]%'
         AND Pincode IS NOT NULL
         AND RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))=RTRIM(Pincode))
    );
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'CK_IntakeSalesOrders_DeliveryAddressJson')
    ALTER TABLE commerce.IntakeSalesOrders ADD CONSTRAINT CK_IntakeSalesOrders_DeliveryAddressJson
        CHECK(DeliveryAddressSnapshotJson IS NULL OR ISJSON(DeliveryAddressSnapshotJson)=1);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'CK_IntakeSalesOrders_DeliveryAddressRequired')
    ALTER TABLE commerce.IntakeSalesOrders ADD CONSTRAINT CK_IntakeSalesOrders_DeliveryAddressRequired CHECK
    (
        FulfilmentPreference='PICKUP' OR
        (DeliveryAddressSnapshotJson IS NOT NULL AND ISJSON(DeliveryAddressSnapshotJson)=1
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.RecipientName'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Mobile'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.HouseStreet'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Locality'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.City'))),N'') IS NOT NULL
         AND NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.State'))),N'') IS NOT NULL
         AND JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') IS NOT NULL
         AND LEN(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))=6
         AND JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') NOT LIKE '%[^0-9]%'
         AND Pincode IS NOT NULL
         AND RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))=RTRIM(Pincode))
    );
COMMIT TRANSACTION;
