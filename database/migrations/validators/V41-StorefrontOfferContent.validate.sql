SET NOCOUNT ON;
IF DB_NAME() NOT IN(N'WhatsBizERP_QA',N'WhatsBizERP_PROD')
    THROW 52110,N'Unexpected database for V41 validation.',1;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.StorefrontPromotions') AND name=N'ShortDescription' AND system_type_id=231 AND max_length=600 AND is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.StorefrontPromotions') AND name=N'DetailedDescription' AND system_type_id=231 AND max_length=-1 AND is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.StorefrontPromotions') AND name=N'TermsAndConditions' AND system_type_id=231 AND max_length=-1 AND is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.StorefrontPromotions') AND name=N'PromoCode' AND system_type_id=231 AND max_length=100 AND is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.StorefrontPromotions') AND name=N'CtaLabel' AND system_type_id=231 AND max_length=80 AND is_nullable=1)
 OR NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.StorefrontPromotions') AND name=N'EligibleItemsDescription' AND system_type_id=231 AND max_length=1000 AND is_nullable=1)
    THROW 52111,N'V41 offer-content columns are missing or have unexpected definitions.',1;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'commerce.StorefrontBanners') AND name=N'PromotionId' AND system_type_id=36 AND is_nullable=1)
    THROW 52112,N'V41 StorefrontBanners.PromotionId must exist and be nullable.',1;

IF NOT EXISTS
(
 SELECT 1 FROM sys.foreign_keys fk
 WHERE fk.parent_object_id=OBJECT_ID(N'commerce.StorefrontBanners')
   AND fk.referenced_object_id=OBJECT_ID(N'commerce.StorefrontPromotions')
   AND fk.name=N'FK_StorefrontBanners_Promotion'
   AND fk.is_disabled=0 AND fk.is_not_trusted=0
   AND (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id=fk.object_id)=2
   AND EXISTS(SELECT 1 FROM sys.foreign_key_columns fkc JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id WHERE fkc.constraint_object_id=fk.object_id AND pc.name=N'TenantId' AND rc.name=N'TenantId')
   AND EXISTS(SELECT 1 FROM sys.foreign_key_columns fkc JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id WHERE fkc.constraint_object_id=fk.object_id AND pc.name=N'PromotionId' AND rc.name=N'PromotionId')
)
    THROW 52113,N'V41 tenant-scoped banner/promotion FK is missing, untrusted, disabled or incorrectly mapped.',1;

IF EXISTS
(
 SELECT 1 FROM commerce.StorefrontBanners b
 WHERE b.PromotionId IS NOT NULL
   AND NOT EXISTS(SELECT 1 FROM commerce.StorefrontPromotions p WHERE p.TenantId=b.TenantId AND p.PromotionId=b.PromotionId)
)
    THROW 52114,N'V41 banner/promotion tenant reference validation failed.',1;

SELECT N'V41_VALID' AS ValidationResult;

