SET NOCOUNT ON;

IF OBJECT_ID(N'commerce.Cities',N'U') IS NULL
    THROW 77100,N'V77 city table is missing.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'commerce.Cities')
      AND c.name=N'BannerHeadline' AND t.name=N'nvarchar'
      AND c.max_length=240 AND c.is_nullable=1
)
    THROW 77101,N'V77 banner headline column is missing or incompatible.',1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    WHERE c.object_id=OBJECT_ID(N'commerce.Cities')
      AND c.name=N'BannerSubtitle' AND t.name=N'nvarchar'
      AND c.max_length=480 AND c.is_nullable=1
)
    THROW 77102,N'V77 banner subtitle column is missing or incompatible.',1;

PRINT N'V77 city marketplace banner copy validator passed.';
