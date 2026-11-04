/* V69: informational tenant campaigns only; never changes order/pricing eligibility. */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'commerce.StorefrontAvailabilitySchedules',N'U') IS NULL
    THROW 69000, N'V69 requires V67 availability schedules.', 1;
DECLARE @resourceDefinition nvarchar(max)=(SELECT definition FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia') AND name=N'CK_StorefrontMedia_Resource');
IF @resourceDefinition IS NULL
    THROW 69006, N'V69 requires the existing storefront media resource constraint.', 1;
IF CHARINDEX(N'''campaign''',@resourceDefinition)=0
BEGIN
    -- Preserve the existing allowed resources rather than replacing historical media semantics.
    DECLARE @resourceSql nvarchar(max)=N'ALTER TABLE commerce.StorefrontMedia DROP CONSTRAINT CK_StorefrontMedia_Resource; ALTER TABLE commerce.StorefrontMedia WITH CHECK ADD CONSTRAINT CK_StorefrontMedia_Resource CHECK(('+@resourceDefinition+N') OR ResourceType=N''campaign'');';
    EXEC sys.sp_executesql @resourceSql;
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StorefrontAvailabilitySchedules') AND name=N'UX_AvailabilitySchedules_TenantSchedule')
    CREATE UNIQUE INDEX UX_AvailabilitySchedules_TenantSchedule ON commerce.StorefrontAvailabilitySchedules(TenantId,ScheduleId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StorefrontMedia') AND name=N'UX_StorefrontMedia_TenantMedia')
    CREATE UNIQUE INDEX UX_StorefrontMedia_TenantMedia ON commerce.StorefrontMedia(TenantId,MediaId);
GO
IF OBJECT_ID(N'commerce.StorefrontPopupCampaigns',N'U') IS NULL
BEGIN
    CREATE TABLE commerce.StorefrontPopupCampaigns(
        CampaignId uniqueidentifier NOT NULL CONSTRAINT PK_StorefrontPopupCampaigns PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        Name nvarchar(150) NOT NULL, Headline nvarchar(200) NOT NULL, HtmlContent nvarchar(max) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_PopupCampaign_Active DEFAULT(0),
        StartsAtUtc datetimeoffset(0) NOT NULL, EndsAtUtc datetimeoffset(0) NOT NULL,
        TimeZoneId nvarchar(100) NOT NULL, Frequency varchar(12) NOT NULL,
        Priority int NOT NULL CONSTRAINT DF_PopupCampaign_Priority DEFAULT(0),
        CtaText nvarchar(80) NULL, DestinationType varchar(10) NULL, DestinationId uniqueidentifier NULL,
        ScheduleId uniqueidentifier NULL, OnlyWhenOrderingOpen bit NOT NULL CONSTRAINT DF_PopupCampaign_OrderOpen DEFAULT(0),
        MediaId uniqueidentifier NULL, UpdatedAtUtc datetimeoffset(0) NOT NULL CONSTRAINT DF_PopupCampaign_Updated DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT FK_PopupCampaign_Tenant FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
        CONSTRAINT FK_PopupCampaign_Schedule FOREIGN KEY(TenantId,ScheduleId) REFERENCES commerce.StorefrontAvailabilitySchedules(TenantId,ScheduleId),
        CONSTRAINT FK_PopupCampaign_Media FOREIGN KEY(TenantId,MediaId) REFERENCES commerce.StorefrontMedia(TenantId,MediaId),
        CONSTRAINT CK_PopupCampaign_Period CHECK(EndsAtUtc>StartsAtUtc),
        CONSTRAINT CK_PopupCampaign_Frequency CHECK(Frequency IN('VISIT','DAY','CAMPAIGN')),
        CONSTRAINT CK_PopupCampaign_Link CHECK(OnlyWhenOrderingOpen=0 OR ScheduleId IS NOT NULL),
        CONSTRAINT CK_PopupCampaign_Destination CHECK((DestinationType IS NULL AND DestinationId IS NULL) OR (DestinationType IN('CATEGORY','PRODUCT','OFFER') AND DestinationId IS NOT NULL)),
        CONSTRAINT CK_PopupCampaign_Name CHECK(LEN(LTRIM(RTRIM(Name)))>0 AND LEN(LTRIM(RTRIM(Headline)))>0)
    );
    CREATE INDEX IX_PopupCampaign_TenantActivePeriod ON commerce.StorefrontPopupCampaigns(TenantId,IsActive,StartsAtUtc,EndsAtUtc) INCLUDE(Priority,ScheduleId);
END;
