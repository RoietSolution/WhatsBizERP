SET NOCOUNT ON;
IF OBJECT_ID(N'commerce.OrderIntakeRequests',N'U') IS NULL
 OR OBJECT_ID(N'commerce.OrderIntakeItems',N'U') IS NULL
 OR OBJECT_ID(N'commerce.OrderIntakeEvents',N'U') IS NULL
    THROW 79100,N'V79 validation failed: intake tables are missing.',1;
IF COL_LENGTH(N'commerce.OrderIntakeRequests',N'TenantId') IS NULL
 OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'SourceChannel') IS NULL
 OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'IntakeStatus') IS NULL
 OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'ProviderMessageId') IS NULL
 OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'CustomerConfirmedAtUtc') IS NULL
 OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'ConvertedOrderId') IS NULL
 OR COL_LENGTH(N'commerce.OrderIntakeRequests',N'Version') IS NULL
    THROW 79101,N'V79 validation failed: required request columns are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'FK_OrderIntakeRequests_Tenant')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeItems') AND name=N'FK_OrderIntakeItems_Request')
 OR NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'commerce.OrderIntakeEvents') AND name=N'FK_OrderIntakeEvents_Request')
    THROW 79102,N'V79 validation failed: tenant/request foreign keys are missing.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'UX_OrderIntakeRequests_ProviderMessage' AND is_unique=1)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'UX_OrderIntakeRequests_ConvertedOrder' AND is_unique=1)
 OR NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'commerce.OrderIntakeRequests') AND name=N'IX_OrderIntakeRequests_TenantStatusDate')
    THROW 79103,N'V79 validation failed: idempotency/order uniqueness or listing index is missing.',1;
IF NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'MULTI_CHANNEL_ORDER_INTAKE' AND IsActive=1)
    THROW 79104,N'V79 validation failed: feature catalog entry is missing.',1;
PRINT N'V79 Multi-Channel Order Intake validator passed.';
