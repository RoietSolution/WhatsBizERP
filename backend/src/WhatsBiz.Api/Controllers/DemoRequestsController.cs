using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.DemoRequests;
using WhatsBiz.Infrastructure.DemoRequests;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController]
[Route("api/demo-requests")]
public sealed class DemoRequestsController(ISender sender, IOptions<DemoRequestOptions> options) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("DemoRequests")]
    [HttpPost]
    [ProducesResponseType<DemoRequestSubmissionResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<DemoRequestSubmissionResult>> Submit(DemoRequestInput input, CancellationToken token)
    {
        var ipAddress = Limit(HttpContext.Connection.RemoteIpAddress?.ToString(), 64);
        var userAgent = Limit(Request.Headers.UserAgent.ToString(), 512);
        var result = await sender.Send(new SubmitDemoRequest(input, ipAddress, userAgent), token);
        return result.Duplicate ? Ok(result) : CreatedAtAction(nameof(Get), new { id = result.LeadId }, result);
    }

    [AllowAnonymous]
    [HttpGet("configuration")]
    public ActionResult<DemoRequestConfiguration> Configuration()
    {
        var value = options.Value;
        return Ok(new DemoRequestConfiguration(value.WhatsAppContactNumber, value.Captcha.Enabled, value.Captcha.Enabled ? value.Captcha.SiteKey : null));
    }

    [HttpGet]
    [HasPermission(Permissions.Admin.View)]
    public Task<PagedDemoRequests> Search([FromQuery] string? search, [FromQuery] string? status, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 25, CancellationToken token = default) =>
        sender.Send(new SearchDemoRequests(search, status, from, to, pageNumber, pageSize), token);

    [HttpGet("{id:long}")]
    [HasPermission(Permissions.Admin.View)]
    public Task<DemoRequestDetail> Get(long id, CancellationToken token) => sender.Send(new GetDemoRequest(id), token);

    [HttpPatch("{id:long}/status")]
    [HasPermission(Permissions.Admin.Settings)]
    public Task<DemoRequestDetail> UpdateStatus(long id, UpdateDemoRequestStatusInput input, CancellationToken token) =>
        sender.Send(new UpdateDemoRequestStatus(id, input.Status, User.Identity?.Name), token);

    private static string? Limit(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, length)];
}

public sealed record UpdateDemoRequestStatusInput(string Status);
