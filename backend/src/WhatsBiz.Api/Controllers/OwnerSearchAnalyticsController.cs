using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Infrastructure.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, PlatformAuthorize, HasPermission(Permissions.Features.Manage), Route("api/customer-demand/owner")]
public sealed class OwnerSearchAnalyticsController(OwnerSearchAnalyticsService analytics) : ControllerBase
{
    [HttpGet("search-analytics/filters")]
    public Task<OwnerSearchAnalyticsFilters> Filters(CancellationToken token) => analytics.FiltersAsync(token);

    [HttpGet("search-analytics")]
    public async Task<IActionResult> Search([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] Guid? cityId, [FromQuery] Guid? retailerTenantId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken token = default) =>
        await SearchCore(from, to, cityId, retailerTenantId, page, pageSize, token);

    private async Task<IActionResult> SearchCore(DateTimeOffset from, DateTimeOffset to, Guid? cityId,
        Guid? retailerTenantId, int page, int pageSize, CancellationToken token)
    {
        try { return Ok(await analytics.SearchAsync(from, to, cityId, retailerTenantId, page, pageSize, token)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
