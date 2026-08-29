SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'core.Features',N'FeatureType') IS NULL
    ALTER TABLE core.Features ADD FeatureType nvarchar(20) NOT NULL CONSTRAINT DF_Features_FeatureType DEFAULT(N'MODULE');
IF COL_LENGTH(N'core.Features',N'ParentFeatureId') IS NULL
    ALTER TABLE core.Features ADD ParentFeatureId uniqueidentifier NULL;
IF COL_LENGTH(N'core.Features',N'Version') IS NULL
    ALTER TABLE core.Features ADD Version nvarchar(20) NOT NULL CONSTRAINT DF_Features_Version DEFAULT(N'V2');
IF COL_LENGTH(N'core.Features',N'SortOrder') IS NULL
    ALTER TABLE core.Features ADD SortOrder int NOT NULL CONSTRAINT DF_Features_SortOrder DEFAULT(0);
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Features_FeatureType')
    ALTER TABLE core.Features WITH CHECK ADD CONSTRAINT CK_Features_FeatureType CHECK(FeatureType IN (N'VERSION',N'MODULE'));
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Features_ParentFeature')
    ALTER TABLE core.Features WITH CHECK ADD CONSTRAINT FK_Features_ParentFeature FOREIGN KEY(ParentFeatureId) REFERENCES core.Features(FeatureId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'core.Features') AND name=N'IX_Features_Parent_Sort')
    CREATE INDEX IX_Features_Parent_Sort ON core.Features(ParentFeatureId,SortOrder) INCLUDE(FeatureKey,FeatureType,Version,IsActive);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'core.TenantFeatures') AND name=N'IX_TenantFeatures_Tenant_Active')
    CREATE INDEX IX_TenantFeatures_Tenant_Active ON core.TenantFeatures(TenantId,IsActive,FeatureId) INCLUDE(IsEnabled,StartDate,EndDate,ModifiedOn);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'core.Subscriptions') AND name=N'IX_Subscriptions_Tenant_Active')
    CREATE INDEX IX_Subscriptions_Tenant_Active ON core.Subscriptions(TenantId,IsActive,StartDate,EndDate) INCLUDE(PlanId);

DECLARE @Features TABLE(FeatureKey nvarchar(100),Name nvarchar(200),Description nvarchar(1000),FeatureType nvarchar(20),ParentKey nvarchar(100),Version nvarchar(20),SortOrder int,ModuleKey nvarchar(100));
INSERT @Features VALUES
(N'V1',N'V1 — Core Retail ERP',N'Core retail ERP platform.',N'VERSION',NULL,N'V1',0,N'VERSION'),
(N'DASHBOARD',N'Dashboard',N'ERP dashboard and operational summaries.',N'MODULE',N'V1',N'V1',10,N'DASHBOARD'),
(N'PRODUCTS',N'Products',N'Product catalog and master data.',N'MODULE',N'V1',N'V1',20,N'PRODUCT'),
(N'INVENTORY',N'Inventory',N'Inventory balances and operations.',N'MODULE',N'V1',N'V1',30,N'INVENTORY'),
(N'POS',N'Point of Sale',N'Point-of-sale billing and returns.',N'MODULE',N'V1',N'V1',40,N'POS'),
(N'CUSTOMERS',N'Customers',N'Customer master data and groups.',N'MODULE',N'V1',N'V1',50,N'CUSTOMER'),
(N'SUPPLIERS',N'Suppliers',N'Supplier master data.',N'MODULE',N'V1',N'V1',60,N'SUPPLIER'),
(N'PURCHASE',N'Purchase',N'Purchasing and purchase returns.',N'MODULE',N'V1',N'V1',70,N'PURCHASE'),
(N'WAREHOUSES',N'Warehouses',N'Warehouse master data.',N'MODULE',N'V1',N'V1',80,N'WAREHOUSE'),
(N'FINANCE',N'Finance',N'Ledgers, receivables, payments and books.',N'MODULE',N'V1',N'V1',90,N'FINANCE'),
(N'REPORTS',N'Reports',N'ERP reports.',N'MODULE',N'V1',N'V1',100,N'REPORTS'),
(N'GST',N'GST',N'GST configuration and reports.',N'MODULE',N'V1',N'V1',110,N'GST'),
(N'ANALYTICS',N'Analytics',N'Core ERP analytics.',N'MODULE',N'V1',N'V1',120,N'ANALYTICS'),
(N'PRINTING',N'Printing',N'Documents, labels and printer configuration.',N'MODULE',N'V1',N'V1',130,N'PRINT'),
(N'USERS_ROLES',N'Users & Roles',N'Tenant identity administration.',N'MODULE',N'V1',N'V1',140,N'IDENTITY'),
(N'ADMINISTRATION',N'Administration',N'Tenant administration and settings.',N'MODULE',N'V1',N'V1',150,N'ADMIN'),
(N'V2',N'V2 — WhatsApp Commerce',N'WhatsApp and advanced commerce platform.',N'VERSION',NULL,N'V2',0,N'VERSION'),
(N'WHATSAPP_COMMERCE',N'WhatsApp Commerce',N'WhatsApp commerce orchestration.',N'MODULE',N'V2',N'V2',10,N'WHATSAPP'),
(N'WHATSAPP_CONFIGURATION',N'WhatsApp Configuration',N'Provider and channel configuration.',N'MODULE',N'V2',N'V2',20,N'WHATSAPP'),
(N'WHATSAPP_COMMERCE_DEMO',N'WhatsApp Commerce Demo',N'MOCK and META_TEST commerce demonstration.',N'MODULE',N'V2',N'V2',30,N'WHATSAPP'),
(N'COMMERCE_PRODUCT_SEARCH',N'Commerce Product Search',N'Conversational product search and availability.',N'MODULE',N'V2',N'V2',40,N'COMMERCE'),
(N'COMMERCE_COLLECTIONS',N'Commerce Collections',N'Commerce product collections and sharing.',N'MODULE',N'V2',N'V2',50,N'COMMERCE'),
(N'COMMERCE_ORDERS',N'Commerce Orders',N'Commerce checkout, orders and delivery.',N'MODULE',N'V2',N'V2',60,N'COMMERCE'),
(N'COMMERCE_ANALYTICS',N'Commerce Analytics',N'Commerce event analytics.',N'MODULE',N'V2',N'V2',70,N'COMMERCE'),
(N'META_WHATSAPP_INTEGRATION',N'Meta WhatsApp Integration',N'Meta Cloud API and META_TEST integration.',N'MODULE',N'V2',N'V2',80,N'WHATSAPP'),
(N'WEBHOOK_DIAGNOSTICS',N'Webhook Diagnostics',N'Webhook readiness and security diagnostics.',N'MODULE',N'V2',N'V2',90,N'WHATSAPP');

MERGE core.Features target USING @Features source ON target.FeatureKey=source.FeatureKey
WHEN MATCHED THEN UPDATE SET Name=source.Name,Description=source.Description,FeatureType=source.FeatureType,Version=source.Version,SortOrder=source.SortOrder,ModuleKey=source.ModuleKey,ReleaseState=CASE WHEN target.ReleaseState=N'DISABLED' THEN target.ReleaseState ELSE N'ACTIVE' END
WHEN NOT MATCHED THEN INSERT(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,Version,SortOrder,CreatedBy)
 VALUES(NEWID(),source.FeatureKey,source.Name,source.Description,source.ModuleKey,N'ACTIVE',1,source.FeatureType,source.Version,source.SortOrder,N'V12 feature hierarchy');
UPDATE f SET ParentFeatureId=p.FeatureId FROM core.Features f JOIN @Features s ON s.FeatureKey=f.FeatureKey JOIN core.Features p ON p.FeatureKey=s.ParentKey;
UPDATE core.Features SET Version=N'V1',ParentFeatureId=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'WAREHOUSES'),SortOrder=81 WHERE FeatureKey=N'ADVANCED_WAREHOUSE';
UPDATE core.Features SET Version=N'V2',ParentFeatureId=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'V2'),SortOrder=100 WHERE FeatureKey IN(N'AI_ASSISTANT',N'INTEGRATIONS');

DECLARE @V1Plan uniqueidentifier=(SELECT PlanId FROM core.Plans WHERE PlanKey=N'V1_DEFAULT');
IF @V1Plan IS NULL BEGIN SET @V1Plan=NEWID(); INSERT core.Plans(PlanId,PlanKey,Name,IsActive,CreatedBy) VALUES(@V1Plan,N'V1_DEFAULT',N'V1 Default',1,N'V12 feature hierarchy'); END;
DECLARE @V2Plan uniqueidentifier=(SELECT PlanId FROM core.Plans WHERE PlanKey=N'V2_COMMERCE');
IF @V2Plan IS NULL BEGIN SET @V2Plan=NEWID(); INSERT core.Plans(PlanId,PlanKey,Name,IsActive,CreatedBy) VALUES(@V2Plan,N'V2_COMMERCE',N'V1 + V2 Commerce',1,N'V12 feature hierarchy'); END;

-- Reconcile every active feature, including legacy/future features not declared in @Features.
-- Version controls entitlement: V1_DEFAULT receives V1; V2_COMMERCE receives all active features.
MERGE core.PlanFeatures target USING
 (SELECT p.PlanId,f.FeatureId,CAST(CASE WHEN p.PlanId=@V2Plan OR f.Version=N'V1' THEN 1 ELSE 0 END AS bit) IsEnabled
  FROM (SELECT @V1Plan PlanId UNION ALL SELECT @V2Plan) p CROSS JOIN core.Features f WHERE f.IsActive=1) source
ON target.PlanId=source.PlanId AND target.FeatureId=source.FeatureId
WHEN MATCHED THEN UPDATE SET IsEnabled=source.IsEnabled,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'V12 feature hierarchy'
WHEN NOT MATCHED THEN INSERT(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy) VALUES(NEWID(),source.PlanId,source.FeatureId,source.IsEnabled,N'V12 feature hierarchy');

-- Preserve tenants that had an explicit WhatsApp Commerce enablement under the legacy flat model.
UPDATE s SET IsActive=0,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'V12 feature hierarchy'
FROM core.Subscriptions s JOIN core.TenantFeatures tf ON tf.TenantId=s.TenantId AND tf.IsEnabled=1 AND tf.IsActive=1
JOIN core.Features f ON f.FeatureId=tf.FeatureId AND f.FeatureKey=N'WHATSAPP_COMMERCE'
WHERE s.IsActive=1 AND s.PlanId<>@V2Plan;
INSERT core.Subscriptions(SubscriptionId,TenantId,PlanId,StartDate,IsActive,CreatedBy)
SELECT NEWID(),tf.TenantId,@V2Plan,SYSUTCDATETIME(),1,N'V12 legacy V2 preservation'
FROM core.TenantFeatures tf JOIN core.Features f ON f.FeatureId=tf.FeatureId AND f.FeatureKey=N'WHATSAPP_COMMERCE'
WHERE tf.IsEnabled=1 AND tf.IsActive=1 AND NOT EXISTS(SELECT 1 FROM core.Subscriptions s WHERE s.TenantId=tf.TenantId AND s.PlanId=@V2Plan AND s.IsActive=1);

INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy)
SELECT NEWID(),t.TenantId,f.FeatureId,ISNULL(pf.IsEnabled,0),N'Initialized from active subscription plan',1,N'V12 feature hierarchy'
FROM core.Tenants t CROSS JOIN core.Features f
OUTER APPLY(SELECT TOP(1) s.PlanId FROM core.Subscriptions s WHERE s.TenantId=t.TenantId AND s.IsActive=1 AND s.StartDate<=SYSUTCDATETIME() AND (s.EndDate IS NULL OR s.EndDate>=SYSUTCDATETIME()) ORDER BY s.StartDate DESC) s
LEFT JOIN core.PlanFeatures pf ON pf.PlanId=s.PlanId AND pf.FeatureId=f.FeatureId
WHERE t.IsActive=1 AND f.IsActive=1 AND NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=t.TenantId AND tf.FeatureId=f.FeatureId);

COMMIT TRANSACTION;
