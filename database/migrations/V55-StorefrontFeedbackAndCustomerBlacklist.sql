/* V55: tenant-scoped storefront feedback and customer phone blacklist. */
IF OBJECT_ID(N'commerce.StorefrontFeedbackTickets', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontFeedbackTickets
    (
        TicketId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StorefrontFeedbackTickets_Id DEFAULT NEWSEQUENTIALID(),
        TenantId UNIQUEIDENTIFIER NOT NULL,
        TicketReference NVARCHAR(32) NOT NULL,
        CustomerId UNIQUEIDENTIFIER NULL,
        GuestName NVARCHAR(250) NULL,
        GuestMobile NVARCHAR(30) NULL,
        FeedbackType NVARCHAR(20) NOT NULL,
        Category NVARCHAR(30) NOT NULL,
        Subject NVARCHAR(200) NOT NULL,
        Description NVARCHAR(4000) NOT NULL,
        OrderId UNIQUEIDENTIFIER NULL,
        Priority NVARCHAR(10) NOT NULL CONSTRAINT DF_StorefrontFeedbackTickets_Priority DEFAULT N'MEDIUM',
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_StorefrontFeedbackTickets_Status DEFAULT N'OPEN',
        AssignedTo UNIQUEIDENTIFIER NULL,
        CustomerVisibleReply NVARCHAR(4000) NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontFeedbackTickets_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontFeedbackTickets_Updated DEFAULT SYSUTCDATETIME(),
        ResolvedAtUtc DATETIMEOFFSET NULL,
        CONSTRAINT PK_StorefrontFeedbackTickets PRIMARY KEY(TicketId),
        CONSTRAINT CK_StorefrontFeedbackTickets_Type CHECK(FeedbackType IN(N'SUGGESTION',N'COMPLAINT')),
        CONSTRAINT CK_StorefrontFeedbackTickets_Category CHECK(Category IN(N'ORDER',N'PRODUCT',N'DELIVERY',N'PAYMENT_REFUND',N'CUSTOMER_SERVICE',N'OTHER')),
        CONSTRAINT CK_StorefrontFeedbackTickets_Priority CHECK(Priority IN(N'LOW',N'MEDIUM',N'HIGH',N'URGENT')),
        CONSTRAINT CK_StorefrontFeedbackTickets_Status CHECK(Status IN(N'OPEN',N'IN_PROGRESS',N'RESOLVED',N'CLOSED',N'REOPENED'))
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontFeedbackTickets_Reference' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackTickets')) CREATE UNIQUE INDEX UX_StorefrontFeedbackTickets_Reference ON commerce.StorefrontFeedbackTickets(TenantId,TicketReference);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackTickets_TenantStatus' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackTickets')) CREATE INDEX IX_StorefrontFeedbackTickets_TenantStatus ON commerce.StorefrontFeedbackTickets(TenantId,Status,UpdatedAtUtc DESC);

IF OBJECT_ID(N'commerce.StorefrontFeedbackTicketActivity', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontFeedbackTicketActivity
    (
        ActivityId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StorefrontFeedbackTicketActivity_Id DEFAULT NEWSEQUENTIALID(),
        TenantId UNIQUEIDENTIFIER NOT NULL,
        TicketId UNIQUEIDENTIFIER NOT NULL,
        ActivityType NVARCHAR(20) NOT NULL,
        FromStatus NVARCHAR(20) NULL,
        ToStatus NVARCHAR(20) NULL,
        CustomerReply NVARCHAR(4000) NULL,
        InternalNote NVARCHAR(4000) NULL,
        ActorUserId UNIQUEIDENTIFIER NULL,
        CreatedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontFeedbackTicketActivity_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_StorefrontFeedbackTicketActivity PRIMARY KEY(ActivityId),
        CONSTRAINT FK_StorefrontFeedbackTicketActivity_Ticket FOREIGN KEY(TicketId) REFERENCES commerce.StorefrontFeedbackTickets(TicketId)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontFeedbackTicketActivity_Ticket' AND object_id=OBJECT_ID(N'commerce.StorefrontFeedbackTicketActivity')) CREATE INDEX IX_StorefrontFeedbackTicketActivity_Ticket ON commerce.StorefrontFeedbackTicketActivity(TenantId,TicketId,CreatedAtUtc);

IF OBJECT_ID(N'commerce.StorefrontCustomerPhoneBlacklist', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontCustomerPhoneBlacklist
    (
        BlacklistId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StorefrontCustomerPhoneBlacklist_Id DEFAULT NEWSEQUENTIALID(),
        TenantId UNIQUEIDENTIFIER NOT NULL,
        NormalizedPhone NVARCHAR(20) NOT NULL,
        Reason NVARCHAR(500) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_StorefrontCustomerPhoneBlacklist_Active DEFAULT 1,
        BlockedBy UNIQUEIDENTIFIER NULL,
        BlockedAtUtc DATETIMEOFFSET NOT NULL CONSTRAINT DF_StorefrontCustomerPhoneBlacklist_Blocked DEFAULT SYSUTCDATETIME(),
        UnblockedBy UNIQUEIDENTIFIER NULL,
        UnblockedAtUtc DATETIMEOFFSET NULL,
        CONSTRAINT PK_StorefrontCustomerPhoneBlacklist PRIMARY KEY(BlacklistId)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_StorefrontCustomerPhoneBlacklist_ActivePhone' AND object_id=OBJECT_ID(N'commerce.StorefrontCustomerPhoneBlacklist')) CREATE UNIQUE INDEX UX_StorefrontCustomerPhoneBlacklist_ActivePhone ON commerce.StorefrontCustomerPhoneBlacklist(TenantId,NormalizedPhone) WHERE IsActive=1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_StorefrontCustomerPhoneBlacklist_Tenant' AND object_id=OBJECT_ID(N'commerce.StorefrontCustomerPhoneBlacklist')) CREATE INDEX IX_StorefrontCustomerPhoneBlacklist_Tenant ON commerce.StorefrontCustomerPhoneBlacklist(TenantId,IsActive,BlockedAtUtc DESC);

DECLARE @parent UNIQUEIDENTIFIER=(SELECT TOP(1)FeatureId FROM core.Features WHERE FeatureKey=N'CUSTOMERS');
DECLARE @feedback UNIQUEIDENTIFIER=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'STOREFRONT_FEEDBACK');
IF @feedback IS NULL BEGIN SET @feedback=NEWID(); INSERT core.Features(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,ParentFeatureId,Version,SortOrder,CreatedBy) VALUES(@feedback,N'STOREFRONT_FEEDBACK',N'Storefront Feedback & Complaints',N'Customer feedback and complaint management.',N'CUSTOMERS',N'ACTIVE',1,N'MODULE',@parent,N'V3',82,N'V55'); END;
DECLARE @blacklist UNIQUEIDENTIFIER=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'STOREFRONT_CUSTOMER_BLACKLIST');
IF @blacklist IS NULL BEGIN SET @blacklist=NEWID(); INSERT core.Features(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,ParentFeatureId,Version,SortOrder,CreatedBy) VALUES(@blacklist,N'STOREFRONT_CUSTOMER_BLACKLIST',N'Storefront Customer Blacklist',N'Tenant-scoped storefront order blocking by phone number.',N'CUSTOMERS',N'ACTIVE',1,N'MODULE',@parent,N'V3',83,N'V55'); END;
INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy) SELECT NEWID(),t.TenantId,f.FeatureId,0,N'Opt-in V55 storefront feature',1,N'V55' FROM core.Tenants t CROSS JOIN (SELECT @feedback FeatureId UNION ALL SELECT @blacklist) f WHERE NOT EXISTS(SELECT 1 FROM core.TenantFeatures x WHERE x.TenantId=t.TenantId AND x.FeatureId=f.FeatureId);
IF OBJECT_ID(N'core.PlanFeatures',N'U') IS NOT NULL INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy) SELECT NEWID(),p.PlanId,f.FeatureId,0,N'V55' FROM core.Plans p CROSS JOIN (SELECT @feedback FeatureId UNION ALL SELECT @blacklist) f WHERE NOT EXISTS(SELECT 1 FROM core.PlanFeatures x WHERE x.PlanId=p.PlanId AND x.FeatureId=f.FeatureId);
