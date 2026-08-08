#pragma warning disable CA1305
using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.Domain.POS;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSDocumentService(IPrintingService printing, IConfiguration configuration) : IPOSDocumentService
{
    private const string ReceiptCss = """.receipt{width:72mm;max-width:72mm;margin:0 auto;font-family:"Courier New",monospace;font-size:12px;line-height:1.25;color:#000;background:#fff}.receipt *{box-sizing:border-box}.receipt-header,.receipt-title,.receipt-center,.receipt-footer{text-align:center}.receipt-logo{display:block;max-width:42mm;max-height:16mm;margin:0 auto 3mm;object-fit:contain}.store-name{margin:0;font-size:18px;font-weight:700;letter-spacing:.2px}.tagline{margin:1mm 0;font-size:11px}.receipt p{margin:1mm 0}.separator{border:0;border-top:1px dashed #000;margin:3mm 0}.receipt-title h1{margin:0;font-size:20px;font-weight:700}.invoice-number{font-size:16px;font-weight:700}.meta{display:grid;grid-template-columns:1fr 1fr;gap:1mm 4mm;text-align:left}.meta-row{display:flex;justify-content:space-between;gap:2mm;white-space:nowrap}.meta-label{font-weight:700}.items{width:100%;border-collapse:collapse;table-layout:fixed;font-size:11px}.items th{font-weight:700;text-align:right;border-bottom:1px dashed #000;padding:1mm 0}.items td{padding:1mm 0;vertical-align:top;text-align:right;overflow-wrap:anywhere}.items th:first-child,.items td:first-child{width:28%;text-align:left}.items th:nth-child(2),.items td:nth-child(2){width:9%}.items th:nth-child(3),.items td:nth-child(3){width:15%}.items th:nth-child(4),.items td:nth-child(4){width:14%;padding-left:1mm}.items th:nth-child(5),.items td:nth-child(5){width:14%;padding-left:1mm}.items th:nth-child(6),.items td:nth-child(6){width:20%}.total-row{display:flex;justify-content:space-between;gap:3mm}.grand-total,.total-amount{font-size:18px;font-weight:700}.amount-words{font-weight:700;text-align:center;margin:3mm 0}.feedback-qr{width:30mm;height:30mm;display:block;margin:2mm auto}.receipt-footer{margin-top:3mm}.receipt-footer p{margin:1mm 0}@media print{@page{size:80mm auto;margin:4mm}html,body{width:80mm;margin:0;padding:0}.receipt{width:72mm;max-width:72mm}.no-print{display:none}}""";
    public string InvoiceHtml(SalesInvoice invoice, string paper)
    {
        var logo = configuration["Printing:InvoiceLogoUrl"] ?? DemoLogo;
        var storeName = configuration["Printing:StoreName"] ?? "KhataDhari Retail Store";
        var tagline = configuration["Printing:Tagline"] ?? "Smart billing. Simple business.";
        var address = configuration["Printing:Address"] ?? invoice.Warehouse.WarehouseName;
        var city = configuration["Printing:City"] ?? "India";
        var phone = configuration["Printing:Phone"];
        var email = configuration["Printing:Email"];
        var gstin = configuration["Printing:GSTIN"];
        var fssai = configuration["Printing:FSSAI"];
        var cashier = invoice.CreatedBy ?? "POS Cashier";
        var counter = invoice.CounterId?.ToString("N")[..6] ?? "Main";
        var paymentMode = string.Join(", ", invoice.Payments.Select(x => x.PaymentMethod.MethodName));
        var qr = Convert.ToBase64String(printing.QrCode(new QRCodeInput($"{invoice.InvoiceNumber}|{invoice.GrandTotal:0.00}", 4)).Data);
        var body = new StringBuilder($"""
                    <style>{ReceiptCss}</style>
            <main class="receipt">
              <header class="receipt-header">
                <img class="receipt-logo" src="{EncodeAttribute(logo)}" alt="Store logo" />
                <h2 class="store-name">{Encode(storeName)}</h2>
                <p class="tagline">{Encode(tagline)}</p>
                <p>{Encode(address)}<br />{Encode(city)}</p>
                {OptionalLine(phone, email)}
                {OptionalLine(gstin is null ? null : "GSTIN: " + gstin, null)}
                {OptionalLine(fssai is null ? null : "FSSAI: " + fssai, null)}
              </header>
              <hr class="separator" />
              <section class="receipt-title">
                <h1>GST INVOICE</h1>
                <div class="invoice-number">Bill No: {Encode(invoice.InvoiceNumber)}</div>
              </section>
              <hr class="separator" />
              <section class="meta" aria-label="Invoice information">
                <div><div class="meta-row"><span class="meta-label">Date</span><span>{invoice.InvoiceDate:dd-MMM-yyyy}</span></div><div class="meta-row"><span class="meta-label">Time</span><span>{invoice.InvoiceDate:HH:mm}</span></div><div class="meta-row"><span class="meta-label">Cashier</span><span>{Encode(cashier)}</span></div><div class="meta-row"><span class="meta-label">Customer</span><span>{Encode(invoice.Customer?.CustomerName ?? "Walk-in")}</span></div></div>
                <div><div class="meta-row"><span class="meta-label">Counter</span><span>{Encode(counter)}</span></div><div class="meta-row"><span class="meta-label">Receipt No</span><span>{Encode(invoice.InvoiceNumber)}</span></div><div class="meta-row"><span class="meta-label">Payment</span><span>{Encode(string.IsNullOrWhiteSpace(paymentMode) ? "Cash" : paymentMode)}</span></div><div class="meta-row"><span class="meta-label">Terminal</span><span>{Encode(configuration["Printing:Terminal"] ?? "POS")}</span></div></div>
              </section>
              <hr class="separator" />
              <table class="items"><thead><tr><th>Item</th><th>Qty</th><th>Rate</th><th>GST%</th><th>GST</th><th>Amount</th></tr></thead><tbody>
        """);
        foreach (var item in invoice.Items)
            body.Append($"<tr><td>{Encode(item.Product.ProductName)}</td><td>{Quantity(item.Quantity)}</td><td>{Money(item.UnitPrice)}</td><td>{item.TaxPercentage:0.##}</td><td>{Money(item.TaxAmount)}</td><td>{Money(item.LineTotal)}</td></tr>");
        body.Append($"""
              </tbody></table>
              <hr class="separator" />
              <section aria-label="Invoice totals">
                <div class="total-row"><span>Subtotal</span><span>{Money(invoice.Subtotal)}</span></div>
                <div class="total-row"><span>Discount</span><span>{Money(invoice.DiscountAmount)}</span></div>
                <div class="total-row"><span>Taxable Amount</span><span>{Money(invoice.Subtotal - invoice.DiscountAmount)}</span></div>
                <div class="total-row"><span>Total GST</span><span>{Money(invoice.TaxAmount)}</span></div>
                <hr class="separator" />
                <div class="total-row grand-total"><span>Grand Total</span><span>₹{Money(invoice.GrandTotal)}</span></div>
                <hr class="separator" />
                <div class="total-row"><span>Paid</span><span>₹{Money(invoice.PaidAmount)}</span></div>
                <div class="total-row"><span>Balance</span><span>₹{Money(invoice.BalanceAmount)}</span></div>
                <hr class="separator" />
                <div class="total-row total-amount"><span>Total Amount</span><span>₹{Money(invoice.GrandTotal)}</span></div>
              </section>
              <p class="amount-words">{AmountInWords(invoice.GrandTotal)}</p>
              <hr class="separator" />
              <section class="receipt-center"><p><strong>Scan to Share Feedback</strong></p><img class="feedback-qr" src="data:image/svg+xml;base64,{qr}" alt="Feedback QR code" /><small>Your feedback helps us improve.</small></section>
              <footer class="receipt-footer"><hr class="separator" /><p>Goods once sold will not be taken back.</p><p><strong>Thank you for shopping with us!</strong></p><p>Visit Again</p></footer>
            </main><script>window.print()</script>
        """);
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
            sheet.Cell(row, 1).Value = invoice.InvoiceNumber; sheet.Cell(row, 2).Value = invoice.InvoiceDate.DateTime; sheet.Cell(row, 3).Value = invoice.Customer?.CustomerName; sheet.Cell(row, 4).Value = invoice.Status; sheet.Cell(row, 5).Value = invoice.Subtotal; sheet.Cell(row, 6).Value = invoice.DiscountAmount; sheet.Cell(row, 7).Value = invoice.TaxAmount; sheet.Cell(row, 8).Value = invoice.GrandTotal; sheet.Cell(row, 9).Value = invoice.PaidAmount; sheet.Cell(row, 10).Value = invoice.BalanceAmount; row++;
        }
        sheet.Columns().AdjustToContents(); using var stream = new MemoryStream(); book.SaveAs(stream); return stream.ToArray();
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string OptionalLine(string? first, string? second) => string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second) ? "" : $"<p>{Encode(first ?? "")}{(string.IsNullOrWhiteSpace(second) ? "" : " · " + Encode(second))}</p>";
    private static string AmountInWords(decimal amount)
    {
        var whole = (long)decimal.Truncate(amount);
        var paise = (long)Math.Round((amount - whole) * 100, MidpointRounding.AwayFromZero);
        return paise == 0
            ? $"Rupees {NumberWords(whole)} Only"
            : $"Rupees {NumberWords(whole)} and {NumberWords(paise)} Paise Only";
    }    private static string NumberWords(long number)
    {
        if (number == 0) return "Zero";
        var groups = new[] { (10000000L, "Crore"), (100000L, "Lakh"), (1000L, "Thousand") };
        var words = new List<string>();
        foreach (var (value, label) in groups)
        {
            if (number < value) continue;
            words.Add($"{BelowThousand(number / value)} {label}");
            number %= value;
        }
        if (number > 0) words.Add(BelowThousand(number));
        return string.Join(" ", words);
    }

    private static string BelowThousand(long number)
    {
        var ones = new[] { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
        var tens = new[] { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };
        var words = new List<string>();
        if (number >= 100) { words.Add($"{ones[number / 100]} Hundred"); number %= 100; }
        if (number >= 20) { words.Add(tens[number / 10]); number %= 10; }
        if (number > 0) words.Add(ones[number]);
        return string.Join(" ", words);
    }
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string EncodeAttribute(string value) => WebUtility.HtmlEncode(value);
    private const string DemoLogo = "data:image/svg+xml,%3Csvg xmlns=%27http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%27 viewBox=%270 0 160 48%27%3E%3Crect width=%27160%27 height=%2748%27 rx=%278%27 fill=%27%231d4ed8%27%2F%3E%3Ctext x=%2780%27 y=%2731%27 text-anchor=%27middle%27 font-family=%27Arial%27 font-size=%2720%27 font-weight=%27bold%27 fill=%27white%27%3EWhatsBiz%3C%2Ftext%3E%3C%2Fsvg%3E";
}
