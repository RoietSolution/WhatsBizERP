CREATE TRIGGER purchase.TR_PurchasePayments_TenantGuard ON purchase.PurchasePayments AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for payment writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN purchase.PurchaseInvoices h ON h.PurchaseInvoiceId=i.PurchaseInvoiceId WHERE h.TenantId<>@t OR h.PurchaseInvoiceId IS NULL) THROW 51401,'Purchase payment invoice is outside the current tenant.',1;
END;
