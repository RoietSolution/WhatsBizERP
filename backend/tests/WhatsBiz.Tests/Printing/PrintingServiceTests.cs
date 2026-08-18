using System.Text;
using FluentAssertions;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.Infrastructure.Printing;
namespace WhatsBiz.Tests.Printing;
public sealed class PrintingServiceTests
{
    private readonly PrintingService service = new();
    [Theory]
    [InlineData("CODE128", "ABC-123")]
    [InlineData("EAN13", "5901234123457")]
    [InlineData("EAN8", "96385074")]
    [InlineData("UPC", "036000291452")]
    [InlineData("CODE39", "ABC123")]
    public void BarcodeProducesSvg(string format, string value) { var result = service.Barcode(new(value, format)); result.ContentType.Should().Be("image/svg+xml"); Encoding.UTF8.GetString(result.Data).Should().Contain("<svg"); }
    [Fact] public void QrCodeProducesSvg() { var result = service.QrCode(new("https://whatsbiz.local/invoice/1")); Encoding.UTF8.GetString(result.Data).Should().Contain("<svg"); }
    [Theory][InlineData("58MM")][InlineData("80MM")][InlineData("A4")] public void DocumentProducesPrintableHtml(string paper) { var result = service.Document(new("SALES_INVOICE", "INV-1", "GST Invoice", "<p>Total 100</p>", paper, "html")); result.ContentType.Should().Be("text/html"); Encoding.UTF8.GetString(result.Data).Should().Contain($"size:{paper.ToLowerInvariant().Replace("a4", "210mm")}"); }
    [Fact] public void DocumentProducesPdf() { var result = service.Document(new("GST_REPORT", "GST-1", "GST Report", "<p>Tax 18</p>", Output: "pdf")); Encoding.ASCII.GetString(result.Data[..8]).Should().StartWith("%PDF-1.4"); }
    [Fact] public void LabelProducesRequestedCopies() { var result = service.Label(new("PRODUCT", "Tea", "P1", "ABC123", 90, 100, 50, 25, 3, "CODE128", "html")); Encoding.UTF8.GetString(result.Data).Split("<article>").Length.Should().Be(4); }
}
