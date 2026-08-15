SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'core.Tenants', N'U') IS NULL CREATE TABLE core.Tenants (
 TenantId uniqueidentifier NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
 TenantKey nvarchar(100) NOT NULL CONSTRAINT UQ_Tenants_TenantKey UNIQUE,
 Name nvarchar(200) NOT NULL, IsActive bit NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT(1),
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_Tenants_CreatedOn DEFAULT(SYSUTCDATETIME()), CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL);

IF OBJECT_ID(N'core.Plans', N'U') IS NULL CREATE TABLE core.Plans (
 PlanId uniqueidentifier NOT NULL CONSTRAINT PK_Plans PRIMARY KEY,
 PlanKey nvarchar(100) NOT NULL CONSTRAINT UQ_Plans_PlanKey UNIQUE,
 Name nvarchar(200) NOT NULL, IsActive bit NOT NULL CONSTRAINT DF_Plans_IsActive DEFAULT(1),
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_Plans_CreatedOn DEFAULT(SYSUTCDATETIME()), CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL);

IF OBJECT_ID(N'core.Features', N'U') IS NULL CREATE TABLE core.Features (
 FeatureId uniqueidentifier NOT NULL CONSTRAINT PK_Features PRIMARY KEY,
 FeatureKey nvarchar(100) NOT NULL, Name nvarchar(200) NOT NULL, Description nvarchar(1000) NULL,
 ModuleKey nvarchar(100) NOT NULL, ReleaseState nvarchar(20) NOT NULL, IsActive bit NOT NULL,
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_Features_CreatedOn DEFAULT(SYSUTCDATETIME()), CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL,
 CONSTRAINT UQ_Features_FeatureKey UNIQUE(FeatureKey),
 CONSTRAINT CK_Features_ReleaseState CHECK(ReleaseState IN ('INTERNAL','BETA','ACTIVE','DISABLED')));

IF OBJECT_ID(N'core.PlanFeatures', N'U') IS NULL CREATE TABLE core.PlanFeatures (
 PlanFeatureId uniqueidentifier NOT NULL CONSTRAINT PK_PlanFeatures PRIMARY KEY,
 PlanId uniqueidentifier NOT NULL, FeatureId uniqueidentifier NOT NULL, IsEnabled bit NOT NULL,
 LimitValue nvarchar(200) NULL,
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_PlanFeatures_CreatedOn DEFAULT(SYSUTCDATETIME()), CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL,
 CONSTRAINT UQ_PlanFeatures_PlanFeature UNIQUE(PlanId,FeatureId),
 CONSTRAINT FK_PlanFeatures_Plans FOREIGN KEY(PlanId) REFERENCES core.Plans(PlanId),
 CONSTRAINT FK_PlanFeatures_Features FOREIGN KEY(FeatureId) REFERENCES core.Features(FeatureId));

IF OBJECT_ID(N'core.Subscriptions', N'U') IS NULL CREATE TABLE core.Subscriptions (
 SubscriptionId uniqueidentifier NOT NULL CONSTRAINT PK_Subscriptions PRIMARY KEY,
 TenantId uniqueidentifier NOT NULL, PlanId uniqueidentifier NOT NULL,
 StartDate datetimeoffset NOT NULL, EndDate datetimeoffset NULL, IsActive bit NOT NULL,
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_Subscriptions_CreatedOn DEFAULT(SYSUTCDATETIME()), CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL,
 CONSTRAINT CK_Subscriptions_Dates CHECK(EndDate IS NULL OR EndDate >= StartDate),
 CONSTRAINT FK_Subscriptions_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
 CONSTRAINT FK_Subscriptions_Plans FOREIGN KEY(PlanId) REFERENCES core.Plans(PlanId));

IF OBJECT_ID(N'core.TenantFeatures', N'U') IS NULL CREATE TABLE core.TenantFeatures (
 TenantFeatureId uniqueidentifier NOT NULL CONSTRAINT PK_TenantFeatures PRIMARY KEY,
 TenantId uniqueidentifier NOT NULL, FeatureId uniqueidentifier NOT NULL, IsEnabled bit NOT NULL,
 StartDate datetimeoffset NULL, EndDate datetimeoffset NULL, Reason nvarchar(1000) NULL, IsActive bit NOT NULL CONSTRAINT DF_TenantFeatures_IsActive DEFAULT(1),
 CreatedOn datetimeoffset NOT NULL CONSTRAINT DF_TenantFeatures_CreatedOn DEFAULT(SYSUTCDATETIME()), CreatedBy nvarchar(256) NULL,
 ModifiedOn datetimeoffset NULL, ModifiedBy nvarchar(256) NULL,
 CONSTRAINT UQ_TenantFeatures_TenantFeature UNIQUE(TenantId,FeatureId),
 CONSTRAINT CK_TenantFeatures_Dates CHECK(EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate),
 CONSTRAINT FK_TenantFeatures_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId),
 CONSTRAINT FK_TenantFeatures_Features FOREIGN KEY(FeatureId) REFERENCES core.Features(FeatureId));

IF COL_LENGTH(N'core.Users', N'TenantId') IS NULL ALTER TABLE core.Users ADD TenantId uniqueidentifier NULL;

DECLARE @TenantId uniqueidentifier='11111111-1111-1111-1111-111111111111', @PlanId uniqueidentifier='22222222-2222-2222-2222-222222222222';
IF NOT EXISTS(SELECT 1 FROM core.Tenants WHERE TenantId=@TenantId) INSERT core.Tenants(TenantId,TenantKey,Name,IsActive,CreatedBy) VALUES(@TenantId,'DEFAULT','Default V1 Tenant',1,'V2 deployment');
IF NOT EXISTS(SELECT 1 FROM core.Plans WHERE PlanId=@PlanId) INSERT core.Plans(PlanId,PlanKey,Name,IsActive,CreatedBy) VALUES(@PlanId,'V1_DEFAULT','V1 Default',1,'V2 deployment');
IF NOT EXISTS(SELECT 1 FROM core.Subscriptions WHERE TenantId=@TenantId AND PlanId=@PlanId) INSERT core.Subscriptions(SubscriptionId,TenantId,PlanId,StartDate,IsActive,CreatedBy) VALUES(NEWID(),@TenantId,@PlanId,'2000-01-01',1,'V2 deployment');

DECLARE @Seed TABLE(FeatureKey nvarchar(100),Name nvarchar(200),Description nvarchar(1000),ModuleKey nvarchar(100));
INSERT @Seed VALUES
('WHATSAPP_COMMERCE','WhatsApp Commerce','WhatsApp commerce capabilities.','WHATSAPP'),
('ADVANCED_WAREHOUSE','Advanced Warehouse','Advanced warehouse capabilities.','WAREHOUSE'),
('AI_ASSISTANT','AI Assistant','AI-assisted capabilities.','AI'),
('INTEGRATIONS','Integrations','External integration capabilities.','INTEGRATIONS');
MERGE core.Features AS t USING @Seed AS s ON t.FeatureKey=s.FeatureKey
WHEN MATCHED THEN UPDATE SET Name=s.Name,Description=s.Description,ModuleKey=s.ModuleKey
WHEN NOT MATCHED THEN INSERT(FeatureId,FeatureKey,Name,Description,ModuleKey,ReleaseState,IsActive,CreatedBy) VALUES(NEWID(),s.FeatureKey,s.Name,s.Description,s.ModuleKey,'INTERNAL',1,'V2 deployment');
INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled,CreatedBy)
SELECT NEWID(),@PlanId,f.FeatureId,0,'V2 deployment' FROM core.Features f JOIN @Seed s ON s.FeatureKey=f.FeatureKey
WHERE NOT EXISTS(SELECT 1 FROM core.PlanFeatures pf WHERE pf.PlanId=@PlanId AND pf.FeatureId=f.FeatureId);
EXEC sys.sp_executesql N'UPDATE core.Users SET TenantId=@id WHERE TenantId IS NULL',N'@id uniqueidentifier',@TenantId;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='FK_Users_Tenants') EXEC(N'ALTER TABLE core.Users WITH CHECK ADD CONSTRAINT FK_Users_Tenants FOREIGN KEY(TenantId) REFERENCES core.Tenants(TenantId)');
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'core.Users') AND name='TenantId' AND is_nullable=1) EXEC(N'ALTER TABLE core.Users ALTER COLUMN TenantId uniqueidentifier NOT NULL');

COMMIT TRANSACTION;
