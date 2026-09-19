CREATE TRIGGER inventory.TR_InventoryBalances_TenantGuard ON inventory.InventoryBalances AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for inventory writes.',1;
 UPDATE b SET TenantId=@t FROM inventory.InventoryBalances b JOIN inserted i ON i.InventoryBalanceId=b.InventoryBalanceId WHERE b.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.InventoryBalances b JOIN inserted i ON i.InventoryBalanceId=b.InventoryBalanceId WHERE b.TenantId<>@t) THROW 51401,'Inventory balance is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN master.Products p ON p.ProductId=i.ProductId WHERE p.TenantId<>@t OR p.IsDeleted=1) THROW 51402,'Inventory product is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51403,'Inventory warehouse is outside the current tenant.',1;
END;
