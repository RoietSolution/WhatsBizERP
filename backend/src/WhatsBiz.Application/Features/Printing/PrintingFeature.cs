using FluentValidation;using MediatR;using WhatsBiz.Application.Common.Interfaces;
namespace WhatsBiz.Application.Features.Printing;
public sealed record PrintTemplateDto(Guid Id,string Code,string Name,string DocumentType,string PaperType,bool IsDefault,string Content);
public sealed record PrinterDto(Guid Id,string PrinterName,string DisplayName,string PrinterType,string PaperSize,string? DocumentType,bool IsDefault,bool AutoCut,bool IsActive);
public sealed record PrinterInput(Guid? Id,string PrinterName,string DisplayName,string PrinterType,string PaperSize,string? DocumentType,bool IsDefault,bool AutoCut,bool IsActive);
public sealed record BarcodeInput(string Value,string Format="CODE128",int Width=300,int Height=100,bool ShowText=true);
public sealed record QRCodeInput(string Value,int PixelsPerModule=8,string ErrorCorrectionLevel="M");
public sealed record DocumentInput(string DocumentType,string DocumentNumber,string Title,string BodyHtml,string PaperType="A4",string Output="pdf",string? TemplateCode=null,bool AutoPrint=false);
public sealed record LabelInput(string LabelType,string ProductName,string? ProductCode,string? Barcode,decimal? Price,decimal? MRP,decimal WidthMm=50,decimal HeightMm=25,int Quantity=1,string BarcodeFormat="CODE128",string Output="html");
public sealed record PrintArtifact(byte[] Data,string ContentType,string FileName);
public sealed record GetPrintTemplates(string? DocumentType):IRequest<IReadOnlyCollection<PrintTemplateDto>>;
public sealed record GetPrinters:IRequest<IReadOnlyCollection<PrinterDto>>;
public sealed record SavePrinter(PrinterInput Input):IRequest;
public sealed record GenerateBarcode(BarcodeInput Input):IRequest<PrintArtifact>;
public sealed record GenerateQRCode(QRCodeInput Input):IRequest<PrintArtifact>;
public sealed record GenerateDocument(DocumentInput Input):IRequest<PrintArtifact>;
public sealed record GenerateLabel(LabelInput Input):IRequest<PrintArtifact>;
internal sealed class BarcodeValidator:AbstractValidator<GenerateBarcode>{private static readonly HashSet<string> Formats=["CODE128","EAN13","EAN8","UPC","CODE39"];public BarcodeValidator(){RuleFor(x=>x.Input.Value).NotEmpty().MaximumLength(200);RuleFor(x=>x.Input.Format).Must(x=>Formats.Contains(x.ToUpperInvariant()));RuleFor(x=>x.Input.Width).InclusiveBetween(80,2000);RuleFor(x=>x.Input.Height).InclusiveBetween(30,1000);}}
internal sealed class QRValidator:AbstractValidator<GenerateQRCode>{public QRValidator(){RuleFor(x=>x.Input.Value).NotEmpty().MaximumLength(2000);RuleFor(x=>x.Input.PixelsPerModule).InclusiveBetween(2,30);}}
internal sealed class DocumentValidator:AbstractValidator<GenerateDocument>{public DocumentValidator(){RuleFor(x=>x.Input.DocumentType).NotEmpty();RuleFor(x=>x.Input.DocumentNumber).NotEmpty();RuleFor(x=>x.Input.BodyHtml).NotEmpty();RuleFor(x=>x.Input.Output).Must(x=>x is "pdf" or "html");}}
internal sealed class LabelValidator:AbstractValidator<GenerateLabel>{public LabelValidator(){RuleFor(x=>x.Input.ProductName).NotEmpty();RuleFor(x=>x.Input.WidthMm).GreaterThan(0);RuleFor(x=>x.Input.HeightMm).GreaterThan(0);RuleFor(x=>x.Input.Quantity).InclusiveBetween(1,500);}}
internal sealed class GetPrintTemplatesHandler(IPrintRepository repository):IRequestHandler<GetPrintTemplates,IReadOnlyCollection<PrintTemplateDto>>{public Task<IReadOnlyCollection<PrintTemplateDto>>Handle(GetPrintTemplates request,CancellationToken token)=>repository.Templates(request.DocumentType,token);}
internal sealed class GetPrintersHandler(IPrintRepository repository):IRequestHandler<GetPrinters,IReadOnlyCollection<PrinterDto>>{public Task<IReadOnlyCollection<PrinterDto>>Handle(GetPrinters request,CancellationToken token)=>repository.Printers(token);}
internal sealed class SavePrinterHandler(IPrintRepository repository):IRequestHandler<SavePrinter>{public Task Handle(SavePrinter request,CancellationToken token)=>repository.SavePrinter(request.Input,token);}
internal sealed class GenerateBarcodeHandler(IPrintingService service):IRequestHandler<GenerateBarcode,PrintArtifact>{public Task<PrintArtifact>Handle(GenerateBarcode request,CancellationToken token)=>Task.FromResult(service.Barcode(request.Input));}
internal sealed class GenerateQRCodeHandler(IPrintingService service):IRequestHandler<GenerateQRCode,PrintArtifact>{public Task<PrintArtifact>Handle(GenerateQRCode request,CancellationToken token)=>Task.FromResult(service.QrCode(request.Input));}
internal sealed class GenerateDocumentHandler(IPrintingService service):IRequestHandler<GenerateDocument,PrintArtifact>{public Task<PrintArtifact>Handle(GenerateDocument request,CancellationToken token)=>Task.FromResult(service.Document(request.Input));}
internal sealed class GenerateLabelHandler(IPrintingService service):IRequestHandler<GenerateLabel,PrintArtifact>{public Task<PrintArtifact>Handle(GenerateLabel request,CancellationToken token)=>Task.FromResult(service.Label(request.Input));}
