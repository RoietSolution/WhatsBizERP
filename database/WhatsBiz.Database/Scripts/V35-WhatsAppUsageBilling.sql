SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'integration.WhatsAppWebhookEvents',N'PricingCategory') IS NULL
    ALTER TABLE integration.WhatsAppWebhookEvents ADD PricingCategory nvarchar(20) NULL;
IF COL_LENGTH(N'integration.WhatsAppWebhookEvents',N'MetaBillable') IS NULL
    ALTER TABLE integration.WhatsAppWebhookEvents ADD MetaBillable bit NULL;
IF COL_LENGTH(N'integration.WhatsAppWebhookEvents',N'MetaPricingModel') IS NULL
    ALTER TABLE integration.WhatsAppWebhookEvents ADD MetaPricingModel nvarchar(40) NULL;

IF OBJECT_ID(N'integration.MetaWhatsAppPricing',N'U') IS NULL
BEGIN
    CREATE TABLE integration.MetaWhatsAppPricing
    (
        MetaWhatsAppPricingId uniqueidentifier NOT NULL CONSTRAINT PK_MetaWhatsAppPricing PRIMARY KEY,
        MarketCode nvarchar(10) NOT NULL,
        CallingCode nvarchar(8) NOT NULL,
        MessageCategory nvarchar(20) NOT NULL,
        Currency char(3) NOT NULL,
        Rate decimal(19,6) NOT NULL,
        EffectiveFrom datetimeoffset NOT NULL,
        EffectiveTo datetimeoffset NULL,
        IsActive bit NOT NULL CONSTRAINT DF_MetaWhatsAppPricing_IsActive DEFAULT(1),
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_MetaWhatsAppPricing_CreatedOn DEFAULT SYSUTCDATETIME(),
        ModifiedOn datetimeoffset NULL,
        CONSTRAINT CK_MetaWhatsAppPricing_Category CHECK(MessageCategory IN(N'MARKETING',N'UTILITY',N'AUTHENTICATION',N'SERVICE')),
        CONSTRAINT CK_MetaWhatsAppPricing_Currency CHECK(Currency=UPPER(Currency) AND LEN(Currency)=3),
        CONSTRAINT CK_MetaWhatsAppPricing_CallingCode CHECK(CallingCode NOT LIKE N'%[^0-9]%' AND LEN(CallingCode) BETWEEN 1 AND 7),
        CONSTRAINT CK_MetaWhatsAppPricing_Rate CHECK(Rate>=0),
        CONSTRAINT CK_MetaWhatsAppPricing_Period CHECK(EffectiveTo IS NULL OR EffectiveTo>EffectiveFrom)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.MetaWhatsAppPricing') AND name=N'IX_MetaWhatsAppPricing_Resolve')
    CREATE INDEX IX_MetaWhatsAppPricing_Resolve ON integration.MetaWhatsAppPricing(MessageCategory,IsActive,EffectiveFrom,EffectiveTo,CallingCode)
        INCLUDE(MarketCode,Currency,Rate);

IF OBJECT_ID(N'integration.WhatsAppMessageUsage',N'U') IS NULL
BEGIN
    CREATE TABLE integration.WhatsAppMessageUsage
    (
        WhatsAppMessageUsageId uniqueidentifier NOT NULL CONSTRAINT PK_WhatsAppMessageUsage PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        WabaId nvarchar(50) NOT NULL,
        PhoneNumberId nvarchar(50) NOT NULL,
        MetaMessageId nvarchar(250) NOT NULL,
        RecipientNumber nvarchar(20) NOT NULL,
        RecipientMarket nvarchar(10) NULL,
        MessageCategory nvarchar(20) NOT NULL CONSTRAINT DF_WhatsAppMessageUsage_Category DEFAULT(N'UNKNOWN'),
        TemplateName nvarchar(200) NULL,
        SentAt datetimeoffset NOT NULL,
        DeliveredAt datetimeoffset NULL,
        DeliveryStatus nvarchar(30) NOT NULL CONSTRAINT DF_WhatsAppMessageUsage_Status DEFAULT(N'SENT'),
        MetaBillable bit NULL,
        MetaPricingModel nvarchar(40) NULL,
        MetaWhatsAppPricingId uniqueidentifier NULL,
        EstimatedMetaCost decimal(19,6) NULL,
        Currency char(3) NULL,
        PricingEffectiveFrom datetimeoffset NULL,
        BillingPeriod date NULL,
        CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_WhatsAppMessageUsage_CreatedOn DEFAULT SYSUTCDATETIME(),
        ModifiedOn datetimeoffset NULL,
        CONSTRAINT FK_WhatsAppMessageUsage_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_WhatsAppMessageUsage_Pricing FOREIGN KEY(MetaWhatsAppPricingId) REFERENCES integration.MetaWhatsAppPricing(MetaWhatsAppPricingId),
        CONSTRAINT UQ_WhatsAppMessageUsage_TenantMessage UNIQUE(TenantId,MetaMessageId),
        CONSTRAINT CK_WhatsAppMessageUsage_Category CHECK(MessageCategory IN(N'MARKETING',N'UTILITY',N'AUTHENTICATION',N'SERVICE',N'UNKNOWN')),
        CONSTRAINT CK_WhatsAppMessageUsage_Status CHECK(DeliveryStatus IN(N'SENT',N'DELIVERED',N'READ',N'FAILED',N'DELETED',N'UNKNOWN')),
        CONSTRAINT CK_WhatsAppMessageUsage_Cost CHECK(EstimatedMetaCost IS NULL OR EstimatedMetaCost>=0),
        CONSTRAINT CK_WhatsAppMessageUsage_BillingPeriod CHECK(BillingPeriod IS NULL OR DAY(BillingPeriod)=1)
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppMessageUsage') AND name=N'IX_WhatsAppMessageUsage_TenantPeriodCategory')
    CREATE INDEX IX_WhatsAppMessageUsage_TenantPeriodCategory ON integration.WhatsAppMessageUsage(TenantId,BillingPeriod,MessageCategory)
        INCLUDE(DeliveryStatus,EstimatedMetaCost,Currency,DeliveredAt);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'integration.WhatsAppMessageUsage') AND name=N'IX_WhatsAppMessageUsage_TenantAssets')
    CREATE INDEX IX_WhatsAppMessageUsage_TenantAssets ON integration.WhatsAppMessageUsage(TenantId,WabaId,PhoneNumberId,SentAt DESC);

COMMIT TRANSACTION;
GO

CREATE OR ALTER TRIGGER integration.TR_MetaWhatsAppPricing_NoOverlap
ON integration.MetaWhatsAppPricing
AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN integration.MetaWhatsAppPricing p ON p.MetaWhatsAppPricingId<>i.MetaWhatsAppPricingId
          AND p.IsActive=1 AND i.IsActive=1
          AND p.MarketCode=i.MarketCode AND p.MessageCategory=i.MessageCategory
          AND p.EffectiveFrom<COALESCE(i.EffectiveTo,CONVERT(datetimeoffset,'9999-12-31T23:59:59+00:00'))
          AND i.EffectiveFrom<COALESCE(p.EffectiveTo,CONVERT(datetimeoffset,'9999-12-31T23:59:59+00:00'))
        UNION ALL
        SELECT 1 FROM inserted a JOIN inserted b ON a.MetaWhatsAppPricingId<b.MetaWhatsAppPricingId
          AND a.IsActive=1 AND b.IsActive=1 AND a.MarketCode=b.MarketCode AND a.MessageCategory=b.MessageCategory
          AND a.EffectiveFrom<COALESCE(b.EffectiveTo,CONVERT(datetimeoffset,'9999-12-31T23:59:59+00:00'))
          AND b.EffectiveFrom<COALESCE(a.EffectiveTo,CONVERT(datetimeoffset,'9999-12-31T23:59:59+00:00'))
    )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51170,N'Active Meta WhatsApp pricing periods cannot overlap for the same market and category.',1;
    END;
END;
GO
