CREATE TRIGGER inventory.TR_InventoryTransactions_TenantGuard ON inventory.InventoryTransactions AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for inventory writes.',1;
 UPDATE x SET TenantId=@t FROM inventory.InventoryTransactions x JOIN inserted i ON i.TransactionId=x.TransactionId WHERE x.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.InventoryTransactions x JOIN inserted i ON i.TransactionId=x.TransactionId WHERE x.TenantId<>@t) THROW 51401,'Inventory transaction is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Transaction warehouse is outside the current tenant.',1;
END;
