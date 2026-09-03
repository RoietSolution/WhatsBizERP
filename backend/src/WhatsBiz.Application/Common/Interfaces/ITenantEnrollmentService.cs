using WhatsBiz.Application.Common.Features;

namespace WhatsBiz.Application.Common.Interfaces;

public interface ITenantEnrollmentService
{
    Task<byte[]> CreateTemplateAsync(CancellationToken cancellationToken = default);
    Task<TenantEnrollmentResult> ImportAsync(byte[] workbook, string? actor, CancellationToken cancellationToken = default);
}
