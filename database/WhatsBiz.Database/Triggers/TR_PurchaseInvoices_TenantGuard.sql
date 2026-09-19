CREATE TRIGGER purchase.TR_PurchaseInvoices_TenantGuard ON purchase.PurchaseInvoices AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for purchase writes.',1;
 UPDATE h SET TenantId=@t FROM purchase.PurchaseInvoices h JOIN inserted i ON i.PurchaseInvoiceId=h.PurchaseInvoiceId WHERE h.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM purchase.PurchaseInvoices h JOIN inserted i ON i.PurchaseInvoiceId=h.PurchaseInvoiceId WHERE h.TenantId<>@t) THROW 51401,'Purchase invoice is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Purchase warehouse is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId WHERE s.TenantId<>@t OR s.IsDeleted=1) THROW 51403,'Purchase supplier is outside the current tenant.',1;
END;
