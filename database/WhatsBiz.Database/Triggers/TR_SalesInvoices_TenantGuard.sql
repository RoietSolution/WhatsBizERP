CREATE TRIGGER sales.TR_SalesInvoices_TenantGuard ON sales.SalesInvoices AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for sales writes.',1;
 UPDATE h SET TenantId=@t FROM sales.SalesInvoices h JOIN inserted i ON i.InvoiceId=h.InvoiceId WHERE h.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM sales.SalesInvoices h JOIN inserted i ON i.InvoiceId=h.InvoiceId WHERE h.TenantId<>@t) THROW 51401,'Sales invoice is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Sales warehouse is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE c.TenantId<>@t OR c.IsDeleted=1) THROW 51403,'Sales customer is outside the current tenant.',1;
END;
