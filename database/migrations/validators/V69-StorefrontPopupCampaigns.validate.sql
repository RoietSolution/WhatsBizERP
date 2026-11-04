SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.StorefrontPopupCampaigns',N'U') IS NULL
    THROW 69001, N'V69 campaign table is missing.', 1;
IF EXISTS(SELECT 1 FROM (VALUES('TenantId'),('CampaignId'),('StartsAtUtc'),('EndsAtUtc'),('TimeZoneId'),('Frequency'),('ScheduleId'),('OnlyWhenOrderingOpen'),('MediaId'),('HtmlContent'),('DestinationId'),('UpdatedAtUtc')) c(Name)
    WHERE COL_LENGTH(N'commerce.StorefrontPopupCampaigns',c.Name) IS NULL)
    THROW 69002, N'V69 required column missing.', 1;
IF (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.StorefrontPopupCampaigns') AND name IN('FK_PopupCampaign_Tenant','FK_PopupCampaign_Schedule','FK_PopupCampaign_Media') AND is_disabled=0 AND is_not_trusted=0)<>3
    THROW 69003, N'V69 tenant foreign keys missing or untrusted.', 1;
IF (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.StorefrontPopupCampaigns') AND is_disabled=0 AND is_not_trusted=0)<>5
    THROW 69004, N'V69 campaign constraints missing or untrusted.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.StorefrontPopupCampaigns') AND name=N'IX_PopupCampaign_TenantActivePeriod')
    THROW 69005, N'V69 campaign index missing.', 1;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commerce.StorefrontMedia') AND name=N'CK_StorefrontMedia_Resource' AND is_disabled=0 AND is_not_trusted=0 AND CHARINDEX(N'''campaign''',definition)>0)
    THROW 69006, N'V69 campaign media resource is not supported.', 1;
SELECT N'V69_STOREFRONT_POPUP_CAMPAIGNS_VALID' AS ValidationResult;
