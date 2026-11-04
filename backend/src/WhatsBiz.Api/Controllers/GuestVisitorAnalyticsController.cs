using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/store/{storeKey}/visitor-analytics"), EnableRateLimiting("GuestAnalytics")]
public sealed class GuestVisitorAnalyticsController(IGuestVisitorAnalyticsService analytics, IHttpContextAccessor context) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<GuestAnalyticsStatus>> Status(string storeKey, CancellationToken token)
        => Ok(await analytics.StatusAsync(storeKey, token));

    [HttpPost("consent"), RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Consent(string storeKey, GuestAnalyticsConsentInput input, CancellationToken token)
    {
        try { await analytics.SetConsentAsync(storeKey, input, context.HttpContext?.Request.Headers.UserAgent.ToString(), token); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(StatusCodes.Status403Forbidden,new { message=ex.Message }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("events"), RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Events(string storeKey, GuestAnalyticsBatchInput input, CancellationToken token)
    {
        try { await analytics.RecordAsync(storeKey,input,token); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { message=ex.Message }); }
        catch (InvalidOperationException) { return StatusCode(StatusCodes.Status403Forbidden,new { message="Analytics consent is unavailable or withdrawn." }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpDelete("consent")]
    public async Task<IActionResult> Withdraw(string storeKey,[FromQuery] Guid visitorId,[FromQuery] Guid sessionId,CancellationToken token)
    { await analytics.WithdrawAsync(storeKey,visitorId,sessionId,token); return NoContent(); }
}

[ApiController, RequireFeature(FeatureKeys.GuestVisitorAnalytics), HasPermission(Permissions.Dashboard.Analytics), Route("api/visitor-analytics")]
public sealed class RetailerGuestVisitorAnalyticsController(IGuestVisitorAnalyticsService analytics, ICurrentUserService current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Report([FromQuery] DateTimeOffset from,[FromQuery] DateTimeOffset to,CancellationToken token)
    {
        if(current.TenantId is not Guid tenant)return Unauthorized();
        try{return Ok(await analytics.RetailerReportAsync(tenant,from,to,token));}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}
    }
}

[ApiController, PlatformAuthorize, HasPermission(Permissions.Features.Manage), Route("api/visitor-analytics/owner")]
public sealed class OwnerGuestVisitorAnalyticsController(IGuestVisitorAnalyticsService analytics) : ControllerBase
{
    [HttpGet("tenants")]
    public async Task<IActionResult> Tenants(CancellationToken token)=>Ok(await analytics.OwnerFiltersAsync(token));

    [HttpGet]
    public async Task<IActionResult> Report([FromQuery] DateTimeOffset from,[FromQuery] DateTimeOffset to,[FromQuery] Guid? tenantId,CancellationToken token)
    {try{return Ok(await analytics.OwnerReportAsync(tenantId,from,to,token));}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}}

    [HttpPut("policy/{tenantId:guid}")]
    public async Task<IActionResult> SetPolicy(Guid tenantId,GuestAnalyticsRetentionInput input,CancellationToken token)
    {try{await analytics.SetRetentionPolicyAsync(tenantId,input,token);return NoContent();}catch(ArgumentException ex){return BadRequest(new{message=ex.Message});}}
}
