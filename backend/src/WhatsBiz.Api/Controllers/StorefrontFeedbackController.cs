using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Exceptions;
using AppUnauthorizedAccessException = WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/storefront-feedback"), RequireFeature(FeatureKeys.StorefrontFeedback)]
public sealed class StorefrontFeedbackController(IStorefrontFeedbackService feedback, ICurrentUserService current) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<StorefrontFeedbackPageDto> Search([FromQuery] string? search, [FromQuery] string? feedbackType, [FromQuery] string? status, [FromQuery] string? priority, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
        => feedback.SearchAsync(Tenant(), search, feedbackType, status, priority, pageNumber, pageSize, token);
    [HttpGet("{ticketId:guid}"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<StorefrontFeedbackTicketDetailDto?> Get(Guid ticketId, CancellationToken token) => feedback.GetAsync(Tenant(), ticketId, true, token);
    [HttpPatch("{ticketId:guid}"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<StorefrontFeedbackTicketDetailDto> Update(Guid ticketId, UpdateStorefrontFeedbackInput input, CancellationToken token) => feedback.UpdateAsync(Tenant(), current.UserId ?? throw new AppUnauthorizedAccessException("A user identity is required."), ticketId, input, token);
    [HttpGet("notifications"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<IReadOnlyCollection<StorefrontFeedbackNotificationDto>> Notifications(CancellationToken token) => feedback.NotificationsAsync(Tenant(), null, token);
    [HttpPost("notifications/{notificationId:guid}/read"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<bool> MarkRead(Guid notificationId, CancellationToken token) => feedback.MarkNotificationReadAsync(Tenant(), notificationId, null, token);
    [HttpPost("notifications/read-all"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<int> MarkAllRead(CancellationToken token) => feedback.MarkAllNotificationsReadAsync(Tenant(), null, token);
    [HttpGet("{ticketId:guid}/attachments"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<IReadOnlyCollection<StorefrontFeedbackAttachmentDto>> Attachments(Guid ticketId, CancellationToken token) => feedback.AttachmentsAsync(Tenant(), ticketId, null, true, token);
    [HttpGet("{ticketId:guid}/attachments/{attachmentId:guid}") , HasPermission(Permissions.Customer.FeedbackManage)]
    public async Task<IActionResult> Attachment(Guid ticketId, Guid attachmentId, CancellationToken token) { var file=await feedback.ReadAttachmentAsync(Tenant(),ticketId,attachmentId,null,true,token); return file is null?NotFound():File(file.Content,file.ContentType,file.FileName,enableRangeProcessing:false); }
    [HttpPost("{ticketId:guid}/attachments"), HasPermission(Permissions.Customer.FeedbackManage), RequestSizeLimit(16 * 1024 * 1024), Consumes("multipart/form-data")]
    public async Task<ActionResult<StorefrontFeedbackAttachmentDto>> AddAttachment(Guid ticketId, IFormFile file, CancellationToken token) { if(file is null)return BadRequest(new{message="Choose an attachment."}); await using var stream=file.OpenReadStream(); using var memory=new MemoryStream(); await stream.CopyToAsync(memory,token); try{return Ok(await feedback.AddAttachmentAsync(Tenant(),ticketId,null,true,file.FileName,file.ContentType,memory.ToArray(),token));}catch(BusinessRuleException ex){return BadRequest(new{message=ex.Message});} }
    private Guid Tenant() => current.TenantId ?? throw new AppUnauthorizedAccessException("A tenant context is required.");
}
