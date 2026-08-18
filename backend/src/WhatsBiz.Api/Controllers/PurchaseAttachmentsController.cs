using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Purchases;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/purchases/{purchaseId:guid}/attachments")]
public sealed class PurchaseAttachmentsController(ISender sender) : ControllerBase
{
    [HttpPost, HasPermission(Permissions.Purchase.Create), RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<PurchaseAttachmentDto> Upload(Guid purchaseId, IFormFile file, CancellationToken token)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, token);
        return await sender.Send(new UploadPurchaseAttachment(purchaseId, file.FileName, file.ContentType, stream.ToArray()), token);
    }

    [HttpGet("{attachmentId:guid}"), HasPermission(Permissions.Purchase.View)]
    public async Task<IActionResult> Download(Guid purchaseId, Guid attachmentId, CancellationToken token)
    {
        var file = await sender.Send(new GetPurchaseAttachment(purchaseId, attachmentId), token);
        return file is null ? NotFound() : File(file.Data, file.ContentType, file.FileName);
    }

    [HttpDelete("{attachmentId:guid}"), HasPermission(Permissions.Purchase.Edit)]
    public async Task<IActionResult> Delete(Guid purchaseId, Guid attachmentId, CancellationToken token)
    {
        await sender.Send(new DeletePurchaseAttachment(purchaseId, attachmentId), token);
        return NoContent();
    }
}
