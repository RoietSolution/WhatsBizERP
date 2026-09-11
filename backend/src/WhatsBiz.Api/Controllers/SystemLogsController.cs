using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Logging;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/admin/system-logs")]
public sealed class SystemLogsController(SystemLogReader logs) : ControllerBase
{
    [HttpGet, PlatformAuthorize]
    public async Task<ActionResult<PagedSystemLogs>> Search(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? level,
        string? requestPath,
        int? statusCode,
        string? traceId,
        string? search,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var end = to ?? DateTimeOffset.Now.Date.AddDays(1);
        var start = from ?? end.AddDays(-1);
        if (start >= end) return BadRequest(new ProblemDetails { Title = "Invalid date range", Detail = "The start date must be earlier than the end date." });
        if (end - start > TimeSpan.FromDays(31)) return BadRequest(new ProblemDetails { Title = "Invalid date range", Detail = "The log date range cannot exceed 31 days." });
        return await logs.SearchAsync(start, end, level, requestPath, statusCode, traceId, search,
            Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, 200), cancellationToken);
    }
}
