CREATE TRIGGER purchase.TR_Suppliers_TenantGuard ON purchase.Suppliers AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for supplier writes.',1;
 UPDATE s SET TenantId=@t FROM purchase.Suppliers s JOIN inserted i ON i.SupplierId=s.SupplierId WHERE s.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM purchase.Suppliers s JOIN inserted i ON i.SupplierId=s.SupplierId WHERE s.TenantId<>@t) THROW 51401,'Supplier is outside the current tenant.',1;
END;
