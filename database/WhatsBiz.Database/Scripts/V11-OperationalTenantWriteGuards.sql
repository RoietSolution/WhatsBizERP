/* Runtime safety net for legacy procedures during Phase 2 rollout.
   The API sets SESSION_CONTEXT from authenticated tenant context before invoking
   idempotent operational procedures. Missing/mismatched context is rejected. */
SET NOCOUNT ON;
GO
CREATE OR ALTER TRIGGER purchase.TR_Suppliers_TenantGuard ON purchase.Suppliers AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for supplier writes.',1;
 UPDATE s SET TenantId=@t FROM purchase.Suppliers s JOIN inserted i ON i.SupplierId=s.SupplierId WHERE s.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM purchase.Suppliers s JOIN inserted i ON i.SupplierId=s.SupplierId WHERE s.TenantId<>@t) THROW 51401,'Supplier is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER inventory.TR_Warehouses_TenantGuard ON inventory.Warehouses AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for warehouse writes.',1;
 UPDATE w SET TenantId=@t FROM inventory.Warehouses w JOIN inserted i ON i.WarehouseId=w.WarehouseId WHERE w.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.Warehouses w JOIN inserted i ON i.WarehouseId=w.WarehouseId WHERE w.TenantId<>@t) THROW 51401,'Warehouse is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesInvoices_TenantGuard ON sales.SalesInvoices AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for sales writes.',1;
 UPDATE h SET TenantId=@t FROM sales.SalesInvoices h JOIN inserted i ON i.InvoiceId=h.InvoiceId WHERE h.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM sales.SalesInvoices h JOIN inserted i ON i.InvoiceId=h.InvoiceId WHERE h.TenantId<>@t) THROW 51401,'Sales invoice is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Sales warehouse is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN sales.Customers c ON c.CustomerId=i.CustomerId WHERE c.TenantId<>@t OR c.IsDeleted=1) THROW 51403,'Sales customer is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchaseInvoices_TenantGuard ON purchase.PurchaseInvoices AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for purchase writes.',1;
 UPDATE h SET TenantId=@t FROM purchase.PurchaseInvoices h JOIN inserted i ON i.PurchaseInvoiceId=h.PurchaseInvoiceId WHERE h.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM purchase.PurchaseInvoices h JOIN inserted i ON i.PurchaseInvoiceId=h.PurchaseInvoiceId WHERE h.TenantId<>@t) THROW 51401,'Purchase invoice is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Purchase warehouse is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN purchase.Suppliers s ON s.SupplierId=i.SupplierId WHERE s.TenantId<>@t OR s.IsDeleted=1) THROW 51403,'Purchase supplier is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER inventory.TR_InventoryBalances_TenantGuard ON inventory.InventoryBalances AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for inventory writes.',1;
 UPDATE b SET TenantId=@t FROM inventory.InventoryBalances b JOIN inserted i ON i.InventoryBalanceId=b.InventoryBalanceId WHERE b.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.InventoryBalances b JOIN inserted i ON i.InventoryBalanceId=b.InventoryBalanceId WHERE b.TenantId<>@t) THROW 51401,'Inventory balance is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN master.Products p ON p.ProductId=i.ProductId WHERE p.TenantId<>@t OR p.IsDeleted=1) THROW 51402,'Inventory product is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51403,'Inventory warehouse is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER inventory.TR_InventoryTransactions_TenantGuard ON inventory.InventoryTransactions AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for inventory writes.',1;
 UPDATE x SET TenantId=@t FROM inventory.InventoryTransactions x JOIN inserted i ON i.TransactionId=x.TransactionId WHERE x.TenantId IS NULL;
 IF EXISTS(SELECT 1 FROM inventory.InventoryTransactions x JOIN inserted i ON i.TransactionId=x.TransactionId WHERE x.TenantId<>@t) THROW 51401,'Inventory transaction is outside the current tenant.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN inventory.Warehouses w ON w.WarehouseId=i.WarehouseId WHERE w.TenantId<>@t OR w.IsDeleted=1) THROW 51402,'Transaction warehouse is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesPayments_TenantGuard ON sales.SalesPayments AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for payment writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId WHERE h.TenantId<>@t OR h.InvoiceId IS NULL) THROW 51401,'Sales payment invoice is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesInvoiceItems_TenantGuard ON sales.SalesInvoiceItems AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for sales item writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=i.ProductId WHERE h.TenantId<>@t OR p.TenantId<>@t OR p.IsDeleted=1) THROW 51401,'Sales item ownership is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchaseInvoiceItems_TenantGuard ON purchase.PurchaseInvoiceItems AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for purchase item writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId JOIN master.Products p ON p.ProductId=i.ProductId WHERE h.TenantId<>@t OR p.TenantId<>@t OR p.IsDeleted=1) THROW 51401,'Purchase item ownership is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchasePayments_TenantGuard ON purchase.PurchasePayments AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for payment writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId WHERE h.TenantId<>@t OR h.PurchaseInvoiceId IS NULL) THROW 51401,'Purchase payment invoice is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER purchase.TR_PurchaseReturns_TenantGuard ON purchase.PurchaseReturns AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for return writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId WHERE h.TenantId<>@t OR h.PurchaseInvoiceId IS NULL) THROW 51401,'Purchase return invoice is outside the current tenant.',1;
END;
GO
CREATE OR ALTER TRIGGER sales.TR_SalesReturns_TenantGuard ON sales.SalesInvoiceReturns AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for return writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId WHERE h.TenantId<>@t OR h.InvoiceId IS NULL) THROW 51401,'Sales return invoice is outside the current tenant.',1;
END;
GO
