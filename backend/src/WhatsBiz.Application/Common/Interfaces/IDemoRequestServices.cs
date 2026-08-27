using WhatsBiz.Application.Features.DemoRequests;

namespace WhatsBiz.Application.Common.Interfaces;

public sealed record DemoRequestCreateResult(long Id, string ReferenceNo, bool Duplicate);

public interface IDemoRequestRepository
{
    Task<DemoRequestCreateResult> CreateAsync(DemoRequestInput input, string source, string? ipAddress, string? userAgent, CancellationToken token);
    Task<PagedDemoRequests> SearchAsync(string? search, string? status, DateTimeOffset? fromDate, DateTimeOffset? toDate, int pageNumber, int pageSize, CancellationToken token);
    Task<DemoRequestDetail> GetAsync(long id, CancellationToken token);
    Task<DemoRequestDetail> UpdateStatusAsync(long id, string status, string? user, CancellationToken token);
    Task SetNotificationStatusAsync(long id, string status, CancellationToken token);
}

public interface IDemoRequestNotificationService
{
    Task<string> NotifyAsync(DemoRequestDetail request, CancellationToken token);
}

public interface IDemoRequestCaptchaVerifier
{
    Task<bool> VerifyAsync(string? tokenValue, string? ipAddress, CancellationToken token);
}
