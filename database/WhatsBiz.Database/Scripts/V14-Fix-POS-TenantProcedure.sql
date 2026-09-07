SET NOCOUNT ON;
DECLARE @d nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'sales.POS_PostInvoice'));
IF @d IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID(N'sales.POS_PostInvoice') AND name=N'@TenantId')
BEGIN
 SET @d=REPLACE(@d,N'CREATE PROCEDURE',N'__PROC__'); SET @d=REPLACE(@d,N'ALTER PROCEDURE',N'__PROC__'); SET @d=REPLACE(@d,N'__PROC__',N'CREATE OR ALTER PROCEDURE');
 SET @d=REPLACE(@d,N'@CreatedBy NVARCHAR(256)=NULL',N'@CreatedBy NVARCHAR(256),@TenantId UNIQUEIDENTIFIER');
 SET @d=REPLACE(@d,N'AS BEGIN SET NOCOUNT ON;',N'AS BEGIN SET NOCOUNT ON;DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N''TenantId''));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,''Tenant context is missing or mismatched.'',1;');
 DECLARE @p int=CHARINDEX(N'INSERT sales.SalesInvoices',@d),@v int,@c int;
 SET @c=CHARINDEX(N')',@p); SET @d=STUFF(@d,@c,0,N',TenantId');
 SET @v=CHARINDEX(N'VALUES(',@p); SET @c=CHARINDEX(N')',@v); SET @d=STUFF(@d,@c,0,N',@TenantId');
 EXEC sys.sp_executesql @d;
END;
