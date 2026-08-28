SET NOCOUNT ON;
IF DB_NAME()<>N'WhatsBizERP_QA' THROW 51270,'Validate_QA_Bootstrap.sql may run only in WhatsBizERP_QA.',1;
DECLARE @TenantId uniqueidentifier=(SELECT TenantId FROM core.Tenants WHERE TenantKey=N'QA_DEFAULT' AND IsActive=1);
IF @TenantId IS NULL THROW 51271,'Active QA_DEFAULT tenant is missing.',1;
IF (SELECT COUNT(*) FROM core.Tenants WHERE TenantKey=N'QA_DEFAULT')<>1 THROW 51272,'QA_DEFAULT tenant is duplicated.',1;
IF (SELECT COUNT(*) FROM core.Subscriptions s JOIN core.Plans p ON p.PlanId=s.PlanId WHERE s.TenantId=@TenantId AND s.IsActive=1 AND p.PlanKey=N'V2_COMMERCE')<>1 THROW 51273,'Exactly one active V2_COMMERCE subscription is required.',1;
IF EXISTS
(
 SELECT 1 FROM (VALUES(N'V1'),(N'V2'),(N'PRODUCTS'),(N'INVENTORY'),(N'POS'),(N'CUSTOMERS'),(N'WHATSAPP_COMMERCE'),(N'WHATSAPP_CONFIGURATION'),(N'WHATSAPP_COMMERCE_DEMO'),(N'COMMERCE_PRODUCT_SEARCH'),(N'COMMERCE_COLLECTIONS'),(N'COMMERCE_ORDERS')) required(FeatureKey)
 LEFT JOIN core.Features f ON f.FeatureKey=required.FeatureKey AND f.IsActive=1
 LEFT JOIN core.TenantFeatures tf ON tf.FeatureId=f.FeatureId AND tf.TenantId=@TenantId AND tf.IsActive=1 AND tf.IsEnabled=1
 LEFT JOIN core.Subscriptions s ON s.TenantId=@TenantId AND s.IsActive=1 AND (s.EndDate IS NULL OR s.EndDate>=SYSUTCDATETIME())
 LEFT JOIN core.PlanFeatures pf ON pf.PlanId=s.PlanId AND pf.FeatureId=f.FeatureId AND pf.IsEnabled=1
 WHERE f.FeatureId IS NULL OR tf.TenantFeatureId IS NULL OR pf.PlanFeatureId IS NULL
) THROW 51274,'A required V1/WhatsApp Commerce effective feature is unavailable.',1;
IF NOT EXISTS(SELECT 1 FROM admin.Companies WHERE CompanyCode=N'QA' AND IsActive=1) THROW 51275,'QA company is missing.',1;
IF NOT EXISTS(SELECT 1 FROM inventory.Warehouses WHERE WarehouseCode=N'QA-MAIN' AND IsActive=1 AND IsDeleted=0) THROW 51276,'QA warehouse is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sales.POSCounters WHERE CounterCode=N'QA-POS-01' AND IsActive=1) THROW 51277,'QA POS counter is missing.',1;
IF NOT EXISTS(SELECT 1 FROM master.Products p JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId WHERE p.TenantId=@TenantId AND p.ProductCode=N'QA-PROD-001' AND b.QuantityAvailable>0) THROW 51278,'QA stocked tenant product is missing.',1;
IF NOT EXISTS(SELECT 1 FROM sales.Customers WHERE TenantId=@TenantId AND CustomerCode=N'QA-CUST-001' AND IsActive=1 AND IsDeleted=0) THROW 51279,'QA tenant customer is missing.',1;
IF NOT EXISTS(SELECT 1 FROM integration.WhatsAppConfigurations WHERE TenantId=@TenantId AND IsEnabled=1) THROW 51280,'QA WhatsApp provider configuration is missing.',1;
IF EXISTS(SELECT TenantId,FeatureId FROM core.TenantFeatures GROUP BY TenantId,FeatureId HAVING COUNT(*)>1) THROW 51281,'Duplicate tenant feature assignments exist.',1;

SELECT t.TenantKey,p.PlanKey,u.UserName,u.Email,c.ProviderMode,c.ConnectionStatus,
       (SELECT COUNT(*) FROM core.TenantFeatures tf WHERE tf.TenantId=@TenantId AND tf.IsActive=1 AND tf.IsEnabled=1) EnabledFeatures,
       (SELECT COUNT(*) FROM master.Products pr WHERE pr.TenantId=@TenantId AND pr.IsActive=1 AND pr.IsDeleted=0) ActiveProducts
FROM core.Tenants t
JOIN core.Subscriptions s ON s.TenantId=t.TenantId AND s.IsActive=1
JOIN core.Plans p ON p.PlanId=s.PlanId
LEFT JOIN core.Users u ON u.TenantId=t.TenantId AND u.NormalizedUserName=N'QA.ADMIN'
LEFT JOIN integration.WhatsAppConfigurations c ON c.TenantId=t.TenantId
WHERE t.TenantId=@TenantId;
PRINT 'QA bootstrap validation passed. UserName/Email remain NULL until the API Identity bootstrap has run.';
