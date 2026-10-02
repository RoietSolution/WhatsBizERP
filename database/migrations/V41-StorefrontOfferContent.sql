SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'commerce.StorefrontPromotions', N'U') IS NULL OR OBJECT_ID(N'commerce.StorefrontBanners', N'U') IS NULL
    THROW 51000, 'V41 requires V38 storefront presentation and V39 storefront promotions.', 1;
GO

IF COL_LENGTH(N'commerce.StorefrontPromotions', N'ShortDescription') IS NULL
    ALTER TABLE commerce.StorefrontPromotions ADD ShortDescription nvarchar(300) NULL;
IF COL_LENGTH(N'commerce.StorefrontPromotions', N'DetailedDescription') IS NULL
    ALTER TABLE commerce.StorefrontPromotions ADD DetailedDescription nvarchar(max) NULL;
IF COL_LENGTH(N'commerce.StorefrontPromotions', N'TermsAndConditions') IS NULL
    ALTER TABLE commerce.StorefrontPromotions ADD TermsAndConditions nvarchar(max) NULL;
IF COL_LENGTH(N'commerce.StorefrontPromotions', N'PromoCode') IS NULL
    ALTER TABLE commerce.StorefrontPromotions ADD PromoCode nvarchar(50) NULL;
IF COL_LENGTH(N'commerce.StorefrontPromotions', N'CtaLabel') IS NULL
    ALTER TABLE commerce.StorefrontPromotions ADD CtaLabel nvarchar(40) NULL;
IF COL_LENGTH(N'commerce.StorefrontPromotions', N'EligibleItemsDescription') IS NULL
    ALTER TABLE commerce.StorefrontPromotions ADD EligibleItemsDescription nvarchar(500) NULL;
IF COL_LENGTH(N'commerce.StorefrontBanners', N'PromotionId') IS NULL
    ALTER TABLE commerce.StorefrontBanners ADD PromotionId uniqueidentifier NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_StorefrontBanners_Promotion' AND parent_object_id=OBJECT_ID(N'commerce.StorefrontBanners'))
    ALTER TABLE commerce.StorefrontBanners WITH CHECK ADD CONSTRAINT FK_StorefrontBanners_Promotion
        FOREIGN KEY(TenantId, PromotionId) REFERENCES commerce.StorefrontPromotions(TenantId, PromotionId);
GO
