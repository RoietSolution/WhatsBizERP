SET NOCOUNT ON;

IF COL_LENGTH(N'master.Products', N'GstPricingType') IS NULL
    THROW 64100, N'V64 validation failed: GstPricingType is missing.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'master.Products')
      AND name = N'GstPricingType'
      AND (system_type_id <> 231 OR max_length <> 20 OR is_nullable <> 0)
)
    THROW 64101, N'V64 validation failed: GstPricingType has an unexpected definition.', 1;

IF EXISTS
(
    SELECT 1
    FROM master.Products
    WHERE GstPricingType NOT IN (N'INCLUSIVE', N'EXCLUSIVE')
)
    THROW 64102, N'V64 validation failed: an invalid GstPricingType value exists.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'master.Products')
      AND name = N'CK_Products_GstPricingType'
      AND is_disabled = 0
      AND is_not_trusted = 0
)
    THROW 64103, N'V64 validation failed: GstPricingType constraint is missing or untrusted.', 1;

SELECT N'V64_PRODUCT_GST_PRICING_TYPE_VALID' AS ValidationResult;
