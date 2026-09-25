using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Administration;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.Domain.POS;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.POS;

namespace WhatsBiz.Tests.POS;

public sealed class POSDocumentServiceTests
{
    [Fact]
    public void GstInvoiceUsesCurrentCompanyDetailsInsteadOfApplicationDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Printing:Terminal"] = "QA-POS"
        }).Build();
        var printing = new PassthroughPrintingService();
        var service = new POSDocumentService(printing, configuration);
        var invoice = new SalesInvoice
        {
            InvoiceId = Guid.NewGuid(),
            InvoiceNumber = "INV-1001",
            InvoiceDate = new DateTimeOffset(2026, 8, 30, 10, 15, 0, TimeSpan.Zero),
            GrandTotal = 118,
            PaidAmount = 118,
            Status = "COMPLETED",
            CreatedBy = "cashier"
        };
        var company = new CompanyDto(
            Guid.NewGuid(), "KD", "Dynamic Retail Store", "Dynamic Retail Private Limited",
            "07ABCDE1234F1Z5", null, null, "42 Market Road", "Second Floor", "New Delhi",
            "Delhi", "07", "India", "110001", "billing@dynamic.example", "+91 9876543210",
            null, null, null, "Returns accepted within seven days.", "Thank you from Dynamic Retail.");

        var html = service.InvoiceHtml(invoice, "A4", new(company, new(5, 2, 20)));

        html.Should().Contain("Dynamic Retail Store");
        html.Should().Contain("Dynamic Retail Private Limited");
        html.Should().Contain("42 Market Road<br />Second Floor<br />New Delhi, Delhi, 110001<br />India");
        html.Should().Contain("GSTIN: 07ABCDE1234F1Z5");
        html.Should().Contain("billing@dynamic.example");
        html.Should().Contain("Returns accepted within seven days.");
        html.Should().Contain("Thank you from Dynamic Retail.");
        html.Should().Contain("Bill No: INV-1001");
        html.Should().Contain("Receipt No");
        html.Should().NotContain("Nidhi Saari Store");
        printing.LastDocument.Should().NotBeNull();
        printing.LastDocument!.IncludeHeader.Should().BeFalse();
    }

    [Fact]
    public void FiftyEightMillimeterInvoiceUsesReadableStackedItemLayout()
    {
        var service = new POSDocumentService(new PassthroughPrintingService(), new ConfigurationBuilder().Build());
        var invoice = new SalesInvoice
        {
            InvoiceId = Guid.NewGuid(),
            InvoiceNumber = "INV-58",
            InvoiceDate = DateTimeOffset.UtcNow,
            Status = "COMPLETED"
        };
        var company = new CompanyDto(
            Guid.NewGuid(), "KD", "Retail Store", "Retail Store", null, null, null, null, null,
            null, null, null, "India", null, null, null, null, null, null, null, null);

        var html = service.InvoiceHtml(invoice, "58MM", new(company, new(0, 0, 20)));

        html.Should().Contain(".paper-58mm .items tr{display:grid");
        html.Should().Contain(".paper-58mm .items{display:block;font-size:10.5px}");
        html.Should().NotContain(".paper-58mm .items{font-size:7px}");
    }

    [Theory]
    [InlineData(true, true, "GST%", "GST")]
    [InlineData(false, true, "", "GST")]
    [InlineData(true, false, "GST%", "")]
    [InlineData(false, false, "", "")]
    public void GstColumnsOnlyRenderWhenEnabled(bool showPercentage, bool showAmount, string percentageHeader, string amountHeader)
    {
        var service = new POSDocumentService(new PassthroughPrintingService(), new ConfigurationBuilder().Build());
        var html = service.InvoiceHtml(Invoice(), "80MM", new(Company(), new(0, 0, 20), null, new(true, true, true, showPercentage, showAmount)));
        if (string.IsNullOrEmpty(percentageHeader)) html.Should().NotContain(">GST%</th>"); else html.Should().Contain(">GST%</th>");
        if (string.IsNullOrEmpty(amountHeader)) html.Should().NotContain(">GST</th>"); else html.Should().Contain(">GST</th>");
        html.Should().Contain("--items-template:");
    }

    [Fact]
    public void GstInvoiceUsesConfiguredPaymentQrAndOmitsFeedbackQr()
    {
        var service = new POSDocumentService(new PassthroughPrintingService(), new ConfigurationBuilder().Build());
        var invoice = Invoice();
        var company = Company();

        var configured = service.InvoiceHtml(invoice, "80MM", new(company, new(0, 0, 20), "data:image/svg+xml;base64,VEVOQU5UX0E="));
        var unconfigured = service.InvoiceHtml(invoice, "80MM", new(company, new(0, 0, 20)));

        configured.Should().Contain("Scan to Pay").And.Contain("VEVOQU5UX0E=");
        configured.Should().NotContain("Scan to Share Feedback");
        unconfigured.Should().NotContain("payment-qr").And.NotContain("Payment QR code");
    }

    [Fact]
    public void CompletedInvoiceGeneratesVersionedPrintDocumentInExisting58mmOrder()
    {
        var service = new POSDocumentService(new PassthroughPrintingService(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Printing:Terminal"] = "QA-POS"
        }).Build());

        var document = service.InvoicePrintDocument(PrintInvoice("COMPLETED"), new(Company(), new(0, 0, 0)));
        var content = PrintableContent(document);

        document.Version.Should().Be(1);
        document.PaperWidth.Should().Be("58MM");
        document.CharactersPerLine.Should().Be(32);
        AssertInOrder(content,
            "Retail Store", "GST INVOICE", "Bill No: INV-BRIDGE-1",
            "Date", "Time", "Counter", "Terminal", "Cashier", "Payment", "Customer", "Receipt No",
            "Server Saved Item", "Qty 2", "Rate Rs. 125.00", "GST% 5", "GST Rs. 11.50", "Amount", "Rs. 251.50",
            "Subtotal", "Rs. 250.00", "Discount", "Rs. 10.00", "Taxable Amount", "Rs. 240.00",
            "Total GST", "Rs. 43.20", "Grand Total", "Rs. 283.40", "Paid", "Balance", "Total Amount",
            "Rupees Two Hundred Eighty Three and Forty Paise Only", "Thank you for shopping with us!");
        document.Operations.Last().Should().Be(new PrintOperationDto("FEED", Count: 3));
    }

    [Theory]
    [InlineData("HELD")]
    [InlineData("SUSPENDED")]
    public void NonFinalInvoicePresentationIsGeneratedOnlyByTheServer(string status)
    {
        var service = new POSDocumentService(new PassthroughPrintingService(), new ConfigurationBuilder().Build());
        var invoice = PrintInvoice(status);

        var document = service.InvoicePrintDocument(invoice, new(Company(), new(0, 0, 0)));
        var content = PrintableContent(document);
        var html = service.InvoiceHtml(invoice, "58MM", new(Company(), new(0, 0, 0)));

        AssertInOrder(content, "PROVISIONAL BILL", $"Status: {status}", "NOT A GST TAX INVOICE", "Bill No: INV-BRIDGE-1");
        content.Should().NotContain("|GST INVOICE|");
        html.Should().Contain("<h1>PROVISIONAL BILL</h1>").And.Contain($"Status: {status}").And.Contain("NOT A GST TAX INVOICE");
        html.Should().NotContain("<h1>GST INVOICE</h1>");
    }

    private static string PrintableContent(PrintDocumentDto document) => "|" + string.Join("|", document.Operations.SelectMany(x =>
        new[] { x.Value, x.Left, x.Right }.Where(value => !string.IsNullOrWhiteSpace(value)))) + "|";

    private static void AssertInOrder(string content, params string[] values)
    {
        var position = -1;
        foreach (var value in values)
        {
            var next = content.IndexOf(value, position + 1, StringComparison.Ordinal);
            next.Should().BeGreaterThan(position, $"'{value}' should follow the previous receipt section");
            position = next;
        }
    }

    private static SalesInvoice PrintInvoice(string status)
    {
        var invoice = new SalesInvoice
        {
            InvoiceId = Guid.NewGuid(),
            InvoiceNumber = "INV-BRIDGE-1",
            InvoiceDate = new DateTimeOffset(2026, 9, 25, 10, 30, 0, TimeSpan.FromHours(5.5)),
            CounterId = Guid.Parse("abcdef12-0000-0000-0000-000000000000"),
            CreatedBy = "cashier",
            Status = status,
            Subtotal = 250m,
            DiscountAmount = 10m,
            TaxAmount = 43.20m,
            RoundOff = 0.20m,
            GrandTotal = 283.40m,
            PaidAmount = status is "HELD" or "SUSPENDED" ? 0m : 283.40m
        };
        invoice.Items.Add(new SalesInvoiceItem
        {
            Product = new Product { ProductName = "Server Saved Item" },
            Quantity = 2m,
            UnitPrice = 125m,
            TaxPercentage = 5m,
            TaxAmount = 11.50m,
            LineTotal = 251.50m
        });
        if (status is not ("HELD" or "SUSPENDED"))
            invoice.Payments.Add(new SalesPayment { PaymentMethod = new PaymentMethod { MethodName = "Cash" }, Amount = 283.40m, Status = "COMPLETED" });
        return invoice;
    }

    private static SalesInvoice Invoice() => new()
    {
        InvoiceId = Guid.NewGuid(), InvoiceNumber = "INV-QR", InvoiceDate = DateTimeOffset.UtcNow, Status = "COMPLETED"
    };

    private static CompanyDto Company() => new(
        Guid.NewGuid(), "KD", "Retail Store", "Retail Store", null, null, null, null, null,
        null, null, null, "India", null, null, null, null, null, null, null, null);

    private sealed class PassthroughPrintingService : IPrintingService
    {
        public DocumentInput? LastDocument { get; private set; }
        public PrintArtifact Barcode(BarcodeInput x) => throw new NotSupportedException();
        public PrintArtifact QrCode(QRCodeInput x) => new(Encoding.UTF8.GetBytes("<svg />"), "image/svg+xml", "qr.svg");
        public PrintArtifact Document(DocumentInput x)
        {
            LastDocument = x;
            return new(Encoding.UTF8.GetBytes(x.BodyHtml), "text/html", "invoice.html");
        }
        public PrintArtifact Label(LabelInput x) => throw new NotSupportedException();
    }
}
