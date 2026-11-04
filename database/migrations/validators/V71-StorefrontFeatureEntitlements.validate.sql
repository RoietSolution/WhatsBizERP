SET NOCOUNT ON;
IF COL_LENGTH(N'integration.WhatsAppProductShareRequests',N'SentContent') IS NULL
    THROW 71010,N'V71 product-share content audit field is missing.',1;
IF (SELECT COUNT(*) FROM core.Features f JOIN core.Features p ON p.FeatureId=f.ParentFeatureId
    WHERE f.FeatureKey IN(N'STOREFRONT_SCHEDULED_SELLING',N'STOREFRONT_POPUP_CAMPAIGNS',N'STOREFRONT_NOTIFICATIONS') AND p.FeatureKey=N'V1' AND f.Version=N'V1' AND f.FeatureType=N'MODULE')<>3
    THROW 71011,N'V71 storefront catalog/parent definitions are incomplete.',1;
IF EXISTS(SELECT 1 FROM core.Plans p CROSS JOIN core.Features f WHERE f.FeatureKey IN(N'STOREFRONT_SCHEDULED_SELLING',N'STOREFRONT_POPUP_CAMPAIGNS',N'STOREFRONT_NOTIFICATIONS')
    AND NOT EXISTS(SELECT 1 FROM core.PlanFeatures pf WHERE pf.PlanId=p.PlanId AND pf.FeatureId=f.FeatureId))
    THROW 71012,N'V71 storefront plan mappings are incomplete.',1;
IF EXISTS(SELECT 1 FROM core.Tenants t CROSS JOIN core.Features f WHERE f.FeatureKey IN(N'STOREFRONT_SCHEDULED_SELLING',N'STOREFRONT_POPUP_CAMPAIGNS',N'STOREFRONT_NOTIFICATIONS')
    AND NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=t.TenantId AND tf.FeatureId=f.FeatureId))
    THROW 71013,N'V71 storefront tenant configuration defaults are incomplete.',1;
