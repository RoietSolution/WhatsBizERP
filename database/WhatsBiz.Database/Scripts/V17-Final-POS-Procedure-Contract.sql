SET NOCOUNT ON;
DECLARE @d nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'sales.POS_PostInvoice')),@p int;
IF @d IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID(N'sales.POS_PostInvoice') AND name=N'@TenantId')
BEGIN
 SET @d=REPLACE(@d,N'CREATE PROCEDURE',N'__PROC__'); SET @d=REPLACE(@d,N'ALTER PROCEDURE',N'__PROC__'); SET @d=REPLACE(@d,N'__PROC__',N'CREATE OR ALTER PROCEDURE');
 SET @p=CHARINDEX(CHAR(10)+N'AS',@d); IF @p=0 THROW 51420,'Unable to locate POS procedure body.',1;
 SET @d=STUFF(@d,@p,0,N',@TenantId UNIQUEIDENTIFIER');
 SET @d=REPLACE(@d,N'AS'+CHAR(10)+N'BEGIN SET NOCOUNT ON;',N'AS'+CHAR(10)+N'BEGIN SET NOCOUNT ON;DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N''TenantId''));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,''Tenant context is missing or mismatched.'',1;');
 DECLARE @i int=CHARINDEX(N'INSERT sales.SalesInvoices',@d),@v int,@e int;
 SET @e=CHARINDEX(N')',@i); SET @d=STUFF(@d,@e,0,N',TenantId'); SET @v=CHARINDEX(N'VALUES(',@i); SET @e=CHARINDEX(N')',@v); SET @d=STUFF(@d,@e,0,N',@TenantId');
 EXEC sys.sp_executesql @d;
END;
