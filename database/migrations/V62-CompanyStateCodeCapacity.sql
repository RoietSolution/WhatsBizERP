/* V62: align the company state-code column with the two-digit GST state-code contract. */
IF COL_LENGTH(N'admin.Companies', N'StateCode') IS NULL
    THROW 62001, N'admin.Companies.StateCode is missing; V62 cannot repair the company schema.', 1;

DECLARE @type sysname = (SELECT t.name FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(N'admin.Companies') AND c.name = N'StateCode');
DECLARE @maxLength int = COL_LENGTH(N'admin.Companies', N'StateCode');

IF @maxLength < 2 AND @type IN (N'char', N'varchar')
    EXEC sys.sp_executesql N'ALTER TABLE admin.Companies ALTER COLUMN StateCode CHAR(2) NULL;';
ELSE IF @maxLength < 4 AND @type IN (N'nchar', N'nvarchar')
    EXEC sys.sp_executesql N'ALTER TABLE admin.Companies ALTER COLUMN StateCode NCHAR(2) NULL;';
