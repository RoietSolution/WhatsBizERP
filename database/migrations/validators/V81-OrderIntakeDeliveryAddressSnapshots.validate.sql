SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.OrderIntakeRequests',N'U') IS NULL
   OR OBJECT_ID(N'commerce.OrderIntakeQuotes',N'U') IS NULL
   OR OBJECT_ID(N'commerce.IntakeSalesOrders',N'U') IS NULL
    THROW 81010,N'V79/V80 order-intake prerequisites are missing.',1;
IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'DeliveryAddressDraftJson') IS NULL
   OR COL_LENGTH(N'commerce.OrderIntakeQuotes',N'DeliveryAddressSnapshotJson') IS NULL
   OR COL_LENGTH(N'commerce.IntakeSalesOrders',N'DeliveryAddressSnapshotJson') IS NULL
    THROW 81011,N'V81 delivery-address snapshot columns are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'CK_OrderIntakeRequests_DeliveryAddressJson')
   OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeQuotes') AND name=N'CK_OrderIntakeQuotes_DeliveryAddressJson')
   OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'CK_IntakeSalesOrders_DeliveryAddressJson')
    THROW 81012,N'V81 delivery-address JSON constraints are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeQuotes') AND name=N'CK_OrderIntakeQuotes_DeliveryAddressRequired' AND is_disabled=0 AND is_not_trusted=0)
   OR NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'CK_IntakeSalesOrders_DeliveryAddressRequired' AND is_disabled=0 AND is_not_trusted=0)
    THROW 81014,N'V81 trusted delivery-address requirements are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeQuotes') AND name=N'FK_OrderIntakeQuotes_Request' AND is_disabled=0)
   OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'FK_IntakeSalesOrders_Request' AND is_disabled=0)
   OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.IntakeSalesOrders') AND name=N'FK_IntakeSalesOrders_Quote' AND is_disabled=0)
    THROW 81016,N'V81 tenant-scoped quote and Sales Order foreign keys are missing or disabled.',1;
IF EXISTS(SELECT 1 FROM commerce.OrderIntakeRequests WHERE DeliveryAddressDraftJson IS NOT NULL AND ISJSON(DeliveryAddressDraftJson)<>1)
   OR EXISTS(SELECT 1 FROM commerce.OrderIntakeQuotes WHERE DeliveryAddressSnapshotJson IS NOT NULL AND ISJSON(DeliveryAddressSnapshotJson)<>1)
   OR EXISTS(SELECT 1 FROM commerce.IntakeSalesOrders WHERE DeliveryAddressSnapshotJson IS NOT NULL AND ISJSON(DeliveryAddressSnapshotJson)<>1)
    THROW 81013,N'V81 found an invalid delivery-address snapshot.',1;
IF EXISTS
(
    SELECT 1 FROM commerce.OrderIntakeQuotes
    WHERE FulfilmentPreference='DELIVERY' AND
      (DeliveryAddressSnapshotJson IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.RecipientName'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Mobile'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.HouseStreet'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Locality'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.City'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.State'))),N'') IS NULL
       OR JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') IS NULL
       OR LEN(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))<>6
       OR JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') LIKE '%[^0-9]%'
       OR Pincode IS NULL
       OR RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))<>RTRIM(Pincode))
)
   OR EXISTS
(
    SELECT 1 FROM commerce.IntakeSalesOrders
    WHERE FulfilmentPreference='DELIVERY' AND
      (DeliveryAddressSnapshotJson IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.RecipientName'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Mobile'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.HouseStreet'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Locality'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.City'))),N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.State'))),N'') IS NULL
       OR JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') IS NULL
       OR LEN(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))<>6
       OR JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode') LIKE '%[^0-9]%'
       OR Pincode IS NULL
       OR RTRIM(JSON_VALUE(DeliveryAddressSnapshotJson,'$.Pincode'))<>RTRIM(Pincode))
)
    THROW 81015,N'V81 found a delivery quote or Sales Order without a complete matching address snapshot.',1;
SELECT N'V81 delivery address draft and immutable snapshots validated.' AS ValidationResult;
