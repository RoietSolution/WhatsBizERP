CREATE TRIGGER sales.TR_SalesInvoiceItems_TenantGuard ON sales.SalesInvoiceItems AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for sales item writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId JOIN master.Products p ON p.ProductId=i.ProductId WHERE h.TenantId<>@t OR p.TenantId<>@t OR p.IsDeleted=1) THROW 51401,'Sales item ownership is outside the current tenant.',1;
END;
