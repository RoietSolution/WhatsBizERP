/* Corrective idempotent contract update for procedures whose source formatting
   differs from the general V12 replacement patterns. */
SET NOCOUNT ON;
DECLARE @name sysname,@def nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT v.Name FROM (VALUES(N'sales.POS_PostInvoice'),(N'inventory.StockTransfer_Post')) v(Name);
OPEN c; FETCH NEXT FROM c INTO @name;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @def=OBJECT_DEFINITION(OBJECT_ID(@name));
 IF @def IS NOT NULL AND NOT EXISTS(SELECT 1 FROM sys.parameters WHERE object_id=OBJECT_ID(@name) AND name=N'@TenantId')
 BEGIN
  SET @def=REPLACE(@def,N'CREATE PROCEDURE',N'__PROC__'); SET @def=REPLACE(@def,N'ALTER PROCEDURE',N'__PROC__'); SET @def=REPLACE(@def,N'__PROC__',N'CREATE OR ALTER PROCEDURE');
  SET @def=REPLACE(@def,N'@CreatedBy NVARCHAR(256)=NULL',N'@CreatedBy NVARCHAR(256),@TenantId UNIQUEIDENTIFIER'); SET @def=REPLACE(@def,N'@CreatedBy nvarchar(256)=NULL',N'@CreatedBy nvarchar(256),@TenantId uniqueidentifier');
  SET @def=REPLACE(@def,N'AS BEGIN SET NOCOUNT ON;',N'AS BEGIN SET NOCOUNT ON;DECLARE @SessionTenant UNIQUEIDENTIFIER=TRY_CONVERT(UNIQUEIDENTIFIER,SESSION_CONTEXT(N''TenantId''));IF @TenantId IS NULL OR @SessionTenant IS NULL OR @SessionTenant<>@TenantId THROW 51410,''Tenant context is missing or mismatched.'',1;');
  IF @name=N'sales.POS_PostInvoice'
  BEGIN
   SET @def=REPLACE(@def,N'CreatedBy)VALUES(',N'CreatedBy,TenantId)VALUES(');
   SET @def=REPLACE(@def,N'@Grand,@Paid,@Status,@Remarks,@CreatedBy)',N'@Grand,@Paid,@Status,@Remarks,@CreatedBy,@TenantId)');
   SET @def=REPLACE(@def,N'@Grand,@Paid,@Status,@Remarks,@CreatedBy);IF',N'@Grand,@Paid,@Status,@Remarks,@CreatedBy,@TenantId);IF');
  END;
  EXEC sys.sp_executesql @def;
 END;
 FETCH NEXT FROM c INTO @name;
END;
CLOSE c; DEALLOCATE c;
