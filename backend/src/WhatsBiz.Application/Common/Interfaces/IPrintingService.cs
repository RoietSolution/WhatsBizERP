using WhatsBiz.Application.Features.Printing;
namespace WhatsBiz.Application.Common.Interfaces;
public interface IPrintRepository { Task<IReadOnlyCollection<PrintTemplateDto>> Templates(string? type, CancellationToken token); Task<IReadOnlyCollection<PrinterDto>> Printers(CancellationToken token); Task SavePrinter(PrinterInput x, CancellationToken token); }
public interface IPrintingService { PrintArtifact Barcode(BarcodeInput x); PrintArtifact QrCode(QRCodeInput x); PrintArtifact Document(DocumentInput x); PrintArtifact Label(LabelInput x); }
