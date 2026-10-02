SET NOCOUNT ON;

IF OBJECT_ID(N'core.Tenants',N'U') IS NULL
   OR OBJECT_ID(N'master.Products',N'U') IS NULL
   OR OBJECT_ID(N'sales.Customers',N'U') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontMedia',N'U') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontConfigurations',N'U') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontBanners',N'U') IS NULL
    THROW 52100,N'Historical V40 baseline verification failed: a required predecessor table is missing.',1;

IF COL_LENGTH(N'sales.Customers',N'MobileNormalized') IS NULL
   OR COL_LENGTH(N'master.Products',N'PackSize') IS NULL
   OR COL_LENGTH(N'commerce.StorefrontConfigurations',N'AllCategoryMediaId') IS NULL
    THROW 52101,N'Historical V40 baseline verification failed: a required V39/V40 column is missing.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.columns c
    WHERE c.object_id=OBJECT_ID(N'commerce.StorefrontConfigurations')
      AND c.name=N'AllCategoryMediaId' AND c.system_type_id=36 AND c.is_nullable=1
)
    THROW 52102,N'Historical V40 baseline verification failed: AllCategoryMediaId has an unexpected definition.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys fk
    WHERE fk.parent_object_id=OBJECT_ID(N'commerce.StorefrontConfigurations')
      AND fk.name=N'FK_StorefrontConfigurations_AllCategoryMedia'
      AND fk.is_disabled=0 AND fk.is_not_trusted=0
      AND fk.referenced_object_id=OBJECT_ID(N'commerce.StorefrontMedia')
)
    THROW 52103,N'Historical V40 baseline verification failed: the All-category media FK is missing or invalid.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys fk
    WHERE fk.parent_object_id=OBJECT_ID(N'commerce.StorefrontConfigurations')
      AND fk.name=N'FK_StorefrontConfigurations_AllCategoryMedia'
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id=fk.object_id)=2
      AND EXISTS(SELECT 1 FROM sys.foreign_key_columns fkc JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id WHERE fkc.constraint_object_id=fk.object_id AND pc.name=N'TenantId' AND rc.name=N'TenantId')
      AND EXISTS(SELECT 1 FROM sys.foreign_key_columns fkc JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id WHERE fkc.constraint_object_id=fk.object_id AND pc.name=N'AllCategoryMediaId' AND rc.name=N'MediaId')
)
    THROW 52108,N'Historical V40 baseline verification failed: the All-category media FK does not preserve tenant-scoped key mapping.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'sales.Customers') AND i.name=N'UX_Customers_TenantMobileNormalized'
      AND i.is_unique=1 AND i.has_filter=1 AND i.is_disabled=0
      AND i.filter_definition LIKE N'%MobileNormalized%'
)
    THROW 52109,N'Historical V40 baseline verification failed: tenant-scoped normalized-mobile unique index is missing or invalid.',1;

IF EXISTS(SELECT 1 FROM sys.triggers WHERE object_id=OBJECT_ID(N'core.TR_Users_ResourceCapacity') AND is_disabled=1)
   OR OBJECT_ID(N'core.TR_Users_ResourceCapacity',N'TR') IS NULL
    THROW 52115,N'Historical V40 baseline verification failed: V37 resource-capacity trigger is missing or disabled.',1;
IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints cc
    WHERE cc.parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia')
      AND cc.name=N'CK_StorefrontMedia_Resource'
      AND cc.is_disabled=0 AND cc.is_not_trusted=0
      AND cc.definition LIKE N'%category-all%'
)
    THROW 52104,N'Historical V40 baseline verification failed: category-all media is not allowed by its trusted constraint.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints cc
    WHERE cc.parent_object_id=OBJECT_ID(N'inventory.InventoryTransactions')
      AND cc.name=N'CK_InventoryTransactions_Type'
      AND cc.is_disabled=0 AND cc.is_not_trusted=0
      AND cc.definition LIKE N'%SALE_REVERSAL%'
)
    THROW 52105,N'Historical V40 baseline verification failed: SALE_REVERSAL inventory support is missing or invalid.',1;

IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'DeliveryEnabled') IS NULL
   OR COL_LENGTH(N'commerce.StorefrontConfigurations',N'ShowProductReviews') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontOtpChallenges',N'U') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontWishlistItems',N'U') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontProductReviews',N'U') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontPromotions',N'U') IS NULL
   OR OBJECT_ID(N'commerce.StorefrontRefunds',N'U') IS NULL
   OR OBJECT_ID(N'sales.FullSaleReversals',N'U') IS NULL
    THROW 52106,N'Historical V40 baseline verification failed: a required V39 Storefront object is missing.',1;

IF OBJECT_ID(N'finance.PostSource',N'P') IS NULL
   OR OBJECT_ID(N'sales.POS_AddPayment',N'P') IS NULL
   OR OBJECT_ID(N'sales.POS_PostInvoice',N'P') IS NULL
   OR OBJECT_ID(N'sales.POS_ReverseFullSale',N'P') IS NULL
   OR OBJECT_ID(N'finance.SettleStorefrontRefund',N'P') IS NULL
    THROW 52107,N'Historical V40 baseline verification failed: a required finance/sales procedure is missing.',1;

SELECT N'V40_BASELINE_VALID' AS ValidationResult;

