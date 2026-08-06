#pragma warning disable CA1305
using System.Net;
using System.Text;
using ClosedXML.Excel;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.Domain.POS;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSDocumentService(IPrintingService printing) : IPOSDocumentService
{
    public string InvoiceHtml(SalesInvoice invoice, string paper)
    {
        var body = new StringBuilder($"<p>{invoice.InvoiceDate:dd-MMM-yyyy HH:mm}</p><table><tr><th>Item</th><th>Qty</th><th>Rate</th><th>GST</th><th>Total</th></tr>");
        foreach (var item in invoice.Items)
            body.Append($"<tr><td>{Encode(item.Product.ProductName)}</td><td>{item.Quantity}</td><td>{item.UnitPrice:F2}</td><td>{item.TaxAmount:F2}</td><td>{item.LineTotal:F2}</td></tr>");
        body.Append($"</table><p>Subtotal: {invoice.Subtotal:F2}<br>Discount: {invoice.DiscountAmount:F2}<br>GST: {invoice.TaxAmount:F2}</p><p><strong>Grand Total: {invoice.GrandTotal:F2}</strong><br>Paid: {invoice.PaidAmount:F2} · Balance: {invoice.BalanceAmount:F2}</p><script>window.print()</script>");
        var document = printing.Document(new DocumentInput("SALES_INVOICE", invoice.InvoiceNumber, "GST INVOICE", body.ToString(), paper.ToUpperInvariant(), "html"));
        return Encoding.UTF8.GetString(document.Data);
    }

    public byte[] Export(IReadOnlyCollection<SalesInvoice> invoices)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Sales");
        string[] headers = ["Invoice Number", "Date", "Customer", "Status", "Subtotal", "Discount", "Tax", "Grand Total", "Paid", "Balance"];
        for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
        sheet.Row(1).Style.Font.Bold = true;
        var row = 2;
        foreach (var invoice in invoices)
        {
            sheet.Cell(row, 1).Value = invoice.InvoiceNumber;sheet.Cell(row, 2).Value = invoice.InvoiceDate.DateTime;sheet.Cell(row, 3).Value = invoice.Customer?.CustomerName;sheet.Cell(row, 4).Value = invoice.Status;sheet.Cell(row, 5).Value = invoice.Subtotal;sheet.Cell(row, 6).Value = invoice.DiscountAmount;sheet.Cell(row, 7).Value = invoice.TaxAmount;sheet.Cell(row, 8).Value = invoice.GrandTotal;sheet.Cell(row, 9).Value = invoice.PaidAmount;sheet.Cell(row, 10).Value = invoice.BalanceAmount;row++;
        }
        sheet.Columns().AdjustToContents();using var stream = new MemoryStream();book.SaveAs(stream);return stream.ToArray();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
