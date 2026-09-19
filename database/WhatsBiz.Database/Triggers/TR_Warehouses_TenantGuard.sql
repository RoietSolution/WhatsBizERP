CREATE TRIGGER inventory.TR_Warehouses_TenantGuard ON inventory.Warehouses AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for warehouse writes.',1;
 UPDATE w SET TenantId=@t FROM inventory.Warehouses w JOIN inserted i ON i.WarehouseId=w.WarehouseId WHERE w.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.Warehouses w JOIN inserted i ON i.WarehouseId=w.WarehouseId WHERE w.TenantId<>@t) THROW 51401,'Warehouse is outside the current tenant.',1;
END;
