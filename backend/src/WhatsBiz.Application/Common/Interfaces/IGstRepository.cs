using WhatsBiz.Application.Features.Gst;
namespace WhatsBiz.Application.Common.Interfaces;
public interface IGstRepository { Task<IReadOnlyCollection<GstReportRowDto>> Report(string report, GstFilter filter, CancellationToken token); Task<GstSettingsDto> Settings(CancellationToken token); Task SaveSettings(GstSettingsInput input, string? user, CancellationToken token); }
public interface IGstExportService { byte[] Export(IReadOnlyCollection<GstReportRowDto> rows, string report, string format); string ContentType(string format); }
