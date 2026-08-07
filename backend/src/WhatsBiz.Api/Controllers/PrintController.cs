using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Printing;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Api.Controllers;
[ApiController, Route("api/print")]
public sealed class PrintController(ISender sender) : ControllerBase
{
    [HttpGet("template"), HasPermission(Permissions.Print.View)] public Task<IReadOnlyCollection<PrintTemplateDto>> Templates([FromQuery] string? documentType, CancellationToken token) => sender.Send(new GetPrintTemplates(documentType), token);
    [HttpPost("barcode"), HasPermission(Permissions.Print.Barcode)] public async Task<IActionResult> Barcode(BarcodeInput input, CancellationToken token) => Artifact(await sender.Send(new GenerateBarcode(input), token));
    [HttpPost("qrcode"), HasPermission(Permissions.Print.Barcode)] public async Task<IActionResult> QRCode(QRCodeInput input, CancellationToken token) => Artifact(await sender.Send(new GenerateQRCode(input), token));
    [HttpPost("document"), HasPermission(Permissions.Print.Document)] public async Task<IActionResult> Document(DocumentInput input, CancellationToken token) => Artifact(await sender.Send(new GenerateDocument(input), token));
    [HttpPost("label"), HasPermission(Permissions.Print.Document)] public async Task<IActionResult> Label(LabelInput input, CancellationToken token) => Artifact(await sender.Send(new GenerateLabel(input), token));
    [HttpGet("printers"), HasPermission(Permissions.Print.View)] public Task<IReadOnlyCollection<PrinterDto>> Printers(CancellationToken token) => sender.Send(new GetPrinters(), token);
    [HttpPost("printers"), HasPermission(Permissions.Print.Settings)] public async Task<IActionResult> Printer(PrinterInput input, CancellationToken token) { await sender.Send(new SavePrinter(input), token); return NoContent(); }
    private FileContentResult Artifact(PrintArtifact x) => File(x.Data, x.ContentType, x.FileName);
}
