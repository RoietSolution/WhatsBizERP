CREATE PROCEDURE [finance].[RefreshOutstanding]
    @TenantId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SessionTenant uniqueidentifier = TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'TenantId'));
    IF @TenantId IS NULL OR (@SessionTenant IS NOT NULL AND @TenantId <> @SessionTenant)
        THROW 51500, N'Tenant context is missing or mismatched.', 1;

    DECLARE @now date = CAST(SYSUTCDATETIME() AS date);

    MERGE finance.CustomerOutstanding WITH (HOLDLOCK) AS target
    USING
    (
        SELECT i.CustomerId, i.InvoiceId, i.InvoiceNumber, i.InvoiceDate,
               DATEADD(day, ISNULL(pt.DueDays, 30), i.InvoiceDate) AS DueDate,
               i.GrandTotal AS InvoiceAmount, i.PaidAmount AS ReceivedAmount,
               i.BalanceAmount AS OutstandingAmount,
               DATEDIFF(day, DATEADD(day, ISNULL(pt.DueDays, 30), i.InvoiceDate), @now) AS AgeDays
        FROM sales.SalesInvoices AS i
        JOIN sales.Customers AS c ON c.CustomerId = i.CustomerId AND c.TenantId = @TenantId
        LEFT JOIN sales.CustomerPaymentTerms AS pt ON pt.PaymentTermId = c.PaymentTermId
        WHERE i.TenantId = @TenantId
          AND i.Status NOT IN (N'VOID', N'CANCELLED', N'DRAFT')
    ) AS source
        ON target.InvoiceId = source.InvoiceId
    WHEN MATCHED THEN UPDATE SET
        CustomerId = source.CustomerId,
        InvoiceNumber = source.InvoiceNumber,
        InvoiceDate = source.InvoiceDate,
        DueDate = source.DueDate,
        InvoiceAmount = source.InvoiceAmount,
        ReceivedAmount = source.ReceivedAmount,
        OutstandingAmount = source.OutstandingAmount,
        AgeDays = source.AgeDays,
        AgeBucket = CASE WHEN source.AgeDays <= 30 THEN N'0-30' WHEN source.AgeDays <= 60 THEN N'31-60' WHEN source.AgeDays <= 90 THEN N'61-90' ELSE N'ABOVE_90' END,
        LastUpdated = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN INSERT
        (CustomerId, InvoiceId, InvoiceNumber, InvoiceDate, DueDate, InvoiceAmount, ReceivedAmount, OutstandingAmount, AgeDays, AgeBucket)
        VALUES
        (source.CustomerId, source.InvoiceId, source.InvoiceNumber, source.InvoiceDate, source.DueDate, source.InvoiceAmount, source.ReceivedAmount, source.OutstandingAmount, source.AgeDays,
         CASE WHEN source.AgeDays <= 30 THEN N'0-30' WHEN source.AgeDays <= 60 THEN N'31-60' WHEN source.AgeDays <= 90 THEN N'61-90' ELSE N'ABOVE_90' END)
    WHEN NOT MATCHED BY SOURCE
         AND EXISTS (SELECT 1 FROM sales.SalesInvoices AS invoice WHERE invoice.InvoiceId = target.InvoiceId AND invoice.TenantId = @TenantId)
        THEN DELETE;

    MERGE finance.SupplierOutstanding WITH (HOLDLOCK) AS target
    USING
    (
        SELECT i.SupplierId, i.PurchaseInvoiceId, i.InvoiceNumber, i.InvoiceDate,
               COALESCE(i.DueDate, i.InvoiceDate) AS DueDate,
               i.GrandTotal AS InvoiceAmount, i.PaidAmount,
               purchase.PurchaseOutstanding(i.PurchaseInvoiceId) AS OutstandingAmount,
               DATEDIFF(day, COALESCE(i.DueDate, i.InvoiceDate), @now) AS AgeDays
        FROM purchase.PurchaseInvoices AS i
        JOIN purchase.Suppliers AS supplier ON supplier.SupplierId = i.SupplierId AND supplier.TenantId = @TenantId
        WHERE i.TenantId = @TenantId
          AND i.IsDeleted = 0
          AND i.Status NOT IN (N'CANCELLED', N'DRAFT')
    ) AS source
        ON target.PurchaseInvoiceId = source.PurchaseInvoiceId
    WHEN MATCHED THEN UPDATE SET
        SupplierId = source.SupplierId,
        InvoiceNumber = source.InvoiceNumber,
        InvoiceDate = source.InvoiceDate,
        DueDate = source.DueDate,
        InvoiceAmount = source.InvoiceAmount,
        PaidAmount = source.PaidAmount,
        OutstandingAmount = source.OutstandingAmount,
        AgeDays = source.AgeDays,
        AgeBucket = CASE WHEN source.AgeDays <= 30 THEN N'0-30' WHEN source.AgeDays <= 60 THEN N'31-60' WHEN source.AgeDays <= 90 THEN N'61-90' ELSE N'ABOVE_90' END,
        LastUpdated = SYSUTCDATETIME()
    WHEN NOT MATCHED BY TARGET THEN INSERT
        (SupplierId, PurchaseInvoiceId, InvoiceNumber, InvoiceDate, DueDate, InvoiceAmount, PaidAmount, OutstandingAmount, AgeDays, AgeBucket)
        VALUES
        (source.SupplierId, source.PurchaseInvoiceId, source.InvoiceNumber, source.InvoiceDate, source.DueDate, source.InvoiceAmount, source.PaidAmount, source.OutstandingAmount, source.AgeDays,
         CASE WHEN source.AgeDays <= 30 THEN N'0-30' WHEN source.AgeDays <= 60 THEN N'31-60' WHEN source.AgeDays <= 90 THEN N'61-90' ELSE N'ABOVE_90' END)
    WHEN NOT MATCHED BY SOURCE
         AND EXISTS (SELECT 1 FROM purchase.PurchaseInvoices AS invoice WHERE invoice.PurchaseInvoiceId = target.PurchaseInvoiceId AND invoice.TenantId = @TenantId)
        THEN DELETE;
END;
GO
