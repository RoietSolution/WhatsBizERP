SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.GuestVisitorAnalyticsPolicies',N'U') IS NULL OR OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions',N'U') IS NULL OR OBJECT_ID(N'commerce.AnalyticsEvents',N'U') IS NULL
    THROW 82010,N'V82 validation failed: guest visitor analytics objects are missing.',1;
IF NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'GUEST_VISITOR_ANALYTICS' AND IsActive=1 AND ParentFeatureId=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'ANALYTICS'))
    THROW 82011,N'V82 validation failed: guest analytics feature catalog or dependency is invalid.',1;
IF COL_LENGTH(N'commerce.GuestVisitorAnalyticsSessions',N'VisitorId') IS NULL OR COL_LENGTH(N'commerce.GuestVisitorAnalyticsSessions',N'ConsentContext') IS NULL OR COL_LENGTH(N'commerce.GuestVisitorAnalyticsSessions',N'NoticeVersion') IS NULL OR COL_LENGTH(N'commerce.GuestVisitorAnalyticsSessions',N'EngagedSeconds') IS NULL
    THROW 82012,N'V82 validation failed: consent/session fields are missing.',1;
IF COL_LENGTH(N'commerce.AnalyticsEvents',N'NormalizedSearchTerm') IS NULL OR COL_LENGTH(N'commerce.AnalyticsEvents',N'GuestSessionId') IS NULL OR COL_LENGTH(N'commerce.AnalyticsEvents',N'ClientEventId') IS NULL OR COL_LENGTH(N'commerce.AnalyticsEvents',N'CategoryId') IS NULL OR COL_LENGTH(N'commerce.AnalyticsEvents',N'ResultCount') IS NULL OR COL_LENGTH(N'commerce.ProductSearchEvents',N'GuestSessionId') IS NULL
    THROW 82013,N'V82 validation failed: event dimensions are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions') AND name=N'FK_GuestAnalyticsSessions_Tenant') OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'FK_AnalyticsEvents_GuestSession') OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'FK_ProductSearchEvents_GuestSession')
    THROW 82014,N'V82 validation failed: tenant/session foreign keys are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'CK_AnalyticsEvents_GuestAnalyticsData')
    THROW 82018,N'V82 validation failed: guest analytics event constraints are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions') AND name=N'IX_GuestAnalyticsSessions_TenantStarted') OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'UX_AnalyticsEvents_TenantClientEvent') OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'IX_AnalyticsEvents_GuestSessionDate') OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'IX_ProductSearchEvents_GuestSession')
    THROW 82015,N'V82 validation failed: tenant-leading analytics indexes are missing.',1;
IF EXISTS(SELECT 1 FROM commerce.GuestVisitorAnalyticsSessions WHERE EngagedSeconds<0 OR DeviceCategory NOT IN('MOBILE','DESKTOP','TABLET','OTHER') OR ViewportCategory NOT IN('SMALL','MEDIUM','LARGE','UNKNOWN') OR TrafficSource NOT IN('DIRECT','SEARCH','SOCIAL','REFERRAL','CAMPAIGN','OTHER'))
 OR EXISTS(SELECT 1 FROM commerce.AnalyticsEvents WHERE GuestSessionId IS NOT NULL AND (ResultCount<0 OR LEN(ISNULL(NormalizedSearchTerm,N''))>200))
    THROW 82016,N'V82 validation failed: invalid analytics data exists.',1;
IF EXISTS(SELECT 1 FROM commerce.GuestVisitorAnalyticsPolicies WHERE RetentionDays NOT BETWEEN 1 AND 365 OR (RetentionDays IS NOT NULL AND (PolicyReviewedAtUtc IS NULL OR PolicyReviewedBy IS NULL)))
    THROW 82017,N'V82 validation failed: retention policy state is inconsistent.',1;
