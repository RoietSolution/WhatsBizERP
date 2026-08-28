/*
  WhatsBiz retailer onboarding (shared SQL Server database)

  Prerequisite: deploy the current database project, including V12.
  Process:
    1. Edit only INPUTS below.
    2. Run with @ApplyChanges = 0 and review both result sets.
    3. Set @ApplyChanges = 1 and run once in the authorized target environment.
    4. Create the retailer Administrator through ASP.NET Core Identity/UserManager. Never insert
       a password or PasswordHash with this script.
    5. Sign in as SystemAdministrator and verify/configure the tenant at /admin/features.

  Current plans: V1_DEFAULT = V1 only; V2_COMMERCE = V1 + V2.
  This script is create-only. Later feature changes belong in /admin/features so child settings are
  preserved and the application feature cache is invalidated normally.

  For the deterministic, safely rerunnable QA bootstrap (including minimum V1 masters), use
  Bootstrap_QA.sql instead. It is guarded to run only against WhatsBizERP_QA.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

/* ================================ INPUTS ================================= */
DECLARE @ApplyChanges bit = 0; -- 0 = validate/preview; 1 = create retailer
DECLARE @TenantKey nvarchar(100) = N'REPLACE_TENANT_KEY'; -- stable uppercase key
DECLARE @TenantName nvarchar(200) = N'Replace Retailer Name';
DECLARE @PlanKey nvarchar(100) = N'V1_DEFAULT'; -- V1_DEFAULT or V2_COMMERCE
DECLARE @SubscriptionStartDate datetimeoffset = SYSUTCDATETIME();
DECLARE @SubscriptionEndDate datetimeoffset = NULL; -- NULL = no scheduled end
DECLARE @CreatedBy nvarchar(256) = N'system-administrator:onboarding';

-- Optional configured-state overrides. ON is permitted only when included by the plan.
DECLARE @FeatureOverrides TABLE
(
    FeatureKey nvarchar(100) NOT NULL PRIMARY KEY,
    ConfiguredEnabled bit NOT NULL
);
-- Examples (uncomment only when required):
-- INSERT @FeatureOverrides VALUES (N'FINANCE', 0);
-- INSERT @FeatureOverrides VALUES (N'COMMERCE_ANALYTICS', 0);
-- INSERT @FeatureOverrides VALUES (N'V2', 0);
/* ======================================================================== */

SET @TenantKey = UPPER(LTRIM(RTRIM(@TenantKey)));
SET @TenantName = LTRIM(RTRIM(@TenantName));
SET @PlanKey = UPPER(LTRIM(RTRIM(@PlanKey)));
SET @CreatedBy = NULLIF(LTRIM(RTRIM(@CreatedBy)), N'');

IF @ApplyChanges NOT IN (0, 1)
    THROW 51010, '@ApplyChanges must be 0 (preview) or 1 (apply).', 1;
IF @TenantKey IN (N'', N'REPLACE_TENANT_KEY') OR @TenantName IN (N'', N'Replace Retailer Name')
    THROW 51011, 'Replace TenantKey and TenantName before execution.', 1;
IF @TenantKey LIKE N'%[^A-Z0-9_-]%'
    THROW 51012, 'TenantKey may contain only A-Z, 0-9, underscore, and hyphen.', 1;
IF @SubscriptionEndDate IS NOT NULL AND @SubscriptionEndDate < @SubscriptionStartDate
    THROW 51013, 'SubscriptionEndDate cannot be earlier than SubscriptionStartDate.', 1;
IF OBJECT_ID(N'core.Tenants', N'U') IS NULL
   OR OBJECT_ID(N'core.Plans', N'U') IS NULL
   OR OBJECT_ID(N'core.Features', N'U') IS NULL
   OR OBJECT_ID(N'core.PlanFeatures', N'U') IS NULL
   OR OBJECT_ID(N'core.Subscriptions', N'U') IS NULL
   OR OBJECT_ID(N'core.TenantFeatures', N'U') IS NULL
    THROW 51014, 'Feature/subscription schema is missing. Deploy the current database project first.', 1;
IF COL_LENGTH(N'core.Features', N'FeatureType') IS NULL
   OR COL_LENGTH(N'core.Features', N'ParentFeatureId') IS NULL
    THROW 51015, 'Hierarchical feature schema is missing. Deploy V12 before onboarding.', 1;
IF NOT EXISTS (SELECT 1 FROM core.Features WHERE FeatureKey = N'V1' AND FeatureType = N'VERSION' AND IsActive = 1)
   OR NOT EXISTS (SELECT 1 FROM core.Features WHERE FeatureKey = N'V2' AND FeatureType = N'VERSION' AND IsActive = 1)
    THROW 51016, 'Active V1/V2 feature definitions are missing.', 1;
IF EXISTS (SELECT 1 FROM core.Tenants WHERE TenantKey = @TenantKey)
    THROW 51017, 'TenantKey already exists. No changes were made; manage the existing tenant instead.', 1;

DECLARE @PlanId uniqueidentifier;
SELECT @PlanId = PlanId FROM core.Plans WHERE PlanKey = @PlanKey AND IsActive = 1;
IF @PlanId IS NULL
    THROW 51018, 'The selected PlanKey does not exist or is inactive.', 1;

IF EXISTS
(
    SELECT 1 FROM core.Features f
    LEFT JOIN core.PlanFeatures pf ON pf.PlanId = @PlanId AND pf.FeatureId = f.FeatureId
    WHERE f.IsActive = 1 AND pf.PlanFeatureId IS NULL
)
    THROW 51019, 'The selected plan has no entitlement row for one or more active features.', 1;

IF EXISTS
(
    SELECT 1 FROM @FeatureOverrides o
    LEFT JOIN core.Features f ON f.FeatureKey = o.FeatureKey AND f.IsActive = 1
    WHERE f.FeatureId IS NULL
)
    THROW 51020, 'A feature override contains an unknown or inactive FeatureKey.', 1;

IF EXISTS
(
    SELECT 1 FROM @FeatureOverrides o
    JOIN core.Features f ON f.FeatureKey = o.FeatureKey
    LEFT JOIN core.PlanFeatures pf ON pf.PlanId = @PlanId AND pf.FeatureId = f.FeatureId
    WHERE o.ConfiguredEnabled = 1 AND ISNULL(pf.IsEnabled, 0) = 0
)
    THROW 51021, 'A feature override attempts to enable a feature excluded by the plan.', 1;

DECLARE @ConfiguredV1 bit =
(
    SELECT COALESCE(o.ConfiguredEnabled, pf.IsEnabled, 0)
    FROM core.Features f
    LEFT JOIN core.PlanFeatures pf ON pf.PlanId = @PlanId AND pf.FeatureId = f.FeatureId
    LEFT JOIN @FeatureOverrides o ON o.FeatureKey = f.FeatureKey
    WHERE f.FeatureKey = N'V1'
);
DECLARE @ConfiguredV2 bit =
(
    SELECT COALESCE(o.ConfiguredEnabled, pf.IsEnabled, 0)
    FROM core.Features f
    LEFT JOIN core.PlanFeatures pf ON pf.PlanId = @PlanId AND pf.FeatureId = f.FeatureId
    LEFT JOIN @FeatureOverrides o ON o.FeatureKey = f.FeatureKey
    WHERE f.FeatureKey = N'V2'
);
IF @ConfiguredV1 = 0 AND @ConfiguredV2 = 1
    THROW 51022, 'Invalid configuration: V2 depends on V1. Enable V1 or disable V2.', 1;

-- Preview is returned in both preview and apply modes.
SELECT @ApplyChanges ApplyChanges, @TenantKey TenantKey, @TenantName TenantName,
       @PlanKey PlanKey, @SubscriptionStartDate SubscriptionStartDate,
       @SubscriptionEndDate SubscriptionEndDate, @ConfiguredV1 ConfiguredV1,
       @ConfiguredV2 ConfiguredV2;

SELECT f.FeatureKey, f.Name FeatureName, f.FeatureType, parent.FeatureKey ParentFeatureKey,
       f.Version, CAST(pf.IsEnabled AS bit) SubscriptionAllowed,
       CAST(COALESCE(o.ConfiguredEnabled, pf.IsEnabled, 0) AS bit) ConfiguredEnabled,
       CASE
           WHEN pf.IsEnabled = 0 THEN N'SUBSCRIPTION_NOT_ENTITLED'
           WHEN COALESCE(o.ConfiguredEnabled, pf.IsEnabled, 0) = 0 THEN N'TENANT_CONFIGURATION_DISABLED'
           WHEN parent.FeatureId IS NOT NULL
                AND COALESCE(parentOverride.ConfiguredEnabled, parentPlan.IsEnabled, 0) = 0
               THEN N'PARENT_VERSION_DISABLED'
           ELSE N'CONFIGURED_AVAILABLE'
       END ConfigurationPreview
FROM core.Features f
JOIN core.PlanFeatures pf ON pf.PlanId = @PlanId AND pf.FeatureId = f.FeatureId
LEFT JOIN core.Features parent ON parent.FeatureId = f.ParentFeatureId
LEFT JOIN core.PlanFeatures parentPlan ON parentPlan.PlanId = @PlanId AND parentPlan.FeatureId = parent.FeatureId
LEFT JOIN @FeatureOverrides o ON o.FeatureKey = f.FeatureKey
LEFT JOIN @FeatureOverrides parentOverride ON parentOverride.FeatureKey = parent.FeatureKey
WHERE f.IsActive = 1
ORDER BY f.Version, f.SortOrder, f.Name;

IF @ApplyChanges = 0
BEGIN
    PRINT 'PREVIEW ONLY: no database changes were made. Set @ApplyChanges = 1 after review.';
    RETURN;
END;

DECLARE @TenantId uniqueidentifier = NEWID();
DECLARE @SubscriptionId uniqueidentifier = NEWID();
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    DECLARE @LockResource nvarchar(255) = N'WhatsBiz:RetailerOnboarding:' + @TenantKey;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = @LockResource,
        @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
    IF @LockResult < 0
        THROW 51023, 'Could not acquire the retailer-onboarding lock.', 1;
    IF EXISTS (SELECT 1 FROM core.Tenants WITH (UPDLOCK, HOLDLOCK) WHERE TenantKey = @TenantKey)
        THROW 51024, 'TenantKey was created concurrently. No changes were made.', 1;

    INSERT core.Tenants(TenantId, TenantKey, Name, IsActive, CreatedBy)
    VALUES (@TenantId, @TenantKey, @TenantName, 1, @CreatedBy);
    INSERT core.Subscriptions
        (SubscriptionId, TenantId, PlanId, StartDate, EndDate, IsActive, CreatedBy)
    VALUES
        (@SubscriptionId, @TenantId, @PlanId, @SubscriptionStartDate, @SubscriptionEndDate, 1, @CreatedBy);
    INSERT core.TenantFeatures
        (TenantFeatureId, TenantId, FeatureId, IsEnabled, StartDate, EndDate, Reason, IsActive, CreatedBy)
    SELECT NEWID(), @TenantId, f.FeatureId,
           CAST(COALESCE(o.ConfiguredEnabled, pf.IsEnabled, 0) AS bit),
           NULL, NULL, N'Initialized from onboarding plan ' + @PlanKey, 1, @CreatedBy
    FROM core.Features f
    JOIN core.PlanFeatures pf ON pf.PlanId = @PlanId AND pf.FeatureId = f.FeatureId
    LEFT JOIN @FeatureOverrides o ON o.FeatureKey = f.FeatureKey
    WHERE f.IsActive = 1;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT t.TenantId, t.TenantKey, t.Name TenantName, t.IsActive TenantActive,
       s.SubscriptionId, p.PlanKey, p.Name PlanName, s.StartDate, s.EndDate,
       s.IsActive SubscriptionActive
FROM core.Tenants t
JOIN core.Subscriptions s ON s.TenantId = t.TenantId
JOIN core.Plans p ON p.PlanId = s.PlanId
WHERE t.TenantId = @TenantId AND s.SubscriptionId = @SubscriptionId;

SELECT f.FeatureKey, f.FeatureType, parent.FeatureKey ParentFeatureKey, f.Version,
       pf.IsEnabled SubscriptionAllowed, tf.IsEnabled ConfiguredEnabled, tf.Reason
FROM core.TenantFeatures tf
JOIN core.Features f ON f.FeatureId = tf.FeatureId
JOIN core.PlanFeatures pf ON pf.PlanId = @PlanId AND pf.FeatureId = tf.FeatureId
LEFT JOIN core.Features parent ON parent.FeatureId = f.ParentFeatureId
WHERE tf.TenantId = @TenantId
ORDER BY f.Version, f.SortOrder, f.Name;

PRINT 'CREATED: retailer tenant, active subscription, and configured feature rows.';
PRINT 'NEXT: create the retailer Administrator with UserManager and the TenantId returned above.';
PRINT 'NEXT: verify configured/effective access at /admin/features before sharing credentials.';
PRINT 'NOTE: Companies, branches, warehouses, and several legacy settings are not tenant-scoped; this script does not create them.';
