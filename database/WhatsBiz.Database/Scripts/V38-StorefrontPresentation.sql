/* V38 - tenant-owned storefront branding, category imagery and scheduled banners. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.StorefrontMedia', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontMedia(
        MediaId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontMedia PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        ResourceType nvarchar(30) NOT NULL,
        FileName nvarchar(255) NOT NULL,
        ContentType nvarchar(100) NOT NULL,
        ThumbnailContentType nvarchar(100) NOT NULL,
        StorageProvider nvarchar(20) NOT NULL,
        ObjectKey nvarchar(1024) NULL,
        ThumbnailObjectKey nvarchar(1024) NULL,
        ImageData varbinary(max) NULL,
        ThumbnailData varbinary(max) NULL,
        ContentHash varchar(64) NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontMedia_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_StorefrontMedia_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT CK_StorefrontMedia_Resource CHECK(ResourceType IN(N'logo',N'category',N'banner-primary',N'banner-secondary')),
        CONSTRAINT CK_StorefrontMedia_Storage CHECK((StorageProvider=N'DATABASE' AND ImageData IS NOT NULL) OR (StorageProvider<>N'DATABASE' AND ObjectKey IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_StorefrontMedia_TenantMedia ON commerce.StorefrontMedia(TenantId,MediaId);
END;

IF OBJECT_ID(N'commerce.StorefrontConfigurations', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontConfigurations(
        TenantId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontConfigurations PRIMARY KEY,
        LogoMediaId uniqueidentifier NULL,
        Tagline nvarchar(250) NULL,
        AccentColor nvarchar(20) NULL,
        DeliveryMessage nvarchar(250) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontConfigurations_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontConfigurations_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_StorefrontConfigurations_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontConfigurations_Logo FOREIGN KEY(TenantId,LogoMediaId) REFERENCES commerce.StorefrontMedia(TenantId,MediaId)
    );
END;

IF OBJECT_ID(N'commerce.StorefrontBanners', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontBanners(
        BannerId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontBanners PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        Slot nvarchar(20) NOT NULL,
        MediaId uniqueidentifier NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_StorefrontBanners_Enabled DEFAULT(0),
        StartsAt datetimeoffset NULL,
        EndsAt datetimeoffset NULL,
        Title nvarchar(150) NULL,
        Subtitle nvarchar(300) NULL,
        TargetUrl nvarchar(500) NULL,
        DisplayOrder int NOT NULL CONSTRAINT DF_StorefrontBanners_Order DEFAULT(0),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontBanners_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontBanners_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_StorefrontBanners_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_StorefrontBanners_Media FOREIGN KEY(TenantId,MediaId) REFERENCES commerce.StorefrontMedia(TenantId,MediaId),
        CONSTRAINT CK_StorefrontBanners_Slot CHECK(Slot IN(N'PRIMARY',N'SECONDARY')),
        CONSTRAINT CK_StorefrontBanners_Schedule CHECK(StartsAt IS NULL OR EndsAt IS NULL OR StartsAt<=EndsAt)
    );
    CREATE UNIQUE INDEX UX_StorefrontBanners_TenantSlot ON commerce.StorefrontBanners(TenantId,Slot);
END;

IF OBJECT_ID(N'commerce.StorefrontCategoryImages', N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontCategoryImages(
        TenantId uniqueidentifier NOT NULL,
        ProductCategoryId uniqueidentifier NOT NULL,
        MediaId uniqueidentifier NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontCategoryImages_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_StorefrontCategoryImages_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_StorefrontCategoryImages PRIMARY KEY(TenantId,ProductCategoryId),
        CONSTRAINT FK_StorefrontCategoryImages_TenantCategory FOREIGN KEY(TenantId,ProductCategoryId) REFERENCES master.TenantProductCategories(TenantId,ProductCategoryId),
        CONSTRAINT FK_StorefrontCategoryImages_Media FOREIGN KEY(TenantId,MediaId) REFERENCES commerce.StorefrontMedia(TenantId,MediaId)
    );
    CREATE UNIQUE INDEX UX_StorefrontCategoryImages_TenantMedia ON commerce.StorefrontCategoryImages(TenantId,MediaId);
END;
GO

CREATE OR ALTER TRIGGER commerce.TR_StorefrontMedia_TenantGuard ON commerce.StorefrontMedia AFTER INSERT,UPDATE,DELETE AS
BEGIN
  SET NOCOUNT ON; DECLARE @tenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
  IF @tenant IS NULL OR EXISTS(SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted EXCEPT SELECT @tenant) THROW 51000,'Tenant context mismatch.',1;
END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontConfigurations_TenantGuard ON commerce.StorefrontConfigurations AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON; DECLARE @tenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @tenant IS NULL OR EXISTS(SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted EXCEPT SELECT @tenant) THROW 51000,'Tenant context mismatch.',1; END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontBanners_TenantGuard ON commerce.StorefrontBanners AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON; DECLARE @tenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @tenant IS NULL OR EXISTS(SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted EXCEPT SELECT @tenant) THROW 51000,'Tenant context mismatch.',1; END;
GO
CREATE OR ALTER TRIGGER commerce.TR_StorefrontCategoryImages_TenantGuard ON commerce.StorefrontCategoryImages AFTER INSERT,UPDATE,DELETE AS
BEGIN SET NOCOUNT ON; DECLARE @tenant uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId')); IF @tenant IS NULL OR EXISTS(SELECT TenantId FROM inserted UNION SELECT TenantId FROM deleted EXCEPT SELECT @tenant) THROW 51000,'Tenant context mismatch.',1; END;
GO

/* Storefront POS orders were introduced after the original WhatsApp-only source constraint. */
IF OBJECT_ID(N'integration.WhatsAppCommerceOrders', N'U') IS NOT NULL
BEGIN
    IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'integration.WhatsAppCommerceOrders') AND name=N'CK_WhatsAppCommerceOrders_Source' AND definition NOT LIKE N'%STOREFRONT%')
    BEGIN
        ALTER TABLE integration.WhatsAppCommerceOrders DROP CONSTRAINT CK_WhatsAppCommerceOrders_Source;
        ALTER TABLE integration.WhatsAppCommerceOrders WITH CHECK ADD CONSTRAINT CK_WhatsAppCommerceOrders_Source CHECK(SourceChannel IN(N'WHATSAPP_DEMO',N'WHATSAPP',N'STOREFRONT'));
    END;
    ELSE IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'integration.WhatsAppCommerceOrders') AND name=N'CK_WhatsAppCommerceOrders_Source')
        ALTER TABLE integration.WhatsAppCommerceOrders WITH CHECK ADD CONSTRAINT CK_WhatsAppCommerceOrders_Source CHECK(SourceChannel IN(N'WHATSAPP_DEMO',N'WHATSAPP',N'STOREFRONT'));
END;
GO
