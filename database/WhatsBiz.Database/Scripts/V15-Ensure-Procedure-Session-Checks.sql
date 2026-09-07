SET NOCOUNT ON;
DECLARE @name sysname,@d nvarchar(max),@needle nvarchar(200),@guard nvarchar(700);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT v.Name FROM (VALUES(N'sales.POS_PostInvoice'),(N'purchase.Purchase_Post'),(N'inventory.StockAdjustment_Post'),(N'inventory.StockTransfer_Post')) v(Name);
OPEN c; FETCH NEXT FROM c INTO @name;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @d=OBJECT_DEFINITION(OBJECT_ID(@name));
 IF @d IS NOT NULL AND @d NOT LIKE '%SessionTenant%'
 BEGIN
  SET @d=REPLACE(@d,N'CREATE PROCEDURE',N'__PROC__'); SET @d=REPLACE(@d,N'ALTER   PROCEDURE',N'__PROC__'); SET @d=REPLACE(@d,N'ALTER PROCEDURE',N'__PROC__'); SET @d=REPLACE(@d,N'__PROC__',N'CREATE OR ALTER PROCEDURE');
  SET @guard=N'DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N''TenantId''));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,''Tenant context is missing or mismatched.'',1;';
  SET @d=REPLACE(@d,N'AS'+CHAR(13)+CHAR(10)+N'BEGIN SET NOCOUNT ON;',N'AS'+CHAR(13)+CHAR(10)+N'BEGIN SET NOCOUNT ON;'+@guard);
  SET @d=REPLACE(@d,N'AS'+CHAR(10)+N'BEGIN SET NOCOUNT ON;',N'AS'+CHAR(10)+N'BEGIN SET NOCOUNT ON;'+@guard);
  EXEC sys.sp_executesql @d;
 END;
 FETCH NEXT FROM c INTO @name;
END;
CLOSE c; DEALLOCATE c;
