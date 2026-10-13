/* V50 - Optional customer-facing tenant display name. */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'core.Tenants', N'U') IS NULL
    THROW 52500, N'V50 requires the core.Tenants table.', 1;

IF COL_LENGTH(N'core.Tenants', N'DisplayName') IS NULL
    ALTER TABLE core.Tenants ADD DisplayName nvarchar(200) NULL;

/* Existing tenants intentionally remain NULL and fall back to Name in the API. */
