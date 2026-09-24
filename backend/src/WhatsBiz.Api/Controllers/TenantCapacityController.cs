using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Capacity;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize]
public sealed class TenantCapacityController(ITenantResourceLimitService limits, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("api/capacity")]
    public Task<TenantCapacitySummary> Own(CancellationToken token)
        => limits.GetTenantCapacitySummaryAsync(currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), token);

    [HttpGet("api/system/tenants/{tenantId:guid}/capacity"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public Task<TenantCapacitySummary> Tenant(Guid tenantId, CancellationToken token) => limits.GetTenantCapacitySummaryAsync(tenantId, token);

    [HttpPut("api/system/tenants/{tenantId:guid}/capacity"), PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    public async Task<ActionResult<TenantCapacitySummary>> Update(Guid tenantId, UpdateTenantCapacityInput input, CancellationToken token)
    {
        try { return Ok(await limits.UpdateTenantCapacityAsync(tenantId,input,currentUser.Username,currentUser.UserId,token)); }
        catch(ArgumentException exception){ return BadRequest(Problem(exception.Message,statusCode:400,title:"Invalid capacity configuration")); }
    }
}
