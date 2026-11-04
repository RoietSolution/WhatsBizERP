/* V1 assisted order intake. Intake records are drafts; they never post sales or inventory. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'core.Features',N'U') IS NULL
   OR OBJECT_ID(N'core.PlanFeatures',N'U') IS NULL
   OR OBJECT_ID(N'core.Plans',N'U') IS NULL
   OR OBJECT_ID(N'integration.WhatsAppWebhookEvents',N'U') IS NULL
    THROW 79000,N'V79 requires the existing feature and WhatsApp webhook schemas.',1;

BEGIN TRANSACTION;

IF OBJECT_ID(N'commerce.OrderIntakeRequests',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.OrderIntakeRequests
    (
        TenantId uniqueidentifier NOT NULL,
        OrderIntakeRequestId uniqueidentifier NOT NULL,
        RequestReference nvarchar(24) NOT NULL,
        SourceChannel varchar(24) NOT NULL,
        IntakeStatus varchar(24) NOT NULL CONSTRAINT DF_OrderIntake_Status DEFAULT('NEW'),
        CustomerId uniqueidentifier NULL,
        CustomerName nvarchar(200) NULL,
        CustomerMobile nvarchar(30) NULL,
        OriginalRequest nvarchar(4000) NOT NULL,
        Notes nvarchar(2000) NULL,
        FulfilmentPreference varchar(16) NULL,
        RequestedAtUtc datetime2(0) NULL,
        ProviderMessageId nvarchar(256) NULL,
        ConversationReference nvarchar(256) NULL,
        LastCustomerInteractionAtUtc datetime2(0) NULL,
        AssignedStaffId uniqueidentifier NULL,
        ConfirmationChannel varchar(24) NULL,
        CustomerConfirmedAtUtc datetime2(0) NULL,
        ConvertedOrderId uniqueidentifier NULL,
        CreatedBy nvarchar(256) NULL,
        UpdatedBy nvarchar(256) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_OrderIntake_Created DEFAULT(SYSUTCDATETIME()),
        UpdatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_OrderIntake_Updated DEFAULT(SYSUTCDATETIME()),
        Version rowversion NOT NULL,
        CONSTRAINT PK_OrderIntakeRequests PRIMARY KEY(TenantId,OrderIntakeRequestId),
        CONSTRAINT FK_OrderIntakeRequests_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT UQ_OrderIntakeRequests_Reference UNIQUE(TenantId,RequestReference),
        CONSTRAINT CK_OrderIntakeRequests_Channel CHECK(SourceChannel IN('WHATSAPP','PHONE_CALL','MANUAL_LIST','WHATSAPP_VOICE','SHOPPING_LIST_IMAGE','CUSTOMER_APP_UPLOAD')),
        CONSTRAINT CK_OrderIntakeRequests_Status CHECK(IntakeStatus IN('NEW','UNDER_REVIEW','AWAITING_CUSTOMER','CONFIRMED','CONVERTED','REJECTED','CANCELLED')),
        CONSTRAINT CK_OrderIntakeRequests_Fulfilment CHECK(FulfilmentPreference IS NULL OR FulfilmentPreference IN('PICKUP','DELIVERY')),
        CONSTRAINT CK_OrderIntakeRequests_Confirmation CHECK(ConfirmationChannel IS NULL OR ConfirmationChannel IN('WHATSAPP','PHONE_CALL','IN_PERSON','CUSTOMER_APP'))
    );
END;

IF OBJECT_ID(N'commerce.OrderIntakeItems',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.OrderIntakeItems
    (
        TenantId uniqueidentifier NOT NULL,
        OrderIntakeRequestId uniqueidentifier NOT NULL,
        OrderIntakeItemId uniqueidentifier NOT NULL,
        RequestedText nvarchar(300) NOT NULL,
        RequestedQuantity decimal(18,4) NULL,
        RequestedUnit nvarchar(60) NULL,
        MatchedProductId uniqueidentifier NULL,
        MatchStatus varchar(16) NOT NULL CONSTRAINT DF_OrderIntakeItem_Match DEFAULT('UNMATCHED'),
        ItemNotes nvarchar(500) NULL,
        CustomerApprovedSubstitution bit NOT NULL CONSTRAINT DF_OrderIntakeItem_Substitution DEFAULT(0),
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_OrderIntakeItem_Created DEFAULT(SYSUTCDATETIME()),
        UpdatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_OrderIntakeItem_Updated DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_OrderIntakeItems PRIMARY KEY(TenantId,OrderIntakeItemId),
        CONSTRAINT FK_OrderIntakeItems_Request FOREIGN KEY(TenantId,OrderIntakeRequestId) REFERENCES commerce.OrderIntakeRequests(TenantId,OrderIntakeRequestId),
        CONSTRAINT CK_OrderIntakeItems_Quantity CHECK(RequestedQuantity IS NULL OR RequestedQuantity>0),
        CONSTRAINT CK_OrderIntakeItems_Match CHECK(MatchStatus IN('UNMATCHED','REVIEW','MATCHED','UNAVAILABLE'))
    );
END;

IF OBJECT_ID(N'commerce.OrderIntakeEvents',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.OrderIntakeEvents
    (
        TenantId uniqueidentifier NOT NULL,
        OrderIntakeEventId uniqueidentifier NOT NULL,
        OrderIntakeRequestId uniqueidentifier NOT NULL,
        EventType varchar(24) NOT NULL,
        PreviousStatus varchar(24) NULL,
        NewStatus varchar(24) NULL,
        Actor nvarchar(256) NULL,
        Note nvarchar(1000) NULL,
        CreatedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_OrderIntakeEvent_Created DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT PK_OrderIntakeEvents PRIMARY KEY(TenantId,OrderIntakeEventId),
        CONSTRAINT FK_OrderIntakeEvents_Request FOREIGN KEY(TenantId,OrderIntakeRequestId) REFERENCES commerce.OrderIntakeRequests(TenantId,OrderIntakeRequestId)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'UX_OrderIntakeRequests_ProviderMessage')
    CREATE UNIQUE INDEX UX_OrderIntakeRequests_ProviderMessage ON commerce.OrderIntakeRequests(TenantId,ProviderMessageId) WHERE ProviderMessageId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'IX_OrderIntakeRequests_TenantStatusDate')
    CREATE INDEX IX_OrderIntakeRequests_TenantStatusDate ON commerce.OrderIntakeRequests(TenantId,IntakeStatus,CreatedAtUtc DESC) INCLUDE(SourceChannel,RequestReference,CustomerName,AssignedStaffId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'UX_OrderIntakeRequests_ConvertedOrder')
    CREATE UNIQUE INDEX UX_OrderIntakeRequests_ConvertedOrder ON commerce.OrderIntakeRequests(TenantId,ConvertedOrderId) WHERE ConvertedOrderId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeItems') AND name=N'IX_OrderIntakeItems_Request')
    CREATE INDEX IX_OrderIntakeItems_Request ON commerce.OrderIntakeItems(TenantId,OrderIntakeRequestId,CreatedAtUtc);

DECLARE @parent uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'V1');
IF @parent IS NULL OR NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'POS')
    OR NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'PRODUCTS')
    OR NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'CUSTOMERS')
    THROW 79001,N'V79 requires the existing V1, POS, Products and Customers feature definitions.',1;

IF NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'MULTI_CHANNEL_ORDER_INTAKE')
    INSERT core.Features(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,FeatureType,ParentFeatureId,Version,SortOrder,CreatedBy)
    VALUES(NEWID(),N'MULTI_CHANNEL_ORDER_INTAKE',N'Multi-Channel Order Intake',N'Retailer-reviewed WhatsApp, phone-call and manual shopping-list requests. Intake never posts an order automatically.',N'POS',N'ACTIVE',1,N'MODULE',@parent,N'V1',90,N'V79');

INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy)
SELECT NEWID(),p.PlanId,f.FeatureId,CONVERT(bit,CASE WHEN
    EXISTS(SELECT 1 FROM core.PlanFeatures x JOIN core.Features xf ON xf.FeatureId=x.FeatureId WHERE x.PlanId=p.PlanId AND xf.FeatureKey=N'POS' AND x.IsEnabled=1)
    AND EXISTS(SELECT 1 FROM core.PlanFeatures x JOIN core.Features xf ON xf.FeatureId=x.FeatureId WHERE x.PlanId=p.PlanId AND xf.FeatureKey=N'PRODUCTS' AND x.IsEnabled=1)
    AND EXISTS(SELECT 1 FROM core.PlanFeatures x JOIN core.Features xf ON xf.FeatureId=x.FeatureId WHERE x.PlanId=p.PlanId AND xf.FeatureKey=N'CUSTOMERS' AND x.IsEnabled=1)
    THEN 1 ELSE 0 END),N'V79'
FROM core.Plans p CROSS JOIN core.Features f
WHERE f.FeatureKey=N'MULTI_CHANNEL_ORDER_INTAKE'
  AND NOT EXISTS(SELECT 1 FROM core.PlanFeatures x WHERE x.PlanId=p.PlanId AND x.FeatureId=f.FeatureId);

COMMIT TRANSACTION;
