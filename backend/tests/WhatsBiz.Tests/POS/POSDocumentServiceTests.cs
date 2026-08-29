using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Administration;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.Domain.POS;
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
        var service = new POSDocumentService(new PassthroughPrintingService(), configuration);
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
        html.Should().NotContain("Nidhi Saari Store");
    }

    private sealed class PassthroughPrintingService : IPrintingService
    {
        public PrintArtifact Barcode(BarcodeInput x) => throw new NotSupportedException();
        public PrintArtifact QrCode(QRCodeInput x) => new(Encoding.UTF8.GetBytes("<svg />"), "image/svg+xml", "qr.svg");
        public PrintArtifact Document(DocumentInput x) => new(Encoding.UTF8.GetBytes(x.BodyHtml), "text/html", "invoice.html");
        public PrintArtifact Label(LabelInput x) => throw new NotSupportedException();
    }
}
