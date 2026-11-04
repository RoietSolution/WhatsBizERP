/* V64: make product selling-price GST semantics explicit for ERP and Storefront pricing. */
SET XACT_ABORT ON;

IF OBJECT_ID(N'master.Products', N'U') IS NULL
    THROW 64001, N'master.Products is required before V64.', 1;

IF COL_LENGTH(N'master.Products', N'GstPricingType') IS NULL
BEGIN
    ALTER TABLE master.Products
        ADD GstPricingType nvarchar(10) NOT NULL
            CONSTRAINT DF_Products_GstPricingType DEFAULT N'EXCLUSIVE' WITH VALUES;
END;
GO

UPDATE master.Products
SET GstPricingType = N'EXCLUSIVE'
WHERE GstPricingType IS NULL OR LTRIM(RTRIM(GstPricingType)) = N'';

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'master.Products')
      AND name = N'CK_Products_GstPricingType'
)
    ALTER TABLE master.Products
        ADD CONSTRAINT CK_Products_GstPricingType
        CHECK (GstPricingType IN (N'INCLUSIVE', N'EXCLUSIVE'));
