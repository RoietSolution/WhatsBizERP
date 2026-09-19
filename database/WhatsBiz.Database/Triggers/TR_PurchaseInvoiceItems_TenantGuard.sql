CREATE TRIGGER purchase.TR_PurchaseInvoiceItems_TenantGuard ON purchase.PurchaseInvoiceItems AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for purchase item writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId JOIN master.Products p ON p.ProductId=i.ProductId WHERE h.TenantId<>@t OR p.TenantId<>@t OR p.IsDeleted=1) THROW 51401,'Purchase item ownership is outside the current tenant.',1;
END;
