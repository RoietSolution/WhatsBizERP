SET NOCOUNT ON;

IF OBJECT_ID(N'sales.POS_PostInvoice', N'P') IS NULL
    THROW 65100, N'V65 validation failed: sales.POS_PostInvoice is missing.', 1;

DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'sales.POS_PostInvoice', N'P'));
IF @definition IS NULL
   OR CHARINDEX(N'GstPricingType', @definition) = 0
   OR CHARINDEX(N'@AdditionalTax', @definition) = 0
   OR CHARINDEX(N'GstPricingType=N''INCLUSIVE''', @definition) = 0
    THROW 65101, N'V65 validation failed: persisted GST pricing-mode logic is missing from sales.POS_PostInvoice.', 1;

SELECT N'V65_POS_PRODUCT_GST_PRICING_TYPE_VALID' AS ValidationResult;
