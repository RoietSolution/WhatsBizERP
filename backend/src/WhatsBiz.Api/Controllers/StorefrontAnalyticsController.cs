using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/storefront-analytics")]
public sealed class StorefrontAnalyticsController(IStorefrontActivityService activity, ICurrentUserService currentUser, ILogger<StorefrontAnalyticsController> logger) : ControllerBase
{
    private static readonly Action<ILogger, Guid, DateTimeOffset, DateTimeOffset, Exception?> ReportFailure = LoggerMessage.Define<Guid, DateTimeOffset, DateTimeOffset>(LogLevel.Error, new EventId(6102, nameof(ReportFailure)), "Storefront activity report failed for tenant {TenantId} between {From} and {To}.");
    [HttpGet]
    [HasPermission(Permissions.Dashboard.Analytics)]
    public async Task<ActionResult<StorefrontActivityReportDto>> Get([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken token)
    {
        var end = to?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var start = from?.ToUniversalTime() ?? end.AddDays(-7);
        if (end <= start || end - start > TimeSpan.FromDays(366))
            return BadRequest(new { message = "Choose a valid activity date range of no more than 366 days." });

        var tenantId = currentUser.TenantId;
        if (tenantId is null) return Unauthorized(new { message = "A tenant context is required." });
        try
        {
            return Ok(await activity.ReportAsync(tenantId.Value, start, end, token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            ReportFailure(logger, tenantId.Value, start, end, exception);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Storefront activity is temporarily unavailable.", detail: "Try again shortly.");
        }
    }

    [HttpGet("behavior")]
    [HasPermission(Permissions.Dashboard.Analytics)]
    public async Task<ActionResult<StorefrontBehaviorReportDto>> Behavior([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken token)
    {
        var end = to?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var start = from?.ToUniversalTime() ?? end.AddDays(-7);
        if (end <= start || end - start > TimeSpan.FromDays(366)) return BadRequest(new { message = "Choose a valid activity date range of no more than 366 days." });
        var tenantId = currentUser.TenantId;
        if (tenantId is null) return Unauthorized(new { message = "A tenant context is required." });
        try { return Ok(await activity.BehaviorAsync(tenantId.Value, start, end, token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { ReportFailure(logger, tenantId.Value, start, end, exception); return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Storefront activity is temporarily unavailable.", detail: "Try again shortly."); }
    }
}
