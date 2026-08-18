using FluentValidation;
using MediatR;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Purchases;

namespace WhatsBiz.Application.Features.Purchases;

public sealed record PurchaseAttachmentDto(Guid Id, string FileName, string ContentType, DateTimeOffset UploadedOn);
public sealed record PurchaseAttachmentFile(string FileName, string ContentType, byte[] Data);
public sealed record UploadPurchaseAttachment(Guid PurchaseId, string FileName, string ContentType, byte[] Data) : IRequest<PurchaseAttachmentDto>;
public sealed record GetPurchaseAttachment(Guid PurchaseId, Guid AttachmentId) : IRequest<PurchaseAttachmentFile?>;
public sealed record DeletePurchaseAttachment(Guid PurchaseId, Guid AttachmentId) : IRequest;

public sealed class UploadPurchaseAttachmentValidator : AbstractValidator<UploadPurchaseAttachment>
{
    public UploadPurchaseAttachmentValidator()
    {
        RuleFor(x => x.PurchaseId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Data).NotEmpty().Must(x => x.Length <= 10 * 1024 * 1024)
            .WithMessage("Attachment cannot exceed 10 MB.");
    }
}

public sealed class PurchaseAttachmentHandlers(IPurchaseRepository repository, ICurrentUserService user) :
    IRequestHandler<UploadPurchaseAttachment, PurchaseAttachmentDto>,
    IRequestHandler<GetPurchaseAttachment, PurchaseAttachmentFile?>,
    IRequestHandler<DeletePurchaseAttachment>
{
    public async Task<PurchaseAttachmentDto> Handle(UploadPurchaseAttachment request, CancellationToken cancellationToken)
    {
        if (!await repository.PurchaseExists(request.PurchaseId, cancellationToken))
            throw new EntityNotFoundException("Purchase not found.");
        var attachment = new PurchaseAttachment
        {
            PurchaseInvoiceId = request.PurchaseId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            FileData = request.Data,
            UploadedBy = user.Username
        };
        repository.AddAttachment(attachment);
        await repository.SaveAttachments(cancellationToken);
        return new(attachment.PurchaseAttachmentId, attachment.FileName, attachment.ContentType, attachment.UploadedOn);
    }

    public async Task<PurchaseAttachmentFile?> Handle(GetPurchaseAttachment request, CancellationToken cancellationToken)
    {
        var attachment = await repository.GetAttachment(request.PurchaseId, request.AttachmentId, false, cancellationToken);
        return attachment is null ? null : new(attachment.FileName, attachment.ContentType, attachment.FileData);
    }

    public async Task Handle(DeletePurchaseAttachment request, CancellationToken cancellationToken)
    {
        var attachment = await repository.GetAttachment(request.PurchaseId, request.AttachmentId, true, cancellationToken)
            ?? throw new EntityNotFoundException("Attachment not found.");
        attachment.IsDeleted = true;
        await repository.SaveAttachments(cancellationToken);
    }
}
