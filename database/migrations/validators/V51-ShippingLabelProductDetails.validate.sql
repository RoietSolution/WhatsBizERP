IF COL_LENGTH(N'commerce.StorefrontConfigurations', N'ShowProductDetailsOnShippingLabel') IS NULL
    THROW 51051, 'V51 validation failed: shipping label product visibility column is missing.', 1;
IF EXISTS (SELECT 1 FROM commerce.StorefrontConfigurations WHERE ShowProductDetailsOnShippingLabel IS NULL)
    THROW 51052, 'V51 validation failed: shipping label product visibility contains NULL.', 1;
