SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'commerce.Cities',N'U') IS NULL
    THROW 77000,N'V77 requires the V74 city marketplace schema.',1;

BEGIN TRANSACTION;

IF COL_LENGTH(N'commerce.Cities',N'BannerHeadline') IS NULL
    ALTER TABLE commerce.Cities ADD BannerHeadline nvarchar(120) NULL;

IF COL_LENGTH(N'commerce.Cities',N'BannerSubtitle') IS NULL
    ALTER TABLE commerce.Cities ADD BannerSubtitle nvarchar(240) NULL;

COMMIT TRANSACTION;
