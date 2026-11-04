SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.PublicRouteRegistry',N'U') IS NULL OR OBJECT_ID(N'commerce.Cities',N'U') IS NULL
 OR OBJECT_ID(N'commerce.CityRetailers',N'U') IS NULL OR OBJECT_ID(N'commerce.PartnerSellers',N'U') IS NULL
 OR OBJECT_ID(N'commerce.PartnerProductListings',N'U') IS NULL OR OBJECT_ID(N'commerce.PartnerCommissionAgreements',N'U') IS NULL
 OR OBJECT_ID(N'commerce.PartnerSettlementEntries',N'U') IS NULL
 THROW 74100,N'V74 required tables are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PublicRouteRegistry') AND name=N'UX_PublicRouteRegistry_NormalizedSlug' AND is_unique=1 AND is_disabled=0)
 THROW 74101,N'Public slug uniqueness is missing.',1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN(N'FK_PublicRouteRegistry_Tenant',N'FK_PublicRouteRegistry_City',N'FK_CityRetailers_City',N'FK_CityRetailers_Tenant',N'FK_PartnerSellers_Tenant',N'FK_PartnerSellers_City',N'FK_PartnerListings_Seller',N'FK_PartnerListings_Product',N'FK_PartnerAgreement_Seller',N'FK_PartnerAgreement_Product',N'FK_PartnerSettlement_Seller',N'FK_PartnerSettlement_Agreement',N'FK_PartnerSettlement_Reversal') AND is_disabled=0 AND is_not_trusted=0)<>13
 THROW 74107,N'One or more V74 tenant/entity foreign keys are missing or untrusted.',1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN(N'FK_PublicRouteRegistry_Tenant',N'FK_CityRetailers_Tenant',N'FK_PartnerSellers_Tenant') AND referenced_object_id=OBJECT_ID(N'core.Tenants') AND is_disabled=0 AND is_not_trusted=0)<>3
 THROW 74114,N'V74 tenant foreign keys must reference canonical core.Tenants.',1;
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN(N'CK_PublicRouteRegistry_Type',N'CK_PublicRouteRegistry_Status',N'CK_PublicRouteRegistry_Entity',N'CK_PublicRouteRegistry_Slug',N'CK_Cities_Name',N'CK_PartnerSellers_Type',N'CK_PartnerSellers_Verification',N'CK_PartnerSellers_Status',N'CK_PartnerSellers_Name',N'CK_PartnerListings_Status',N'CK_PartnerAgreement_Type',N'CK_PartnerAgreement_Value',N'CK_PartnerAgreement_Basis',N'CK_PartnerAgreement_Status',N'CK_PartnerAgreement_Dates',N'CK_PartnerSettlement_Type',N'CK_PartnerSettlement_Amounts') AND is_disabled=0 AND is_not_trusted=0)<>17
 THROW 74108,N'One or more V74 validation constraints are missing or untrusted.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PartnerSettlementEntries') AND name=N'UX_PartnerSettlement_Idempotency' AND is_unique=1 AND is_disabled=0)
 THROW 74109,N'Partner settlement idempotency uniqueness is missing.',1;
IF EXISTS(SELECT 1 FROM (VALUES(N'CITY_MARKETPLACE'),(N'PARTNER_SELLER_MANAGEMENT'),(N'PARTNER_COMMISSIONS'),(N'PARTNER_SETTLEMENTS')) k(FeatureKey)
 WHERE NOT EXISTS(SELECT 1 FROM core.Features f WHERE f.FeatureKey=k.FeatureKey AND f.IsActive=1))
 THROW 74110,N'Partner feature catalog entries are missing.',1;
IF EXISTS(SELECT 1 FROM core.TenantFeatures tf JOIN core.Features f ON f.FeatureId=tf.FeatureId WHERE f.FeatureKey IN(N'CITY_MARKETPLACE',N'PARTNER_SELLER_MANAGEMENT',N'PARTNER_COMMISSIONS',N'PARTNER_SETTLEMENTS') AND tf.IsEnabled=1)
 THROW 74111,N'V74 must not enable partner features for tenants by default.',1;
IF EXISTS(SELECT 1 FROM core.Tenants t CROSS JOIN core.Features f WHERE f.FeatureKey IN(N'CITY_MARKETPLACE',N'PARTNER_SELLER_MANAGEMENT',N'PARTNER_COMMISSIONS',N'PARTNER_SETTLEMENTS') AND NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=t.TenantId AND tf.FeatureId=f.FeatureId))
 THROW 74112,N'Partner feature tenant entitlement defaults are incomplete.',1;
IF EXISTS(SELECT 1 FROM core.Plans p CROSS JOIN core.Features f WHERE f.FeatureKey IN(N'CITY_MARKETPLACE',N'PARTNER_SELLER_MANAGEMENT',N'PARTNER_COMMISSIONS',N'PARTNER_SETTLEMENTS') AND NOT EXISTS(SELECT 1 FROM core.PlanFeatures pf WHERE pf.PlanId=p.PlanId AND pf.FeatureId=f.FeatureId))
 THROW 74113,N'Partner feature plan entitlement mappings are incomplete.',1;
IF EXISTS(SELECT 1 FROM commerce.PublicRouteRegistry GROUP BY NormalizedSlug HAVING COUNT(*)>1)
 THROW 74102,N'Public route slug collision exists.',1;
IF EXISTS(SELECT 1 FROM commerce.PublicRouteRegistry WHERE EntityType='CITY' AND (OriginalSlug LIKE '%[^A-Za-z0-9-]%' OR LEFT(LTRIM(RTRIM(OriginalSlug)),1)='-' OR RIGHT(LTRIM(RTRIM(OriginalSlug)),1)='-'))
 THROW 74115,N'A city route must use the strict letters, digits and interior-hyphens slug format.',1;
IF EXISTS(SELECT 1 FROM commerce.PublicRouteRegistry WHERE EntityType='RETAILER' AND (OriginalSlug LIKE '%[^A-Za-z0-9_-]%' OR LEFT(LTRIM(RTRIM(OriginalSlug)),1) IN('-','_') OR RIGHT(LTRIM(RTRIM(OriginalSlug)),1) IN('-','_')))
 THROW 74116,N'A legacy retailer route alias has unsupported characters.',1;
IF EXISTS(SELECT 1 FROM core.Tenants t WHERE NOT EXISTS(SELECT 1 FROM commerce.PublicRouteRegistry r WHERE r.TenantId=t.TenantId AND r.NormalizedSlug=LOWER(LTRIM(RTRIM(t.TenantKey)))))
 THROW 74103,N'An existing retailer route was not preserved in the registry.',1;
IF EXISTS(SELECT 1 FROM commerce.PartnerProductListings l WHERE NOT EXISTS(SELECT 1 FROM commerce.PartnerSellers s WHERE s.OperatingTenantId=l.OperatingTenantId AND s.PartnerSellerId=l.PartnerSellerId))
 THROW 74104,N'Partner listing tenant ownership is invalid.',1;
IF EXISTS(SELECT 1 FROM commerce.PartnerSettlementEntries s WHERE s.IdempotencyKey IS NULL OR LTRIM(RTRIM(s.IdempotencyKey))='')
 THROW 74105,N'Partner settlement idempotency data is invalid.',1;
IF EXISTS(SELECT 1 FROM commerce.Cities WHERE IsPublished=1 AND IsActive=0)
 THROW 74106,N'An inactive city is published.',1;
SELECT N'V74 schema validated; no partner order/payment/invoice workflow is enabled by this migration.' Result;
