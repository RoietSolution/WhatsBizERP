using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Authorize, Route("api/features")]
public sealed class FeatureAccessController(IFeatureService features, ITenantEnrollmentService enrollment, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("effective")]
    public Task<TenantFeatureConfiguration> Effective(CancellationToken token)
        => features.GetTenantConfigurationAsync(CurrentTenant(), token);

    [HttpGet("administration/tenants"), HasPermission(Permissions.Features.Manage)]
    public Task<IReadOnlyCollection<FeatureTenantSummary>> Tenants(CancellationToken token) => features.GetTenantsAsync(token);

    [HttpGet("administration/tenant-enrollment-template"), HasPermission(Permissions.Features.Manage)]
    public async Task<IActionResult> EnrollmentTemplate(CancellationToken token) => File(
        await enrollment.CreateTemplateAsync(token),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "tenant-enrollment-template.xlsx");

    [HttpPost("administration/tenant-enrollment"), HasPermission(Permissions.Features.Manage), RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<TenantEnrollmentResult> EnrollTenant(IFormFile file, CancellationToken token)
    {
        if (file.Length == 0) throw new ArgumentException("Select a completed tenant enrollment workbook.");
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, token);
        return await enrollment.ImportAsync(stream.ToArray(), currentUser.Username, token);
    }

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
