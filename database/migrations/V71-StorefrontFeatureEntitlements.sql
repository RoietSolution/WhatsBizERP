/* Existing feature catalog only. Preserve owner overrides and saved storefront configuration. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'integration.WhatsAppProductShareRequests',N'U') IS NULL
    THROW 71000,N'V71 requires V70 product sharing.',1;
IF OBJECT_ID(N'commerce.StorefrontAvailabilitySchedules',N'U') IS NULL OR OBJECT_ID(N'commerce.StorefrontPopupCampaigns',N'U') IS NULL
    THROW 71002,N'V71 requires V67 scheduled selling and V69 popup campaign schemas.',1;
IF COL_LENGTH(N'integration.WhatsAppProductShareRequests',N'SentContent') IS NULL
    ALTER TABLE integration.WhatsAppProductShareRequests ADD SentContent nvarchar(max) NULL;
GO
DECLARE @parent uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'V1');
IF @parent IS NULL OR NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'ADMINISTRATION') OR NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'PRODUCTS')
    THROW 71001,N'V71 requires the existing V1, Administration and Products feature definitions.',1;
DECLARE @catalog TABLE(FeatureKey nvarchar(100),Name nvarchar(150),Description nvarchar(500),SortOrder int);
INSERT @catalog VALUES
 (N'STOREFRONT_SCHEDULED_SELLING',N'Scheduled Selling Windows',N'Tenant booking windows; disabling suspends schedule restrictions without deleting schedules.',85),
 (N'STOREFRONT_POPUP_CAMPAIGNS',N'Storefront Popup Campaigns',N'Optional promotional storefront popups; saved campaigns are retained when disabled.',86),
 (N'STOREFRONT_NOTIFICATIONS',N'Storefront Notifications',N'Optional restock messages and retailer alerts only; essential transactional messages are unaffected.',87);
INSERT core.Features(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,ParentFeatureId,Version,SortOrder,CreatedBy)
SELECT NEWID(),c.FeatureKey,c.Name,c.Description,N'ADMINISTRATION',N'ACTIVE',1,N'MODULE',@parent,N'V1',c.SortOrder,N'V71'
FROM @catalog c WHERE NOT EXISTS(SELECT 1 FROM core.Features f WHERE f.FeatureKey=c.FeatureKey);

-- Eligible plans inherit existing Administration + Products capability. Never override an existing plan decision.
INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy)
SELECT NEWID(),p.PlanId,f.FeatureId,CONVERT(bit,CASE WHEN
 EXISTS(SELECT 1 FROM core.PlanFeatures pf JOIN core.Features x ON x.FeatureId=pf.FeatureId WHERE pf.PlanId=p.PlanId AND x.FeatureKey=N'ADMINISTRATION' AND pf.IsEnabled=1)
 AND EXISTS(SELECT 1 FROM core.PlanFeatures pf JOIN core.Features x ON x.FeatureId=pf.FeatureId WHERE pf.PlanId=p.PlanId AND x.FeatureKey=N'PRODUCTS' AND pf.IsEnabled=1) THEN 1 ELSE 0 END),N'V71'
FROM core.Plans p CROSS JOIN core.Features f JOIN @catalog c ON c.FeatureKey=f.FeatureKey
WHERE NOT EXISTS(SELECT 1 FROM core.PlanFeatures pf WHERE pf.PlanId=p.PlanId AND pf.FeatureId=f.FeatureId);

-- Preserve already-used configurations on upgrade; otherwise owner opt-in. Existing overrides are never rewritten.
INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy)
SELECT NEWID(),t.TenantId,f.FeatureId,CONVERT(bit,CASE
 WHEN f.FeatureKey=N'STOREFRONT_SCHEDULED_SELLING' AND EXISTS(SELECT 1 FROM commerce.StorefrontAvailabilitySchedules s WHERE s.TenantId=t.TenantId AND s.IsEnabled=1
    AND (EXISTS(SELECT 1 FROM commerce.StorefrontAvailabilityScheduleProducts p WHERE p.TenantId=s.TenantId AND p.ScheduleId=s.ScheduleId)
      OR EXISTS(SELECT 1 FROM commerce.StorefrontAvailabilityScheduleCategories c WHERE c.TenantId=s.TenantId AND c.ScheduleId=s.ScheduleId))) THEN 1
 WHEN f.FeatureKey=N'STOREFRONT_POPUP_CAMPAIGNS' AND EXISTS(SELECT 1 FROM commerce.StorefrontPopupCampaigns p WHERE p.TenantId=t.TenantId AND p.IsActive=1) THEN 1
 WHEN f.FeatureKey=N'STOREFRONT_NOTIFICATIONS' AND EXISTS(SELECT 1 FROM commerce.StorefrontConfigurations sc WHERE sc.TenantId=t.TenantId AND (sc.NewOrderEmailNotificationsEnabled=1 OR sc.RestockWhatsAppNotificationsEnabled=1)) THEN 1
 ELSE 0 END),N'V71: preserve active saved configuration; otherwise owner opt-in',1,N'V71'
FROM core.Tenants t CROSS JOIN core.Features f JOIN @catalog c ON c.FeatureKey=f.FeatureKey
WHERE NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=t.TenantId AND tf.FeatureId=f.FeatureId);
