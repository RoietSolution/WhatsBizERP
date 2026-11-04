/* V82: opt-in, tenant-scoped anonymous storefront analytics. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'core.Tenants',N'U') IS NULL OR OBJECT_ID(N'core.Features',N'U') IS NULL
    THROW 82000,N'V82 requires the canonical tenant and feature catalogs.',1;
IF OBJECT_ID(N'commerce.AnalyticsEvents',N'U') IS NULL
    THROW 82001,N'V82 requires the existing storefront analytics foundation.',1;

BEGIN TRANSACTION;
DECLARE @parent uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'ANALYTICS');
IF @parent IS NULL THROW 82002,N'V82 requires the Analytics parent feature.',1;
IF NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'GUEST_VISITOR_ANALYTICS')
    INSERT core.Features(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,ParentFeatureId,Version,SortOrder,CreatedBy)
    VALUES(NEWID(),N'GUEST_VISITOR_ANALYTICS',N'Guest Visitor Analytics',N'Opt-in, aggregated storefront visitor analytics. Requires an approved retention policy and customer analytics consent.',N'ANALYTICS',N'ACTIVE',1,N'MODULE',@parent,N'V1',92,N'V82');

DECLARE @feature uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'GUEST_VISITOR_ANALYTICS');
INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy)
SELECT NEWID(),p.PlanId,@feature,CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM core.PlanFeatures pf JOIN core.Features f ON f.FeatureId=pf.FeatureId WHERE pf.PlanId=p.PlanId AND f.FeatureKey=N'ANALYTICS' AND pf.IsEnabled=1) THEN 1 ELSE 0 END),N'V82'
FROM core.Plans p WHERE NOT EXISTS(SELECT 1 FROM core.PlanFeatures x WHERE x.PlanId=p.PlanId AND x.FeatureId=@feature);
INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy)
SELECT NEWID(),t.TenantId,@feature,0,N'Explicit owner opt-in required',1,N'V82'
FROM core.Tenants t WHERE NOT EXISTS(SELECT 1 FROM core.TenantFeatures x WHERE x.TenantId=t.TenantId AND x.FeatureId=@feature);

IF OBJECT_ID(N'commerce.GuestVisitorAnalyticsPolicies',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.GuestVisitorAnalyticsPolicies
    (
        TenantId uniqueidentifier NOT NULL CONSTRAINT PK_GuestVisitorAnalyticsPolicies PRIMARY KEY,
        RetentionDays int NULL,
        PolicyReviewedAtUtc datetime2(0) NULL,
        PolicyReviewedBy nvarchar(256) NULL,
        ModifiedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_GuestAnalyticsPolicy_Modified DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_GuestAnalyticsPolicy_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_GuestAnalyticsPolicy_Retention CHECK(RetentionDays IS NULL OR RetentionDays BETWEEN 1 AND 365),
        CONSTRAINT CK_GuestAnalyticsPolicy_Review CHECK((RetentionDays IS NULL AND PolicyReviewedAtUtc IS NULL AND PolicyReviewedBy IS NULL) OR (RetentionDays IS NOT NULL AND PolicyReviewedAtUtc IS NOT NULL AND PolicyReviewedBy IS NOT NULL))
    );
END;

IF OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.GuestVisitorAnalyticsSessions
    (
        TenantId uniqueidentifier NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        VisitorId uniqueidentifier NOT NULL,
        NoticeVersion nvarchar(40) NOT NULL,
        ConsentContext varchar(24) NOT NULL CONSTRAINT DF_GuestAnalytics_ConsentContext DEFAULT('STOREFRONT'),
        ConsentedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_GuestAnalytics_ConsentAt DEFAULT(SYSUTCDATETIME()),
        StartedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_GuestAnalytics_Started DEFAULT(SYSUTCDATETIME()),
        LastActivityAtUtc datetime2(0) NOT NULL CONSTRAINT DF_GuestAnalytics_LastActivity DEFAULT(SYSUTCDATETIME()),
        EngagedSeconds int NOT NULL CONSTRAINT DF_GuestAnalytics_Engaged DEFAULT(0),
        DeviceCategory varchar(12) NOT NULL,
        BrowserCategory varchar(20) NOT NULL,
        OperatingSystemCategory varchar(20) NOT NULL,
        ViewportCategory varchar(12) NOT NULL,
        TrafficSource varchar(16) NOT NULL,
        CampaignSource nvarchar(100) NULL,
        CampaignMedium nvarchar(100) NULL,
        CampaignName nvarchar(120) NULL,
        CONSTRAINT PK_GuestVisitorAnalyticsSessions PRIMARY KEY(TenantId,SessionId),
        CONSTRAINT FK_GuestAnalyticsSessions_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_GuestAnalytics_SessionValues CHECK(EngagedSeconds>=0 AND DeviceCategory IN('MOBILE','DESKTOP','TABLET','OTHER') AND ViewportCategory IN('SMALL','MEDIUM','LARGE','UNKNOWN') AND TrafficSource IN('DIRECT','SEARCH','SOCIAL','REFERRAL','CAMPAIGN','OTHER'))
    );
    CREATE INDEX IX_GuestAnalyticsSessions_TenantStarted ON commerce.GuestVisitorAnalyticsSessions(TenantId,StartedAtUtc) INCLUDE(VisitorId,DeviceCategory,BrowserCategory,OperatingSystemCategory,TrafficSource,EngagedSeconds);
    CREATE INDEX IX_GuestAnalyticsSessions_TenantVisitor ON commerce.GuestVisitorAnalyticsSessions(TenantId,VisitorId,StartedAtUtc);
END;

IF COL_LENGTH(N'commerce.AnalyticsEvents',N'GuestSessionId') IS NULL
    ALTER TABLE commerce.AnalyticsEvents ADD GuestSessionId uniqueidentifier NULL;
IF COL_LENGTH(N'commerce.AnalyticsEvents',N'ClientEventId') IS NULL
    ALTER TABLE commerce.AnalyticsEvents ADD ClientEventId uniqueidentifier NULL;
IF COL_LENGTH(N'commerce.AnalyticsEvents',N'CategoryId') IS NULL
    ALTER TABLE commerce.AnalyticsEvents ADD CategoryId uniqueidentifier NULL;
IF COL_LENGTH(N'commerce.AnalyticsEvents',N'NormalizedSearchTerm') IS NULL
    ALTER TABLE commerce.AnalyticsEvents ADD NormalizedSearchTerm nvarchar(200) NULL;
IF COL_LENGTH(N'commerce.AnalyticsEvents',N'ResultCount') IS NULL
    ALTER TABLE commerce.AnalyticsEvents ADD ResultCount int NULL;
IF COL_LENGTH(N'commerce.ProductSearchEvents',N'GuestSessionId') IS NULL
    ALTER TABLE commerce.ProductSearchEvents ADD GuestSessionId uniqueidentifier NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'FK_AnalyticsEvents_GuestSession')
    ALTER TABLE commerce.AnalyticsEvents WITH CHECK ADD CONSTRAINT FK_AnalyticsEvents_GuestSession FOREIGN KEY(TenantId,GuestSessionId) REFERENCES commerce.GuestVisitorAnalyticsSessions(TenantId,SessionId) ON DELETE CASCADE;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'FK_ProductSearchEvents_GuestSession')
    ALTER TABLE commerce.ProductSearchEvents WITH CHECK ADD CONSTRAINT FK_ProductSearchEvents_GuestSession FOREIGN KEY(TenantId,GuestSessionId) REFERENCES commerce.GuestVisitorAnalyticsSessions(TenantId,SessionId) ON DELETE CASCADE;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'CK_AnalyticsEvents_GuestAnalyticsData')
    ALTER TABLE commerce.AnalyticsEvents WITH CHECK ADD CONSTRAINT CK_AnalyticsEvents_GuestAnalyticsData CHECK(((ClientEventId IS NULL AND GuestSessionId IS NULL) OR (ClientEventId IS NOT NULL AND GuestSessionId IS NOT NULL)) AND (ResultCount IS NULL OR ResultCount>=0) AND LEN(ISNULL(NormalizedSearchTerm,N''))<=200);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'UX_AnalyticsEvents_TenantClientEvent')
    CREATE UNIQUE INDEX UX_AnalyticsEvents_TenantClientEvent ON commerce.AnalyticsEvents(TenantId,ClientEventId) WHERE ClientEventId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'IX_AnalyticsEvents_GuestSessionDate')
    CREATE INDEX IX_AnalyticsEvents_GuestSessionDate ON commerce.AnalyticsEvents(TenantId,GuestSessionId,CreatedOn) INCLUDE(EventType,ProductId,CategoryId,ResultCount) WHERE GuestSessionId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'IX_ProductSearchEvents_GuestSession')
    CREATE INDEX IX_ProductSearchEvents_GuestSession ON commerce.ProductSearchEvents(TenantId,GuestSessionId,CreatedAtUtc) WHERE GuestSessionId IS NOT NULL;
COMMIT TRANSACTION;
