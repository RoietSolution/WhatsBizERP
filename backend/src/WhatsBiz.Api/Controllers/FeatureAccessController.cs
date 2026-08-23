using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/features")]
public sealed class FeatureAccessController(IFeatureService features, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("effective")]
    public Task<TenantFeatureConfiguration> Effective(CancellationToken token)
        => features.GetTenantConfigurationAsync(CurrentTenant(), token);

    [HttpGet("administration/tenants"), HasPermission(Permissions.Features.Manage)]
    public Task<IReadOnlyCollection<FeatureTenantSummary>> Tenants(CancellationToken token) => features.GetTenantsAsync(token);

    [HttpGet("administration/tenants/{tenantId:guid}"), HasPermission(Permissions.Features.Manage)]
    public Task<TenantFeatureConfiguration> Tenant(Guid tenantId, CancellationToken token)
        => features.GetTenantConfigurationAsync(tenantId, token);

    [HttpPut("administration/tenants/{tenantId:guid}"), HasPermission(Permissions.Features.Manage)]
    public async Task<ActionResult<TenantFeatureConfiguration>> Update(Guid tenantId, IReadOnlyCollection<TenantFeatureUpdate> updates, CancellationToken token)
    {
        try { return Ok(await features.UpdateTenantConfigurationAsync(tenantId, updates, currentUser.Username, token)); }
        catch (ArgumentException ex) { return BadRequest(Problem(ex.Message, statusCode: 400, title: "Invalid feature configuration")); }
        catch (InvalidOperationException ex) { return Conflict(Problem(ex.Message, statusCode: 409, title: "Subscription does not allow feature")); }
    }

    private Guid CurrentTenant() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
}
