IF COL_LENGTH(N'admin.Companies', N'StateCode') IS NULL
    THROW 62002, N'V62 StateCode column is missing.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'admin.Companies')
      AND c.name = N'StateCode'
      AND t.name IN (N'char', N'varchar', N'nchar', N'nvarchar')
      AND ((t.name IN (N'char', N'varchar') AND c.max_length >= 2) OR (t.name IN (N'nchar', N'nvarchar') AND c.max_length >= 4))
)
    THROW 62003, N'V62 StateCode capacity is less than two characters.', 1;

IF EXISTS (SELECT 1 FROM admin.Companies WHERE StateCode IS NOT NULL AND LTRIM(RTRIM(StateCode)) <> N'' AND LTRIM(RTRIM(StateCode)) NOT LIKE '[0-9][0-9]')
    THROW 62004, N'Existing company StateCode data is not a two-digit code.', 1;
