/*
  Retailer deboarding runbook for the current shared-database tenant model.
  This is intentionally non-destructive: export and retain data before any approved purge.
  Replace the tenant key and execute only after business/legal approval.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TenantKey nvarchar(100) = N'REPLACE_TENANT_KEY';
DECLARE @Reason nvarchar(1000) = N'Retailer deboarded after approved termination';
DECLARE @Actor nvarchar(256) = N'deboarding';
DECLARE @TenantId uniqueidentifier;

IF @TenantKey = N'REPLACE_TENANT_KEY' THROW 51020, 'Replace the deboarding placeholder before execution.', 1;
SELECT @TenantId = TenantId FROM core.Tenants WHERE TenantKey = @TenantKey;
IF @TenantId IS NULL THROW 51021, 'TenantKey was not found.', 1;

BEGIN TRANSACTION;
UPDATE core.TenantFeatures SET IsEnabled = 0, IsActive = 0, EndDate = COALESCE(EndDate, SYSUTCDATETIME()), Reason = @Reason, ModifiedOn = SYSUTCDATETIME(), ModifiedBy = @Actor WHERE TenantId = @TenantId;
UPDATE core.Subscriptions SET IsActive = 0, EndDate = COALESCE(EndDate, SYSUTCDATETIME()), ModifiedOn = SYSUTCDATETIME(), ModifiedBy = @Actor WHERE TenantId = @TenantId AND IsActive = 1;
UPDATE core.Users SET IsActive = 0, IsDeleted = 1, ModifiedOn = SYSUTCDATETIME(), ModifiedBy = @Actor WHERE TenantId = @TenantId AND IsActive = 1;
UPDATE core.Tenants SET IsActive = 0, ModifiedOn = SYSUTCDATETIME(), ModifiedBy = @Actor WHERE TenantId = @TenantId;

IF OBJECT_ID(N'integration.WhatsAppConfigurations', N'U') IS NOT NULL
    UPDATE integration.WhatsAppConfigurations SET IsEnabled = 0, ConnectionStatus = N'DISABLED', LastError = @Reason, ModifiedOn = SYSUTCDATETIME() WHERE TenantId = @TenantId;
COMMIT TRANSACTION;

SELECT TenantId, TenantKey, Name, IsActive FROM core.Tenants WHERE TenantId = @TenantId;
PRINT 'Required follow-up: revoke provider secrets/webhooks, export and checksum agreed data, reconcile balances, verify backup/restore, and purge/anonymize only under approved retention policy.';
