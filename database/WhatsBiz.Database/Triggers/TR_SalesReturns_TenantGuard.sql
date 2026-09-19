CREATE TRIGGER sales.TR_SalesReturns_TenantGuard ON sales.SalesInvoiceReturns AFTER INSERT, UPDATE AS
BEGIN
 SET NOCOUNT ON; DECLARE @t uniqueidentifier=TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));
 IF @t IS NULL THROW 51400,'Tenant context is required for return writes.',1;
 IF EXISTS(SELECT 1 FROM inserted i LEFT JOIN sales.SalesInvoices h ON h.InvoiceId=i.InvoiceId WHERE h.TenantId<>@t OR h.InvoiceId IS NULL) THROW 51401,'Sales return invoice is outside the current tenant.',1;
END;
