SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.StoreDirectoryEntries',N'U') IS NULL
 THROW 74000,N'V74 requires V72/V73 public catalog governance.',1;
IF OBJECT_ID(N'core.Tenants',N'U') IS NULL
 THROW 74001,N'V74 requires the tenant master.',1;
IF NOT EXISTS(
 SELECT 1 FROM sys.key_constraints pk
 JOIN sys.index_columns ic ON ic.object_id=pk.parent_object_id AND ic.index_id=pk.unique_index_id AND ic.key_ordinal=1
 JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
 WHERE pk.parent_object_id=OBJECT_ID(N'core.Tenants') AND pk.type='PK' AND c.name=N'TenantId' AND c.system_type_id=36 AND c.is_nullable=0
 AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=pk.parent_object_id AND x.index_id=pk.unique_index_id AND x.key_ordinal>0)=1
)
 THROW 74007,N'V74 requires core.Tenants.TenantId as a non-null uniqueidentifier primary key.',1;
BEGIN TRANSACTION;

/* A route slug is a globally reserved public identifier. Existing tenant keys
   are copied verbatim (normalized only for lookup); collisions stop migration. */
IF OBJECT_ID(N'commerce.PublicRouteRegistry',N'U') IS NULL
 CREATE TABLE commerce.PublicRouteRegistry(
  RouteId uniqueidentifier NOT NULL CONSTRAINT DF_PublicRouteRegistry_Id DEFAULT NEWSEQUENTIALID(),
  OriginalSlug nvarchar(120) NOT NULL,
  NormalizedSlug AS LOWER(LTRIM(RTRIM(OriginalSlug))) PERSISTED,
  EntityType varchar(16) NOT NULL,
  TenantId uniqueidentifier NULL,
  CityId uniqueidentifier NULL,
  RouteStatus varchar(16) NOT NULL CONSTRAINT DF_PublicRouteRegistry_Status DEFAULT('ACTIVE'),
  CreatedAt datetimeoffset(0) NOT NULL CONSTRAINT DF_PublicRouteRegistry_Created DEFAULT SYSUTCDATETIME(),
  UpdatedAt datetimeoffset(0) NULL,
  CreatedBy nvarchar(256) NULL,
  UpdatedBy nvarchar(256) NULL,
  Version rowversion NOT NULL,
  CONSTRAINT PK_PublicRouteRegistry PRIMARY KEY(RouteId),
  CONSTRAINT CK_PublicRouteRegistry_Type CHECK(EntityType IN ('CITY','RETAILER')),
  CONSTRAINT CK_PublicRouteRegistry_Status CHECK(RouteStatus IN ('ACTIVE','INACTIVE','UNPUBLISHED','RETIRED')),
  CONSTRAINT CK_PublicRouteRegistry_Entity CHECK((EntityType='CITY' AND CityId IS NOT NULL AND TenantId IS NULL) OR (EntityType='RETAILER' AND TenantId IS NOT NULL AND CityId IS NULL)),
  CONSTRAINT CK_PublicRouteRegistry_Slug CHECK(
   LEN(LTRIM(RTRIM(OriginalSlug))) BETWEEN 1 AND 120 AND
   ((EntityType='CITY' AND OriginalSlug NOT LIKE '%[^A-Za-z0-9-]%' AND LEFT(OriginalSlug,1)<>'-' AND RIGHT(OriginalSlug,1)<>'-') OR
    (EntityType='RETAILER' AND OriginalSlug NOT LIKE '%[^A-Za-z0-9_-]%' AND LEFT(OriginalSlug,1) NOT IN('-','_') AND RIGHT(OriginalSlug,1) NOT IN('-','_')))
  )
 );
GO

IF OBJECT_ID(N'commerce.Cities',N'U') IS NULL
 CREATE TABLE commerce.Cities(
  CityId uniqueidentifier NOT NULL CONSTRAINT DF_Cities_Id DEFAULT NEWSEQUENTIALID(),
  CityName nvarchar(120) NOT NULL, StateName nvarchar(120) NOT NULL,
  MarketplaceName nvarchar(160) NOT NULL, Description nvarchar(500) NULL,
  LogoMediaId uniqueidentifier NULL, BannerMediaId uniqueidentifier NULL,
  SeoTitle nvarchar(180) NULL, SeoDescription nvarchar(320) NULL,
  ExternalMarketingDomain nvarchar(253) NULL,
  IsActive bit NOT NULL CONSTRAINT DF_Cities_Active DEFAULT(0),
  IsPublished bit NOT NULL CONSTRAINT DF_Cities_Published DEFAULT(0),
  CreatedAt datetimeoffset(0) NOT NULL CONSTRAINT DF_Cities_Created DEFAULT SYSUTCDATETIME(),
  UpdatedAt datetimeoffset(0) NULL, CreatedBy nvarchar(256) NULL, UpdatedBy nvarchar(256) NULL,
  Version rowversion NOT NULL,
  CONSTRAINT PK_Cities PRIMARY KEY(CityId),
  CONSTRAINT CK_Cities_Name CHECK(LEN(LTRIM(RTRIM(CityName)))>0 AND LEN(LTRIM(RTRIM(StateName)))>0 AND LEN(LTRIM(RTRIM(MarketplaceName)))>0)
 );
GO

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_PublicRouteRegistry_Tenant')
 ALTER TABLE commerce.PublicRouteRegistry ADD CONSTRAINT FK_PublicRouteRegistry_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_PublicRouteRegistry_City')
 ALTER TABLE commerce.PublicRouteRegistry ADD CONSTRAINT FK_PublicRouteRegistry_City FOREIGN KEY(CityId) REFERENCES commerce.Cities(CityId);
GO

/* Do not silently rename existing store routes. Reserved application paths or
   normalized collisions require operator resolution before any backfill. */
IF EXISTS(
 SELECT LOWER(LTRIM(RTRIM(TenantKey))) FROM core.Tenants
 GROUP BY LOWER(LTRIM(RTRIM(TenantKey))) HAVING COUNT(*)>1
) THROW 74002,N'Existing retailer store keys collide after public-slug normalization; resolve explicitly before V74.',1;
IF EXISTS(SELECT 1 FROM core.Tenants WHERE TenantKey IS NULL OR LTRIM(RTRIM(TenantKey))='' OR LEN(LTRIM(RTRIM(TenantKey)))>120 OR TenantKey LIKE '%[^A-Za-z0-9_-]%' OR LEFT(LTRIM(RTRIM(TenantKey)),1) IN('-','_') OR RIGHT(LTRIM(RTRIM(TenantKey)),1) IN('-','_'))
 THROW 74003,N'An existing retailer store key cannot be preserved as a public route alias; do not rename automatically.',1;
IF EXISTS(SELECT 1 FROM core.Tenants WHERE LOWER(LTRIM(RTRIM(TenantKey))) IN
 ('ref','share','cart','checkout','products','categories','search','offers','orders','addresses','account','auth','feedback','payment-return','assets','city','stores','api','admin'))
 THROW 74004,N'An existing retailer store key conflicts with a reserved application route; resolve explicitly before V74.',1;
IF EXISTS(SELECT 1 FROM commerce.PublicRouteRegistry r JOIN core.Tenants t ON LOWER(LTRIM(RTRIM(t.TenantKey)))=r.NormalizedSlug AND r.TenantId<>t.TenantId)
 THROW 74005,N'Public route registry contains a retailer slug collision; manual review required.',1;
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PublicRouteRegistry') AND name=N'UX_PublicRouteRegistry_NormalizedSlug') CREATE UNIQUE INDEX UX_PublicRouteRegistry_NormalizedSlug ON commerce.PublicRouteRegistry(NormalizedSlug);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PublicRouteRegistry') AND name=N'UX_PublicRouteRegistry_Tenant') CREATE UNIQUE INDEX UX_PublicRouteRegistry_Tenant ON commerce.PublicRouteRegistry(TenantId) WHERE TenantId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PublicRouteRegistry') AND name=N'UX_PublicRouteRegistry_City') CREATE UNIQUE INDEX UX_PublicRouteRegistry_City ON commerce.PublicRouteRegistry(CityId) WHERE CityId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PublicRouteRegistry') AND name=N'IX_PublicRouteRegistry_PublicLookup') CREATE INDEX IX_PublicRouteRegistry_PublicLookup ON commerce.PublicRouteRegistry(NormalizedSlug,RouteStatus) INCLUDE(EntityType,TenantId,CityId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.Cities') AND name=N'UX_Cities_NameState') CREATE UNIQUE INDEX UX_Cities_NameState ON commerce.Cities(CityName,StateName);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'master.Products') AND name=N'UX_Products_Tenant_ProductId') CREATE UNIQUE INDEX UX_Products_Tenant_ProductId ON master.Products(TenantId,ProductId);
GO

INSERT commerce.PublicRouteRegistry(OriginalSlug,EntityType,TenantId,RouteStatus,CreatedBy)
SELECT t.TenantKey,'RETAILER',t.TenantId,CASE WHEN t.IsActive=1 THEN 'ACTIVE' ELSE 'INACTIVE' END,N'V74-backfill'
FROM core.Tenants t WHERE NOT EXISTS(SELECT 1 FROM commerce.PublicRouteRegistry r WHERE r.TenantId=t.TenantId);
GO

IF OBJECT_ID(N'commerce.CityRetailers',N'U') IS NULL
 CREATE TABLE commerce.CityRetailers(
  CityId uniqueidentifier NOT NULL, TenantId uniqueidentifier NOT NULL,
  Locality nvarchar(120) NULL, ServiceArea nvarchar(300) NULL,
  IsActive bit NOT NULL CONSTRAINT DF_CityRetailers_Active DEFAULT(1),
  CreatedAt datetimeoffset(0) NOT NULL CONSTRAINT DF_CityRetailers_Created DEFAULT SYSUTCDATETIME(),
  CreatedBy nvarchar(256) NULL,
  CONSTRAINT PK_CityRetailers PRIMARY KEY(CityId,TenantId),
  CONSTRAINT FK_CityRetailers_City FOREIGN KEY(CityId) REFERENCES commerce.Cities(CityId),
  CONSTRAINT FK_CityRetailers_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId)
 );
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.CityRetailers') AND name=N'IX_CityRetailers_Tenant') CREATE INDEX IX_CityRetailers_Tenant ON commerce.CityRetailers(TenantId,IsActive) INCLUDE(CityId,Locality);
GO

/* Partner catalog is tenant-owned. Partner sellers do not become tenants or
   users; public discovery remains gated by V72/V73 and product governance. */
IF OBJECT_ID(N'commerce.PartnerSellers',N'U') IS NULL
 CREATE TABLE commerce.PartnerSellers(
  PartnerSellerId uniqueidentifier NOT NULL CONSTRAINT DF_PartnerSellers_Id DEFAULT NEWSEQUENTIALID(),
  OperatingTenantId uniqueidentifier NOT NULL,
  SellerName nvarchar(160) NOT NULL, SellerType varchar(24) NOT NULL,
  ContactName nvarchar(120) NULL, ContactEmail nvarchar(256) NULL, ContactPhone nvarchar(32) NULL,
  CityId uniqueidentifier NULL, Locality nvarchar(120) NULL,
  BusinessRegistrationReference nvarchar(100) NULL, TaxRegistrationReference nvarchar(100) NULL,
  FoodRegistrationReference nvarchar(100) NULL,
  VerificationStatus varchar(20) NOT NULL CONSTRAINT DF_PartnerSellers_Verification DEFAULT('PENDING'),
  SellerStatus varchar(16) NOT NULL CONSTRAINT DF_PartnerSellers_Status DEFAULT('ACTIVE'),
  CreatedAt datetimeoffset(0) NOT NULL CONSTRAINT DF_PartnerSellers_Created DEFAULT SYSUTCDATETIME(),
  UpdatedAt datetimeoffset(0) NULL, CreatedBy nvarchar(256) NOT NULL, UpdatedBy nvarchar(256) NULL,
  Version rowversion NOT NULL,
  CONSTRAINT PK_PartnerSellers PRIMARY KEY(PartnerSellerId),
  CONSTRAINT UQ_PartnerSellers_TenantId UNIQUE(OperatingTenantId,PartnerSellerId),
  CONSTRAINT FK_PartnerSellers_Tenant FOREIGN KEY(OperatingTenantId) REFERENCES core.Tenants(TenantId),
  CONSTRAINT FK_PartnerSellers_City FOREIGN KEY(CityId) REFERENCES commerce.Cities(CityId),
  CONSTRAINT CK_PartnerSellers_Type CHECK(SellerType IN ('INDIVIDUAL','BUSINESS','FOOD_BUSINESS','OTHER')),
  CONSTRAINT CK_PartnerSellers_Verification CHECK(VerificationStatus IN ('PENDING','VERIFIED','REJECTED','SUSPENDED')),
  CONSTRAINT CK_PartnerSellers_Status CHECK(SellerStatus IN ('ACTIVE','SUSPENDED','CLOSED')),
  CONSTRAINT CK_PartnerSellers_Name CHECK(LEN(LTRIM(RTRIM(SellerName)))>0)
 );
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PartnerSellers') AND name=N'IX_PartnerSellers_TenantStatus') CREATE INDEX IX_PartnerSellers_TenantStatus ON commerce.PartnerSellers(OperatingTenantId,SellerStatus,VerificationStatus);
GO

IF OBJECT_ID(N'commerce.PartnerProductListings',N'U') IS NULL
 CREATE TABLE commerce.PartnerProductListings(
  OperatingTenantId uniqueidentifier NOT NULL, ProductId uniqueidentifier NOT NULL,
  PartnerSellerId uniqueidentifier NOT NULL, ListingStatus varchar(20) NOT NULL CONSTRAINT DF_PartnerListings_Status DEFAULT('PENDING_APPROVAL'),
  CreatedAt datetimeoffset(0) NOT NULL CONSTRAINT DF_PartnerListings_Created DEFAULT SYSUTCDATETIME(),
  CreatedBy nvarchar(256) NOT NULL, Version rowversion NOT NULL,
  CONSTRAINT PK_PartnerProductListings PRIMARY KEY(OperatingTenantId,ProductId),
  CONSTRAINT FK_PartnerListings_Seller FOREIGN KEY(OperatingTenantId,PartnerSellerId) REFERENCES commerce.PartnerSellers(OperatingTenantId,PartnerSellerId),
  CONSTRAINT FK_PartnerListings_Product FOREIGN KEY(OperatingTenantId,ProductId) REFERENCES master.Products(TenantId,ProductId),
  CONSTRAINT CK_PartnerListings_Status CHECK(ListingStatus IN ('PENDING_APPROVAL','APPROVED','REJECTED','SUSPENDED'))
 );
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PartnerProductListings') AND name=N'IX_PartnerListings_Seller') CREATE INDEX IX_PartnerListings_Seller ON commerce.PartnerProductListings(OperatingTenantId,PartnerSellerId,ListingStatus);
GO

IF OBJECT_ID(N'commerce.PartnerCommissionAgreements',N'U') IS NULL
 CREATE TABLE commerce.PartnerCommissionAgreements(
  AgreementId uniqueidentifier NOT NULL CONSTRAINT DF_PartnerAgreement_Id DEFAULT NEWSEQUENTIALID(),
  OperatingTenantId uniqueidentifier NOT NULL, PartnerSellerId uniqueidentifier NOT NULL,
  ProductId uniqueidentifier NULL, AgreementVersion int NOT NULL,
  CommissionType varchar(12) NOT NULL, CommissionValue decimal(18,4) NOT NULL,
  CalculationBasis varchar(24) NOT NULL, DiscountResponsibility varchar(20) NOT NULL,
  DeliveryResponsibility varchar(20) NOT NULL, PackagingResponsibility varchar(20) NOT NULL,
  PaymentFeeResponsibility varchar(20) NOT NULL, ReturnTreatment varchar(24) NOT NULL,
  SettlementCycle varchar(16) NOT NULL, EffectiveFrom datetimeoffset(0) NOT NULL, EffectiveTo datetimeoffset(0) NULL,
  AgreementStatus varchar(16) NOT NULL CONSTRAINT DF_PartnerAgreement_Status DEFAULT('DRAFT'),
  CreatedAt datetimeoffset(0) NOT NULL CONSTRAINT DF_PartnerAgreement_Created DEFAULT SYSUTCDATETIME(), CreatedBy nvarchar(256) NOT NULL,
  CONSTRAINT PK_PartnerCommissionAgreements PRIMARY KEY(AgreementId),
  CONSTRAINT FK_PartnerAgreement_Seller FOREIGN KEY(OperatingTenantId,PartnerSellerId) REFERENCES commerce.PartnerSellers(OperatingTenantId,PartnerSellerId),
  CONSTRAINT FK_PartnerAgreement_Product FOREIGN KEY(OperatingTenantId,ProductId) REFERENCES master.Products(TenantId,ProductId),
  CONSTRAINT CK_PartnerAgreement_Type CHECK(CommissionType IN ('PERCENT','FIXED')),
  CONSTRAINT CK_PartnerAgreement_Value CHECK(CommissionValue>=0 AND (CommissionType<>'PERCENT' OR CommissionValue<=100)),
  CONSTRAINT CK_PartnerAgreement_Basis CHECK(CalculationBasis IN ('ITEM_GROSS','ITEM_NET','ITEM_TAXABLE')),
  CONSTRAINT CK_PartnerAgreement_Status CHECK(AgreementStatus IN ('DRAFT','ACTIVE','EXPIRED','REVOKED')),
  CONSTRAINT CK_PartnerAgreement_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo>EffectiveFrom)
 );
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PartnerCommissionAgreements') AND name=N'IX_PartnerAgreement_Effective') CREATE INDEX IX_PartnerAgreement_Effective ON commerce.PartnerCommissionAgreements(OperatingTenantId,PartnerSellerId,AgreementStatus,EffectiveFrom,EffectiveTo);
GO

/* These records are intentionally not connected to payment or invoice posting
   in V74. They are append-only accounting snapshots for a later gated rollout. */
IF OBJECT_ID(N'commerce.PartnerSettlementEntries',N'U') IS NULL
 CREATE TABLE commerce.PartnerSettlementEntries(
  SettlementEntryId uniqueidentifier NOT NULL CONSTRAINT DF_PartnerSettlement_Id DEFAULT NEWSEQUENTIALID(),
  OperatingTenantId uniqueidentifier NOT NULL, PartnerSellerId uniqueidentifier NOT NULL,
  OrderReference nvarchar(100) NOT NULL, OrderLineReference nvarchar(100) NULL,
  EntryType varchar(20) NOT NULL, GrossAmount decimal(18,2) NOT NULL,
  CommissionAmount decimal(18,2) NOT NULL, SellerPayable decimal(18,2) NOT NULL,
  AgreementId uniqueidentifier NULL, AgreementVersion int NULL, IdempotencyKey nvarchar(160) NOT NULL,
  ReversesEntryId uniqueidentifier NULL, OccurredAt datetimeoffset(0) NOT NULL CONSTRAINT DF_PartnerSettlement_Occurred DEFAULT SYSUTCDATETIME(),
  CreatedBy nvarchar(256) NOT NULL,
  CONSTRAINT PK_PartnerSettlementEntries PRIMARY KEY(SettlementEntryId),
  CONSTRAINT FK_PartnerSettlement_Seller FOREIGN KEY(OperatingTenantId,PartnerSellerId) REFERENCES commerce.PartnerSellers(OperatingTenantId,PartnerSellerId),
  CONSTRAINT FK_PartnerSettlement_Agreement FOREIGN KEY(AgreementId) REFERENCES commerce.PartnerCommissionAgreements(AgreementId),
  CONSTRAINT FK_PartnerSettlement_Reversal FOREIGN KEY(ReversesEntryId) REFERENCES commerce.PartnerSettlementEntries(SettlementEntryId),
  CONSTRAINT CK_PartnerSettlement_Type CHECK(EntryType IN ('SALE','REFUND','ADJUSTMENT','SETTLEMENT')),
  CONSTRAINT CK_PartnerSettlement_Amounts CHECK(GrossAmount>=0 AND CommissionAmount>=0 AND SellerPayable>=0)
 );
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PartnerSettlementEntries') AND name=N'UX_PartnerSettlement_Idempotency') CREATE UNIQUE INDEX UX_PartnerSettlement_Idempotency ON commerce.PartnerSettlementEntries(OperatingTenantId,IdempotencyKey);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.PartnerSettlementEntries') AND name=N'IX_PartnerSettlement_SellerHistory') CREATE INDEX IX_PartnerSettlement_SellerHistory ON commerce.PartnerSettlementEntries(OperatingTenantId,PartnerSellerId,OccurredAt);
GO
DECLARE @parent uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'V1');
IF @parent IS NULL THROW 74006,N'V74 requires the existing V1 feature root.',1;
DECLARE @featureCatalog TABLE(FeatureKey nvarchar(100),FeatureName nvarchar(150),FeatureDescription nvarchar(500),SortOrder int);
INSERT @featureCatalog VALUES
 (N'CITY_MARKETPLACE',N'City Marketplace',N'Configure retailer city and locality eligibility for the KhataDhari marketplace.',89),
 (N'PARTNER_SELLER_MANAGEMENT',N'Partner Seller Management',N'Manage tenant-scoped third-party sellers and product attribution.',90),
 (N'PARTNER_COMMISSIONS',N'Partner Commissions',N'Manage versioned partner commission agreements; financial activation remains separately gated.',91),
 (N'PARTNER_SETTLEMENTS',N'Partner Settlements',N'Review seller settlement ledger and record controlled manual settlements.',92);
INSERT core.Features(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,ParentFeatureId,Version,SortOrder,CreatedBy)
SELECT NEWID(),c.FeatureKey,c.FeatureName,c.FeatureDescription,N'ADMINISTRATION',N'ACTIVE',1,N'MODULE',@parent,N'V1',c.SortOrder,N'V74'
FROM @featureCatalog c WHERE NOT EXISTS(SELECT 1 FROM core.Features f WHERE f.FeatureKey=c.FeatureKey);
INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy)
SELECT NEWID(),p.PlanId,f.FeatureId,CONVERT(bit,CASE WHEN
 EXISTS(SELECT 1 FROM core.PlanFeatures pf JOIN core.Features x ON x.FeatureId=pf.FeatureId WHERE pf.PlanId=p.PlanId AND x.FeatureKey=N'ADMINISTRATION' AND pf.IsEnabled=1)
 AND EXISTS(SELECT 1 FROM core.PlanFeatures pf JOIN core.Features x ON x.FeatureId=pf.FeatureId WHERE pf.PlanId=p.PlanId AND x.FeatureKey=N'PRODUCTS' AND pf.IsEnabled=1)
 AND (c.FeatureKey IN(N'CITY_MARKETPLACE',N'PARTNER_SELLER_MANAGEMENT') OR EXISTS(SELECT 1 FROM core.PlanFeatures pf JOIN core.Features x ON x.FeatureId=pf.FeatureId WHERE pf.PlanId=p.PlanId AND x.FeatureKey=N'FINANCE' AND pf.IsEnabled=1)) THEN 1 ELSE 0 END),N'V74'
FROM core.Plans p CROSS JOIN @featureCatalog c JOIN core.Features f ON f.FeatureKey=c.FeatureKey
WHERE NOT EXISTS(SELECT 1 FROM core.PlanFeatures pf WHERE pf.PlanId=p.PlanId AND pf.FeatureId=f.FeatureId);
INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy)
SELECT NEWID(),t.TenantId,f.FeatureId,0,N'V74: explicit Application Owner enablement required',1,N'V74'
FROM core.Tenants t CROSS JOIN @featureCatalog c JOIN core.Features f ON f.FeatureKey=c.FeatureKey
WHERE NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=t.TenantId AND tf.FeatureId=f.FeatureId);
GO
COMMIT TRANSACTION;
