/* READ ONLY. Run with an authorized QA connection before requesting V72 deployment.
   Existing products are a grandfathered baseline, not automatically approved.
   No bulk transition is performed by this diagnostic. */
SET NOCOUNT ON;
IF DB_NAME()<>N'WhatsBizERP_QA' OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'srv1930195'
 THROW 72010,N'Expected srv1930195 / WhatsBizERP_QA. Diagnostic stopped.',1;
SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) AS ServerName,DB_NAME() AS DatabaseName;
SELECT t.TenantId,t.TenantKey,t.Name,t.IsActive,
 (SELECT COUNT_BIG(*) FROM master.Products p WHERE p.TenantId=t.TenantId) AS ExistingProducts,
 (SELECT COUNT_BIG(*) FROM master.Products p WHERE p.TenantId=t.TenantId
  AND p.IsActive=1 AND p.IsDeleted=0 AND p.IsWhatsAppVisible=1
  AND EXISTS(SELECT 1 FROM master.TenantProductCategories c JOIN master.ProductCategories category ON category.ProductCategoryId=c.ProductCategoryId AND category.IsActive=1 AND category.IsDeleted=0 WHERE c.TenantId=t.TenantId AND c.ProductCategoryId=p.CategoryId)
  AND EXISTS(SELECT 1 FROM master.TenantUnitsOfMeasure u JOIN master.UnitsOfMeasure unit ON unit.UnitId=u.UnitId AND unit.IsActive=1 AND unit.IsDeleted=0 WHERE u.TenantId=t.TenantId AND u.UnitId=p.UnitId)) AS CandidatePublicProducts
FROM core.Tenants t ORDER BY t.TenantKey;
SELECT OBJECT_ID(N'commerce.ProductPublications',N'U') AS PublicationTable,
 OBJECT_ID(N'commerce.PublicCatalogBaseline',N'U') AS BaselineMarkerTable;
