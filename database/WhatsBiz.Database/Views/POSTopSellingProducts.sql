CREATE VIEW [sales].[POSTopSellingProducts]
AS
SELECT CONVERT(date, i.[InvoiceDate]) [SalesDate], d.[ProductId], p.[ProductCode], p.[ProductName],
       SUM(d.[Quantity]-d.[ReturnedQuantity]) [QuantitySold], SUM(d.[LineTotal]) [GrossSales]
FROM [sales].[SalesInvoiceItems] d
JOIN [sales].[SalesInvoices] i ON i.[InvoiceId]=d.[InvoiceId]
JOIN [master].[Products] p ON p.[ProductId]=d.[ProductId]
WHERE i.[Status] IN ('COMPLETED','PARTIALLY_RETURNED','RETURNED')
GROUP BY CONVERT(date, i.[InvoiceDate]), d.[ProductId], p.[ProductCode], p.[ProductName];
