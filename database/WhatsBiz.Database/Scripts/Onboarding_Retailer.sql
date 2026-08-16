/*
  Retailer onboarding runbook for the current shared-database tenant model.
  Run after the SQL project/PostDeployment baseline. Review all SELECT output before go-live.
  This script creates tenant/plan/feature/company records; create the administrator and password
  through the application identity workflow, never by inserting a plain-text password here.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TenantKey nvarchar(100) = N'REPLACE_TENANT_KEY';
DECLARE @TenantName nvarchar(200) = N'Replace Retailer Name';
DECLARE @PlanKey nvarchar(100) = N'V1_DEFAULT';
DECLARE @CompanyCode nvarchar(30) = N'RETAILER001';
DECLARE @CompanyName nvarchar(200) = N'Replace Retailer Name';
DECLARE @Country nvarchar(100) = N'India';
DECLARE @CreatedBy nvarchar(256) = N'onboarding';
DECLARE @EnableAllActiveFeatures bit = 0; -- Set to 1 only after commercial approval.
DECLARE @TenantId uniqueidentifier = NEWID();
DECLARE @PlanId uniqueidentifier;
DECLARE @CompanyId uniqueidentifier = NEWID();

IF @TenantKey = N'REPLACE_TENANT_KEY' OR @CompanyCode = N'RETAILER001'
    THROW 51010, 'Replace the onboarding placeholders before execution.', 1;
IF EXISTS (SELECT 1 FROM core.Tenants WHERE TenantKey = @TenantKey)
    THROW 51011, 'TenantKey already exists. Stop and use the existing tenant.', 1;
SELECT @PlanId = PlanId FROM core.Plans WHERE PlanKey = @PlanKey AND IsActive = 1;
IF @PlanId IS NULL THROW 51012, 'The requested active plan does not exist.', 1;
IF EXISTS (SELECT 1 FROM admin.Companies WHERE CompanyCode = @CompanyCode)
    THROW 51013, 'CompanyCode already exists. Stop and use the existing company.', 1;

BEGIN TRANSACTION;
INSERT core.Tenants(TenantId, TenantKey, Name, IsActive, CreatedBy)
VALUES (@TenantId, @TenantKey, @TenantName, 1, @CreatedBy);

INSERT core.Subscriptions(SubscriptionId, TenantId, PlanId, StartDate, IsActive, CreatedBy)
VALUES (NEWID(), @TenantId, @PlanId, SYSUTCDATETIME(), 1, @CreatedBy);

INSERT core.TenantFeatures(TenantFeatureId, TenantId, FeatureId, IsEnabled, Reason, IsActive, CreatedBy)
SELECT NEWID(), @TenantId, f.FeatureId,
       CASE WHEN @EnableAllActiveFeatures = 1 AND pf.IsEnabled = 1 THEN 1 ELSE 0 END,
       N'Initial retailer onboarding', 1, @CreatedBy
FROM core.Features f
LEFT JOIN core.PlanFeatures pf ON pf.FeatureId = f.FeatureId AND pf.PlanId = @PlanId
WHERE f.IsActive = 1 AND NOT EXISTS (SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId = @TenantId AND tf.FeatureId = f.FeatureId);

INSERT admin.Companies(CompanyId, CompanyCode, CompanyName, Country, IsActive, CreatedOn)
VALUES (@CompanyId, @CompanyCode, @CompanyName, @Country, 1, SYSUTCDATETIME());
COMMIT TRANSACTION;

SELECT @TenantId TenantId, @TenantKey TenantKey, @CompanyId CompanyId, @CompanyCode CompanyCode, @PlanKey PlanKey;
SELECT FeatureKey, IsEnabled FROM core.TenantFeatures tf JOIN core.Features f ON f.FeatureId = tf.FeatureId WHERE tf.TenantId = @TenantId ORDER BY FeatureKey;
PRINT 'Next steps: create the administrator through the application, configure company/tax/year/branch/warehouse/printer/payment settings, import master data, and complete the onboarding checklist.';
