/* V47 - Storefront customer password authentication and visitor activity enrichment. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH(N'sales.Customers',N'StorefrontPasswordHash') IS NULL
    ALTER TABLE sales.Customers ADD StorefrontPasswordHash nvarchar(500) NULL;
GO

IF COL_LENGTH(N'commerce.AnalyticsEvents',N'VisitorSessionId') IS NULL
    ALTER TABLE commerce.AnalyticsEvents ADD VisitorSessionId varchar(64) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'IX_AnalyticsEvents_TenantVisitorCreated')
    CREATE INDEX IX_AnalyticsEvents_TenantVisitorCreated ON commerce.AnalyticsEvents(TenantId,VisitorSessionId,CreatedOn DESC);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'CK_AnalyticsEvents_VisitorSessionId')
    ALTER TABLE commerce.AnalyticsEvents WITH CHECK ADD CONSTRAINT CK_AnalyticsEvents_VisitorSessionId CHECK(VisitorSessionId IS NULL OR (LEN(VisitorSessionId) BETWEEN 16 AND 64 AND VisitorSessionId NOT LIKE '%[^A-Za-z0-9_-]%'));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'sales.Customers') AND name=N'CK_Customers_StorefrontPasswordHash')
    ALTER TABLE sales.Customers WITH CHECK ADD CONSTRAINT CK_Customers_StorefrontPasswordHash CHECK(StorefrontPasswordHash IS NULL OR LEN(StorefrontPasswordHash) BETWEEN 60 AND 500);
