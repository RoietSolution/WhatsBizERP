/* V58: prevent more than one active primary image per tenant/product. */
;WITH ranked AS
(
    SELECT ProductImageId,
           ROW_NUMBER() OVER
           (
               PARTITION BY TenantId, ProductId
               ORDER BY CASE WHEN IsPrimary=1 THEN 0 ELSE 1 END, CreatedOn, ProductImageId
           ) AS ImageRank
    FROM master.ProductImages
    WHERE IsActive=1 AND IsDeleted=0
)
UPDATE image
SET IsPrimary=0, ModifiedOn=SYSUTCDATETIME(), ModifiedBy=N'V58'
FROM master.ProductImages image
JOIN ranked ON ranked.ProductImageId=image.ProductImageId
WHERE ranked.ImageRank>1 AND image.IsPrimary=1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name=N'UX_ProductImages_Tenant_Product_Primary'
      AND object_id=OBJECT_ID(N'master.ProductImages')
)
    CREATE UNIQUE INDEX UX_ProductImages_Tenant_Product_Primary
    ON master.ProductImages(TenantId,ProductId)
    WHERE IsActive=1 AND IsDeleted=0 AND IsPrimary=1;
