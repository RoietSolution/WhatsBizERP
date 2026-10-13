using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/storefront-feedback"), RequireFeature(FeatureKeys.StorefrontFeedback)]
public sealed class StorefrontFeedbackController(IStorefrontFeedbackService feedback, ICurrentUserService current) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<StorefrontFeedbackPageDto> Search([FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? priority, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
        => feedback.SearchAsync(Tenant(), search, status, priority, pageNumber, pageSize, token);
    [HttpGet("{ticketId:guid}"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<StorefrontFeedbackTicketDetailDto?> Get(Guid ticketId, CancellationToken token) => feedback.GetAsync(Tenant(), ticketId, true, token);
    [HttpPatch("{ticketId:guid}"), HasPermission(Permissions.Customer.FeedbackManage)]
    public Task<StorefrontFeedbackTicketDetailDto> Update(Guid ticketId, UpdateStorefrontFeedbackInput input, CancellationToken token) => feedback.UpdateAsync(Tenant(), current.UserId ?? throw new UnauthorizedAccessException("A user identity is required."), ticketId, input, token);
    private Guid Tenant() => current.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
}
