CREATE PROCEDURE [finance].[RefreshOutstanding]
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @now date = CAST(SYSUTCDATETIME() AS date);

    MERGE finance.CustomerOutstanding AS t USING
    (
        SELECT i.CustomerId,i.InvoiceId,i.InvoiceNumber,i.InvoiceDate,
               DATEADD(day,ISNULL(pt.DueDays,30),i.InvoiceDate) DueDate,
               i.GrandTotal InvoiceAmount,i.PaidAmount ReceivedAmount,i.BalanceAmount OutstandingAmount,
               DATEDIFF(day,DATEADD(day,ISNULL(pt.DueDays,30),i.InvoiceDate),@now) AgeDays
        FROM sales.SalesInvoices i
        JOIN sales.Customers c ON c.CustomerId=i.CustomerId
        LEFT JOIN sales.CustomerPaymentTerms pt ON pt.PaymentTermId=c.PaymentTermId
        WHERE i.Status NOT IN('VOID','CANCELLED','DRAFT')
    ) s ON t.InvoiceId=s.InvoiceId
    WHEN MATCHED THEN UPDATE SET CustomerId=s.CustomerId,InvoiceNumber=s.InvoiceNumber,InvoiceDate=s.InvoiceDate,DueDate=s.DueDate,InvoiceAmount=s.InvoiceAmount,ReceivedAmount=s.ReceivedAmount,OutstandingAmount=s.OutstandingAmount,AgeDays=s.AgeDays,AgeBucket=CASE WHEN s.AgeDays<=30 THEN '0-30' WHEN s.AgeDays<=60 THEN '31-60' WHEN s.AgeDays<=90 THEN '61-90' ELSE 'ABOVE_90' END,LastUpdated=SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT(CustomerId,InvoiceId,InvoiceNumber,InvoiceDate,DueDate,InvoiceAmount,ReceivedAmount,OutstandingAmount,AgeDays,AgeBucket) VALUES(s.CustomerId,s.InvoiceId,s.InvoiceNumber,s.InvoiceDate,s.DueDate,s.InvoiceAmount,s.ReceivedAmount,s.OutstandingAmount,s.AgeDays,CASE WHEN s.AgeDays<=30 THEN '0-30' WHEN s.AgeDays<=60 THEN '31-60' WHEN s.AgeDays<=90 THEN '61-90' ELSE 'ABOVE_90' END)
    -- CustomerOutstanding is a rebuildable derived cache. Remove entries whose
    -- invoices are no longer eligible (void/cancelled/draft or removed source).
    WHEN NOT MATCHED BY SOURCE THEN DELETE;

    MERGE finance.SupplierOutstanding AS t USING
    (
        SELECT i.SupplierId,i.PurchaseInvoiceId,i.InvoiceNumber,i.InvoiceDate,
               COALESCE(i.DueDate,i.InvoiceDate) DueDate,
               i.GrandTotal InvoiceAmount,i.PaidAmount,
               purchase.PurchaseOutstanding(i.PurchaseInvoiceId) OutstandingAmount,
               DATEDIFF(day,COALESCE(i.DueDate,i.InvoiceDate),@now) AgeDays
        FROM purchase.PurchaseInvoices i
        WHERE i.IsDeleted=0 AND i.Status NOT IN('CANCELLED','DRAFT')
    ) s ON t.PurchaseInvoiceId=s.PurchaseInvoiceId
    WHEN MATCHED THEN UPDATE SET SupplierId=s.SupplierId,InvoiceNumber=s.InvoiceNumber,InvoiceDate=s.InvoiceDate,DueDate=s.DueDate,InvoiceAmount=s.InvoiceAmount,PaidAmount=s.PaidAmount,OutstandingAmount=s.OutstandingAmount,AgeDays=s.AgeDays,AgeBucket=CASE WHEN s.AgeDays<=30 THEN '0-30' WHEN s.AgeDays<=60 THEN '31-60' WHEN s.AgeDays<=90 THEN '61-90' ELSE 'ABOVE_90' END,LastUpdated=SYSUTCDATETIME()
    WHEN NOT MATCHED THEN INSERT(SupplierId,PurchaseInvoiceId,InvoiceNumber,InvoiceDate,DueDate,InvoiceAmount,PaidAmount,OutstandingAmount,AgeDays,AgeBucket) VALUES(s.SupplierId,s.PurchaseInvoiceId,s.InvoiceNumber,s.InvoiceDate,s.DueDate,s.InvoiceAmount,s.PaidAmount,s.OutstandingAmount,s.AgeDays,CASE WHEN s.AgeDays<=30 THEN '0-30' WHEN s.AgeDays<=60 THEN '31-60' WHEN s.AgeDays<=90 THEN '61-90' ELSE 'ABOVE_90' END)
    -- SupplierOutstanding is a rebuildable derived cache. Remove entries whose
    -- invoices are no longer eligible (deleted/cancelled/draft or removed source).
    WHEN NOT MATCHED BY SOURCE THEN DELETE;
END;
