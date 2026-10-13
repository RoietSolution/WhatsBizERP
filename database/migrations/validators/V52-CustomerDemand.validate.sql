IF OBJECT_ID(N'commerce.ProductSearchEvents',N'U') IS NULL OR OBJECT_ID(N'commerce.ProductAvailabilityRequests',N'U') IS NULL OR OBJECT_ID(N'commerce.MissingProductRequests',N'U') IS NULL
    THROW 51052, 'V52 validation failed: customer demand tables are missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ProductAvailabilityRequests_Active' AND object_id=OBJECT_ID(N'commerce.ProductAvailabilityRequests'))
    THROW 51053, 'V52 validation failed: active availability request uniqueness is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_MissingProductRequests_Active' AND object_id=OBJECT_ID(N'commerce.MissingProductRequests'))
    THROW 51055, 'V52 validation failed: active missing-product request uniqueness is missing.', 1;
IF EXISTS (SELECT 1 FROM commerce.ProductSearchEvents WHERE ResultCount<0 OR InStockResultCount<0 OR OutOfStockResultCount<0 OR InStockResultCount>ResultCount OR OutOfStockResultCount>ResultCount)
    THROW 51054, 'V52 validation failed: invalid search result counts exist.', 1;
