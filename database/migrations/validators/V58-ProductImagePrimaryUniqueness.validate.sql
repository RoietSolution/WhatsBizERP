IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name=N'UX_ProductImages_Tenant_Product_Primary'
      AND object_id=OBJECT_ID(N'master.ProductImages')
)
    THROW 58001,N'V58 product-image primary uniqueness index is missing.',1;

IF EXISTS
(
    SELECT TenantId,ProductId
    FROM master.ProductImages
    WHERE IsActive=1 AND IsDeleted=0 AND IsPrimary=1
    GROUP BY TenantId,ProductId
    HAVING COUNT(*)>1
)
    THROW 58002,N'V58 found duplicate active primary product images.',1;

SELECT N'V58 product-image primary uniqueness validated.' AS ValidationMessage;
