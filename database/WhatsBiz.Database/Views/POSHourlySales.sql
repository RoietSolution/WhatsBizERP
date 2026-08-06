CREATE VIEW [sales].[POSHourlySales]
AS
SELECT CONVERT(date, [InvoiceDate]) [SalesDate], DATEPART(hour, [InvoiceDate]) [SalesHour],
       COUNT_BIG(*) [InvoiceCount], SUM([GrandTotal]) [GrossSales], SUM([PaidAmount]) [Collections]
FROM [sales].[SalesInvoices]
WHERE [Status] IN ('COMPLETED','PARTIALLY_RETURNED','RETURNED')
GROUP BY CONVERT(date, [InvoiceDate]), DATEPART(hour, [InvoiceDate]);
