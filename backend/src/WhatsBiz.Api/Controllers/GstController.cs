using MediatR;
using Microsoft.AspNetCore.Mvc;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Gst;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/gst")]
public sealed class GstController(ISender sender) : ControllerBase
{
    private static GstFilter Filter(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, Guid? partyId, decimal? gstRate)
    {
        var now = DateTimeOffset.Now;
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        return new(from ?? month, to ?? month.AddMonths(1), branchId, partyId, gstRate);
    }

    private async Task<IActionResult> GetReport(string report, DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, Guid? partyId, decimal? gstRate, CancellationToken token) =>
        Ok(await sender.Send(new GetGstReport(report, Filter(from, to, branchId, partyId, gstRate)), token));

    [HttpGet("sales-register"), HasPermission(Permissions.Gst.View)]
    public Task<IActionResult> Sales(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, Guid? customerId, decimal? gstRate, CancellationToken token) => GetReport("sales-register", from, to, branchId, customerId, gstRate, token);

    [HttpGet("purchase-register"), HasPermission(Permissions.Gst.View)]
    public Task<IActionResult> Purchase(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, Guid? supplierId, decimal? gstRate, CancellationToken token) => GetReport("purchase-register", from, to, branchId, supplierId, gstRate, token);

    [HttpGet("hsn-summary"), HasPermission(Permissions.Gst.View)]
    public Task<IActionResult> Hsn(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, decimal? gstRate, CancellationToken token) => GetReport("hsn-summary", from, to, branchId, null, gstRate, token);

    [HttpGet("gstr1"), HasPermission(Permissions.Gst.View)]
    public Task<IActionResult> Gstr1(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, CancellationToken token) => GetReport("gstr1", from, to, branchId, null, null, token);

    [HttpGet("gstr3b"), HasPermission(Permissions.Gst.View)]
    public Task<IActionResult> Gstr3b(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, CancellationToken token) => GetReport("gstr3b", from, to, branchId, null, null, token);

    [HttpGet("tax-summary"), HasPermission(Permissions.Gst.View)]
    public Task<IActionResult> Tax(DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, decimal? gstRate, CancellationToken token) => GetReport("tax-summary", from, to, branchId, null, gstRate, token);

    [HttpGet("export/{report}"), HasPermission(Permissions.Gst.Export)]
    public async Task<IActionResult> Export(string report, string format, DateTimeOffset? from, DateTimeOffset? to, Guid? branchId, Guid? partyId, decimal? gstRate, CancellationToken token)
    {
        var file = await sender.Send(new ExportGstReport(report, format, Filter(from, to, branchId, partyId, gstRate)), token);
        return File(file.Data, file.ContentType, file.FileName);
    }

    [HttpGet("configuration"), HasPermission(Permissions.Gst.Configuration)]
    public Task<GstSettingsDto> Configuration(CancellationToken token) => sender.Send(new GetGstSettings(), token);

    [HttpPut("configuration"), HasPermission(Permissions.Gst.Configuration)]
    public Task<GstSettingsDto> Configuration(GstSettingsInput input, CancellationToken token) => sender.Send(new SaveGstSettings(input), token);
}
