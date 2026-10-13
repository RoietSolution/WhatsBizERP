/* V51 - tenant-controlled product visibility on shipping labels. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'ShowProductDetailsOnShippingLabel') IS NULL
BEGIN
    ALTER TABLE commerce.StorefrontConfigurations ADD ShowProductDetailsOnShippingLabel bit NOT NULL
        CONSTRAINT DF_StorefrontConfigurations_ShowProductDetailsOnShippingLabel DEFAULT(1);
END;
