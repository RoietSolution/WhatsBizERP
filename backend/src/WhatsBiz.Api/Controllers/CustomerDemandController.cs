using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/customer-demand"), RequireFeature(FeatureKeys.CustomerDemand)]
public sealed class CustomerDemandController(ICustomerDemandService demand, ICustomerDemandManagementService management, ICurrentUserService current) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Dashboard.Analytics)]
    public Task<CustomerDemandSummaryDto> Get([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken token)
    { var end=to?.ToUniversalTime()??DateTimeOffset.UtcNow; var start=from?.ToUniversalTime()??end.AddDays(-30); return demand.SummaryAsync(current.TenantId??throw new UnauthorizedAccessException("A tenant context is required."),start,end,token); }

    [HttpGet("search-analytics"), HasPermission(Permissions.Dashboard.Analytics)]
    public Task<CustomerDemandSummaryDto> SearchAnalytics([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken token)
    { var end=to?.ToUniversalTime()??DateTimeOffset.UtcNow; var start=from?.ToUniversalTime()??end.AddDays(-30); return demand.SummaryAsync(current.TenantId??throw new UnauthorizedAccessException("A tenant context is required."),start,end,token); }

    [HttpPatch("missing/{requestId:guid}"), HasPermission(Permissions.Dashboard.Analytics)]
    public async Task<IActionResult> UpdateMissingStatus(Guid requestId, UpdateMissingProductRequestStatusInput input, CancellationToken token)
    {
        var tenant = current.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
        return await management.UpdateMissingProductStatusAsync(tenant, requestId, input.Status, token) ? NoContent() : NotFound();
    }
}
