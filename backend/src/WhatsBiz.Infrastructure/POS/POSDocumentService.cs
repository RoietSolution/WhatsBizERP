#pragma warning disable CA1305
using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Administration;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.Domain.POS;

namespace WhatsBiz.Infrastructure.POS;

public sealed class POSDocumentService(IPrintingService printing, IConfiguration configuration) : IPOSDocumentService
{
    private const int PrintDocumentVersion = 1;
    private const int ReceiptCharactersPerLine = 32;
    private const string ReceiptCss = """
        .receipt{margin:0 auto;font-family:"Courier New",monospace;font-size:13px;line-height:1.35;color:#000;background:#fff}.receipt *{box-sizing:border-box}.receipt-header,.receipt-title,.receipt-center,.receipt-footer{text-align:center}.receipt-logo{display:block;max-width:42mm;max-height:16mm;margin:0 auto 3mm;object-fit:contain}.store-name{margin:0;font-size:18px;font-weight:700}.tagline,.receipt p{margin:1mm 0}.separator{border:0;border-top:1px dashed #000;margin:3mm 0}.receipt-title h1{margin:0;font-size:20px}.meta{display:grid;grid-template-columns:1fr 1fr;gap:1mm 4mm;text-align:left;font-size:13px}.meta-row{display:grid;grid-template-columns:max-content minmax(0,1fr);gap:2mm;min-width:0}.meta-row>span:last-child{text-align:right;overflow-wrap:normal;word-break:normal}.meta-wide{grid-column:1/-1}.meta-label{font-weight:700;white-space:nowrap}.items{width:100%;border-collapse:collapse;table-layout:fixed;font-size:13px}.items th,.items td{padding:1.5mm .5mm;text-align:right;vertical-align:top}.items th{border-bottom:1px dashed #000;white-space:nowrap;font-weight:700}.items th:first-child,.items td:first-child{width:29%;padding-left:0;text-align:left;overflow-wrap:normal;word-break:normal}.items th:nth-child(2),.items td:nth-child(2){width:8%}.items th:nth-child(3),.items td:nth-child(3){width:18%}.items th:nth-child(4),.items td:nth-child(4){width:10%}.items th:nth-child(5),.items td:nth-child(5){width:15%}.items th:nth-child(6),.items td:nth-child(6){width:20%;padding-right:0}.items td:not(:first-child){white-space:nowrap}.items .item-name{font-weight:600}.total-row{display:flex;justify-content:space-between;gap:3mm;font-size:13px}.grand-total,.total-amount{font-size:18px;font-weight:700}.amount-words{font-weight:700;text-align:center;margin:3mm 0}.feedback-qr{width:60mm;height:60mm;display:block;margin:2mm auto;image-rendering:pixelated;shape-rendering:crispEdges}.receipt-footer{margin-top:3mm}
        .paper-58mm .receipt{width:54mm;max-width:54mm;font-family:"Arial Narrow",Arial,sans-serif;font-size:12px;line-height:1.3}.paper-58mm .receipt-logo{max-width:34mm;max-height:12mm}.paper-58mm .store-name{font-size:16px}.paper-58mm .receipt-title h1{font-size:16px}.paper-58mm .meta{display:block;font-size:12px}.paper-58mm .meta-row{display:grid;grid-template-columns:18mm minmax(0,1fr);margin-bottom:.5mm}.paper-58mm .items{display:block;font-size:12px}.paper-58mm .items thead{display:none}.paper-58mm .items tbody{display:block}.paper-58mm .items tr{display:grid;grid-template-columns:var(--items-template);padding:1.2mm 0;border-bottom:1px dashed #000}.paper-58mm .items td{display:block;width:auto!important;padding:.45mm .25mm;text-align:right;white-space:nowrap}.paper-58mm .items td:first-child{grid-column:1/-1;width:auto;padding:0 0 .7mm;text-align:left;font-size:12px;font-weight:700;white-space:normal}.paper-58mm .items td:not(:first-child)::before{content:attr(data-label);display:block;margin-bottom:.2mm;font-size:8px;font-weight:700;color:#222}.paper-58mm .items td:nth-child(2){padding-left:0;text-align:left}.paper-58mm .items td:last-child{padding-right:0;font-weight:700}.paper-58mm .grand-total,.paper-58mm .total-amount{font-size:15px}.paper-58mm .feedback-qr{width:50mm;height:50mm}.paper-58mm .separator{margin:2mm 0}
        .paper-80mm .receipt{width:72mm;max-width:72mm;font-size:13px}.paper-80mm .items{font-size:12.5px}.paper-80mm .feedback-qr{width:60mm;height:60mm}
        .paper-a4 .receipt{width:100%;max-width:none;font-family:Arial,sans-serif;font-size:11pt}.paper-a4 .receipt-header{text-align:left;position:relative;padding-right:50mm;min-height:24mm}.paper-a4 .receipt-logo{position:absolute;right:0;top:0}.paper-a4 .receipt-title{text-align:left}.paper-a4 .meta{border:1px solid #ccc;padding:4mm;gap:8mm}.paper-a4 .items{font-size:11pt;margin-top:4mm}.paper-a4 .items th{background:#f2f4f7;border-bottom:1px solid #999;padding:2mm}.paper-a4 .items td{border-bottom:1px solid #ddd;padding:2mm}.paper-a4 .receipt-center{display:none}.paper-a4 .receipt-footer{margin-top:12mm;border-top:1px solid #999;padding-top:4mm}.paper-a4 .total-row{margin-left:auto;width:82mm;padding:1mm 0}
        .paper-58mm .items tr{grid-template-columns:var(--items-template)}.items col{width:auto!important}.items th,.items td{width:auto!important}
        @media print{html,body{margin:0;padding:0}.no-print{display:none!important}.receipt{overflow:visible}.items tr{page-break-inside:avoid}}
        """;
    public string InvoiceHtml(SalesInvoice invoice, string paper, POSInvoicePrintContext context)
    {
        var receipt = BuildReceiptPresentation(invoice, context);
        var logo = configuration["Printing:InvoiceLogoUrl"];
        var company = context.Company;
        var storeName = company.CompanyName;
        var legalName = string.Equals(company.LegalName, company.CompanyName, StringComparison.OrdinalIgnoreCase) ? null : company.LegalName;
        var tagline = configuration["Printing:Tagline"];
        var address = CompanyAddress(company);
        var phone = company.Phone;
        var email = company.Email;
        var gstin = company.GSTIN;
        var fssai = configuration["Printing:FSSAI"];
        var cashier = invoice.CreatedBy ?? "";
        var counter = invoice.CounterId?.ToString("N")[..6] ?? "";
        var paymentMode = string.Join(", ", invoice.Payments.Select(x => x.PaymentMethod.MethodName));
        var options = context.Options ?? new POSInvoicePrintOptions();
        var counterLine = options.ShowCounter ? $"<div class=\"meta-row\"><span class=\"meta-label\">Counter</span><span>{Encode(counter)}</span></div>" : "";
        var terminalLine = options.ShowTerminal ? $"<div class=\"meta-row\"><span class=\"meta-label\">Terminal</span><span>{Encode(configuration["Printing:Terminal"] ?? "")}</span></div>" : "";
        var cashierLine = options.ShowCashier ? $"<div class=\"meta-row\"><span class=\"meta-label\">Cashier</span><span>{Encode(cashier)}</span></div>" : "";
        var gstHeaders = (options.ShowGstPercentage ? "<th>GST%</th>" : "") + (options.ShowGstAmount ? "<th>GST</th>" : "");
        var columnWidths = options.ShowGstPercentage && options.ShowGstAmount ? new[] { "42%", "10%", "17%", "13%", "18%" } : options.ShowGstPercentage || options.ShowGstAmount ? new[] { "48%", "11%", "19%", "22%" } : new[] { "57%", "13%", "30%" };
        var columnGroup = "<colgroup>" + string.Join("", columnWidths.Select(width => $"<col style=\"width:{width}\" />")) + "</colgroup>";
        var thermalTemplate = options.ShowGstPercentage && options.ShowGstAmount ? "1.25fr .55fr .85fr .65fr .9fr" : options.ShowGstPercentage || options.ShowGstAmount ? "1.55fr .6fr .7fr 1fr" : "1.8fr .65fr 1fr";
        var loyalty = context.Loyalty;
        var titleDetails = string.Join("", receipt.TitleDetails.Select(x =>
            x.Bold ? $"<p><strong>{Encode(x.Value)}</strong></p>" : $"<p>{Encode(x.Value)}</p>"));
        var body = new StringBuilder($"""
                    <style>{ReceiptCss}</style>
            <main class="receipt">
              <header class="receipt-header">
                {(string.IsNullOrWhiteSpace(logo) ? "" : $"<img class=\"receipt-logo\" src=\"{EncodeAttribute(logo)}\" alt=\"Store logo\" />")}
                <h2 class="store-name">{Encode(storeName)}</h2>
                {(string.IsNullOrWhiteSpace(legalName) ? "" : $"<p>{Encode(legalName)}</p>")}
                {(string.IsNullOrWhiteSpace(tagline) ? "" : $"<p class=\"tagline\">{Encode(tagline ?? "")}</p>")}
                {(string.IsNullOrWhiteSpace(address) ? "" : $"<p>{address}</p>")}
                {(options.ShowGstNumber ? OptionalLine(gstin is null ? null : "GSTIN: " + gstin, null) : "")}
                {OptionalLine(phone, email)}
                {OptionalLine(fssai is null ? null : "FSSAI: " + fssai, null)}
              </header>
              <hr class="separator" />
              <section class="receipt-title">
                <h1>{Encode(receipt.Title)}</h1>
                {titleDetails}
                <div class="invoice-number">Bill No: {Encode(invoice.InvoiceNumber)}</div>
              </section>
              <hr class="separator" />
              <section class="meta" aria-label="Invoice information">
                <div class="meta-row"><span class="meta-label">Date</span><span>{invoice.InvoiceDate:dd-MMM-yyyy}</span></div>
                <div class="meta-row"><span class="meta-label">Time</span><span>{invoice.InvoiceDate:HH:mm}</span></div>
                {counterLine}
                {terminalLine}
                {cashierLine}
                <div class="meta-row"><span class="meta-label">Payment</span><span>{Encode(string.IsNullOrWhiteSpace(paymentMode) ? "Unpaid" : paymentMode)}</span></div>
                <div class="meta-row meta-wide"><span class="meta-label">Customer</span><span>{Encode(invoice.Customer?.CustomerName ?? "Walk-in")}</span></div>
                <div class="meta-row meta-wide"><span class="meta-label">Receipt No</span><span>{Encode(invoice.InvoiceNumber)}</span></div>
              </section>
              <hr class="separator" />
              <table class="items" style="--items-template:{thermalTemplate}">{columnGroup}<thead><tr><th>Item</th><th>Qty</th><th>Rate</th>{gstHeaders}<th>Amount</th></tr></thead><tbody>
        """);
        foreach (var item in invoice.Items)
        {
            var gstPercentCell = options.ShowGstPercentage ? $"<td data-label=\"GST%\">{item.TaxPercentage:0.##}</td>" : "";
            var gstAmountCell = options.ShowGstAmount ? $"<td data-label=\"GST\">{Money(item.TaxAmount)}</td>" : "";
            body.Append($"<tr><td class=\"item-name\">{Encode(item.Product.ProductName)}</td><td data-label=\"Qty\">{Quantity(item.Quantity)}</td><td data-label=\"Rate\">{Money(item.UnitPrice)}</td>{gstPercentCell}{gstAmountCell}<td data-label=\"Amount\">{Money(item.LineTotal)}</td></tr>");
        }
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
                {(loyalty.Redeemed > 0 ? $"<div class=\"total-row\"><span>Coins redeemed</span><span>{loyalty.Redeemed} (-₹{Money(loyalty.Discount)})</span></div>" : "")}
                {(loyalty.Earned > 0 ? $"<div class=\"total-row\"><span>Coins earned</span><span>{loyalty.Earned}</span></div>" : "")}
                <hr class="separator" />
                <div class="total-row total-amount"><span>Total Amount</span><span>₹{Money(invoice.GrandTotal)}</span></div>
              </section>
              <p class="amount-words">{receipt.AmountWords}</p>
              <hr class="separator" />
              {(string.IsNullOrWhiteSpace(context.PaymentQrDataUrl) ? "" : $"<section class=\"receipt-center payment-qr\"><p><strong>Scan to Pay</strong></p><img class=\"feedback-qr\" src=\"{EncodeAttribute(context.PaymentQrDataUrl)}\" alt=\"Payment QR code\" /></section>")}
              <footer class="receipt-footer"><hr class="separator" />{OptionalParagraph(receipt.Terms)}<p><strong>{Encode(receipt.Footer)}</strong></p></footer>
            </main><script>window.print()</script>
        """);
        var document = printing.Document(new DocumentInput("SALES_INVOICE", invoice.InvoiceNumber, receipt.Title, body.ToString(), paper.ToUpperInvariant(), "html", IncludeHeader: false));
        return Encoding.UTF8.GetString(document.Data);
    }

    public PrintDocumentDto InvoicePrintDocument(SalesInvoice invoice, POSInvoicePrintContext context)
    {
        var receipt = BuildReceiptPresentation(invoice, context);
        var operations = new List<PrintOperationDto>();
        void Text(string value, string alignment = "LEFT", bool bold = false) =>
            operations.Add(new("TEXT", PrinterText(value), alignment, bold));
        void Columns(ReceiptPair pair) =>
            operations.Add(new("COLUMNS", Bold: pair.Bold, Left: PrinterText(pair.Left), Right: PrinterValue(pair)));
        void Separator() => operations.Add(new("SEPARATOR", Value: "-"));

        foreach (var line in receipt.Header) Text(line.Value, "CENTER", line.Bold);
        Separator();
        Text(receipt.Title, "CENTER", true);
        foreach (var line in receipt.TitleDetails) Text(line.Value, "CENTER", line.Bold);
        Text($"Bill No: {invoice.InvoiceNumber}", "CENTER");
        Separator();
        foreach (var row in receipt.Metadata) Columns(row);
        Separator();
        foreach (var item in receipt.Items)
        {
            Text(item.Name, bold: true);
            foreach (var row in item.Rows) Columns(row);
            Separator();
        }
        foreach (var row in receipt.Summary) Columns(row);
        Separator();
        Columns(receipt.GrandTotal);
        Separator();
        foreach (var row in receipt.Settlement) Columns(row);
        Separator();
        Columns(receipt.TotalAmount);
        Text(receipt.AmountWords, "CENTER", true);
        Separator();
        if (!string.IsNullOrWhiteSpace(receipt.Terms)) Text(receipt.Terms!, "CENTER");
        Text(receipt.Footer, "CENTER", true);
        operations.Add(new("FEED", Count: 3));
        return new(PrintDocumentVersion, "58MM", ReceiptCharactersPerLine, operations);
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

    private ReceiptPresentation BuildReceiptPresentation(SalesInvoice invoice, POSInvoicePrintContext context)
    {
        var company = context.Company;
        var options = context.Options ?? new POSInvoicePrintOptions();
        var provisional = invoice.Status is "HELD" or "SUSPENDED";
        var header = new List<ReceiptText> { new(company.CompanyName, true) };
        if (!string.Equals(company.LegalName, company.CompanyName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(company.LegalName))
            header.Add(new(company.LegalName!));
        AddText(header, configuration["Printing:Tagline"]);
        foreach (var line in CompanyAddressLines(company)) header.Add(new(line));
        if (options.ShowGstNumber && !string.IsNullOrWhiteSpace(company.GSTIN)) header.Add(new($"GSTIN: {company.GSTIN}"));
        var contact = string.Join(" \u00B7 ", new[] { company.Phone, company.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        AddText(header, contact);
        var fssai = configuration["Printing:FSSAI"];
        if (!string.IsNullOrWhiteSpace(fssai)) header.Add(new($"FSSAI: {fssai}"));

        var titleDetails = new List<ReceiptText>();
        if (provisional)
        {
            titleDetails.Add(new($"Status: {invoice.Status}"));
            titleDetails.Add(new("NOT A GST TAX INVOICE", true));
        }

        var metadata = new List<ReceiptPair>
        {
            new("Date", invoice.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)),
            new("Time", invoice.InvoiceDate.ToString("HH:mm", CultureInfo.InvariantCulture))
        };
        if (options.ShowCounter) metadata.Add(new("Counter", invoice.CounterId?.ToString("N")[..6] ?? ""));
        if (options.ShowTerminal) metadata.Add(new("Terminal", configuration["Printing:Terminal"] ?? ""));
        if (options.ShowCashier) metadata.Add(new("Cashier", invoice.CreatedBy ?? ""));
        var payment = string.Join(", ", invoice.Payments.Select(x => x.PaymentMethod.MethodName));
        metadata.Add(new("Payment", string.IsNullOrWhiteSpace(payment) ? "Unpaid" : payment));
        metadata.Add(new("Customer", invoice.Customer?.CustomerName ?? "Walk-in"));
        metadata.Add(new("Receipt No", invoice.InvoiceNumber));

        var items = invoice.Items.Select(item =>
        {
            var rows = new List<ReceiptPair>
            {
                new($"Qty {Quantity(item.Quantity)}", $"Rate {Money(item.UnitPrice)}", RightIsMoney: true)
            };
            if (options.ShowGstPercentage || options.ShowGstAmount)
            {
                var left = options.ShowGstPercentage ? $"GST% {Quantity(item.TaxPercentage)}" : "";
                var right = options.ShowGstAmount ? $"GST {Money(item.TaxAmount)}" : "";
                rows.Add(new(left, right, RightIsMoney: options.ShowGstAmount));
            }
            rows.Add(new("Amount", Money(item.LineTotal), Bold: true, RightIsMoney: true));
            return new ReceiptItem(item.Product.ProductName, rows);
        }).ToArray();

        var summary = new[]
        {
            new ReceiptPair("Subtotal", Money(invoice.Subtotal), RightIsMoney: true),
            new ReceiptPair("Discount", Money(invoice.DiscountAmount), RightIsMoney: true),
            new ReceiptPair("Taxable Amount", Money(invoice.Subtotal - invoice.DiscountAmount), RightIsMoney: true),
            new ReceiptPair("Total GST", Money(invoice.TaxAmount), RightIsMoney: true)
        };
        var settlement = new List<ReceiptPair>
        {
            new("Paid", Money(invoice.PaidAmount), RightIsMoney: true),
            new("Balance", Money(invoice.BalanceAmount), RightIsMoney: true)
        };
        if (context.Loyalty.Redeemed > 0) settlement.Add(new("Coins redeemed", $"{context.Loyalty.Redeemed} (-Rs. {Money(context.Loyalty.Discount)})"));
        if (context.Loyalty.Earned > 0) settlement.Add(new("Coins earned", context.Loyalty.Earned.ToString(CultureInfo.InvariantCulture)));

        return new(
            header,
            provisional ? "PROVISIONAL BILL" : "GST INVOICE",
            titleDetails,
            metadata,
            items,
            summary,
            new("Grand Total", Money(invoice.GrandTotal), Bold: true, RightIsMoney: true),
            settlement,
            new("Total Amount", Money(invoice.GrandTotal), Bold: true, RightIsMoney: true),
            AmountInWords(invoice.GrandTotal),
            company.TermsAndConditions,
            string.IsNullOrWhiteSpace(company.InvoiceFooter) ? "Thank you for shopping with us!" : company.InvoiceFooter!);
    }

    private static void AddText(ICollection<ReceiptText> lines, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(new(value));
    }

    private static string PrinterValue(ReceiptPair pair)
    {
        if (!pair.RightIsMoney || string.IsNullOrWhiteSpace(pair.Right)) return PrinterText(pair.Right);
        var split = pair.Right.LastIndexOf(' ');
        return split < 0
            ? $"Rs. {pair.Right}"
            : $"{pair.Right[..split]} Rs. {pair.Right[(split + 1)..]}";
    }

    private static string PrinterText(string value) => value.Replace("\u20B9", "Rs. ", StringComparison.Ordinal).Replace("\u00B7", "/", StringComparison.Ordinal);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Quantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string OptionalLine(string? first, string? second) => string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second) ? "" : $"<p>{Encode(first ?? "")}{(string.IsNullOrWhiteSpace(second) ? "" : " · " + Encode(second))}</p>";
    private static string OptionalParagraph(string? value) => string.IsNullOrWhiteSpace(value) ? "" : $"<p>{Encode(value)}</p>";
    private static string CompanyAddress(CompanyDto company)
    {
        var cityLine = string.Join(", ", new[] { company.City, company.State, company.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.Join("<br />", new[] { company.AddressLine1, company.AddressLine2, cityLine, company.Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Encode(x!)));
    }
    private static IReadOnlyCollection<string> CompanyAddressLines(CompanyDto company)
    {
        var cityLine = string.Join(", ", new[] { company.City, company.State, company.PostalCode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new[] { company.AddressLine1, company.AddressLine2, cityLine, company.Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray();
    }

    private sealed record ReceiptPresentation(
        IReadOnlyCollection<ReceiptText> Header,
        string Title,
        IReadOnlyCollection<ReceiptText> TitleDetails,
        IReadOnlyCollection<ReceiptPair> Metadata,
        IReadOnlyCollection<ReceiptItem> Items,
        IReadOnlyCollection<ReceiptPair> Summary,
        ReceiptPair GrandTotal,
        IReadOnlyCollection<ReceiptPair> Settlement,
        ReceiptPair TotalAmount,
        string AmountWords,
        string? Terms,
        string Footer);
    private sealed record ReceiptText(string Value, bool Bold = false);
    private sealed record ReceiptPair(string Left, string Right, bool Bold = false, bool RightIsMoney = false);
    private sealed record ReceiptItem(string Name, IReadOnlyCollection<ReceiptPair> Rows);
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
}
