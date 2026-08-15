SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @TenantId uniqueidentifier,
        @FeatureId uniqueidentifier,
        @CompanyName nvarchar(250);

SELECT @TenantId=TenantId
FROM core.Tenants
WHERE TenantKey=N'DEFAULT' AND Name=N'Default V1 Tenant' AND IsActive=1;

SELECT TOP(1) @CompanyName=CompanyName
FROM admin.Companies
WHERE IsActive=1
ORDER BY CreatedOn;

SELECT @FeatureId=FeatureId
FROM core.Features
WHERE FeatureKey=N'WHATSAPP_COMMERCE' AND IsActive=1;

IF @TenantId IS NULL OR @FeatureId IS NULL OR @CompanyName<>N'WhatsBiz ERP Demo'
    THROW 51400,N'The intended WhatsBiz development demo tenant could not be resolved.',1;

MERGE core.TenantFeatures AS target
USING (SELECT @TenantId TenantId,@FeatureId FeatureId) AS source
ON target.TenantId=source.TenantId AND target.FeatureId=source.FeatureId
WHEN MATCHED THEN UPDATE SET IsEnabled=1,IsActive=1,StartDate=COALESCE(target.StartDate,SYSUTCDATETIME()),EndDate=NULL,
    Reason=N'WhatsApp Commerce development presentation tenant',ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'WC-DEMO-003 deployment'
WHEN NOT MATCHED THEN INSERT(TenantFeatureId,TenantId,FeatureId,IsEnabled,StartDate,EndDate,Reason,IsActive,CreatedBy)
    VALUES(NEWID(),source.TenantId,source.FeatureId,1,SYSUTCDATETIME(),NULL,N'WhatsApp Commerce development presentation tenant',1,N'WC-DEMO-003 deployment');

MERGE integration.WhatsAppConfigurations AS target
USING (SELECT @TenantId TenantId) AS source
ON target.TenantId=source.TenantId
WHEN MATCHED THEN UPDATE SET ProviderMode=N'MOCK',IsEnabled=1,ConnectionStatus=N'CONFIGURED',
    BusinessDisplayName=COALESCE(target.BusinessDisplayName,@CompanyName),LastError=NULL,
    ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'WC-DEMO-003 deployment'
WHEN NOT MATCHED THEN INSERT(WhatsAppConfigurationId,TenantId,BusinessDisplayName,ProviderMode,IsEnabled,ConnectionStatus,CreatedBy)
    VALUES(NEWID(),source.TenantId,@CompanyName,N'MOCK',1,N'CONFIGURED',N'WC-DEMO-003 deployment');

COMMIT TRANSACTION;
