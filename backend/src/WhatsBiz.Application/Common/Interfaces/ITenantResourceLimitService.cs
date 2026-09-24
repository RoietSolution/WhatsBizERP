using WhatsBiz.Application.Common.Capacity;

namespace WhatsBiz.Application.Common.Interfaces;

public interface ITenantResourceLimitService
{
    Task<TenantCapacitySummary> GetTenantCapacitySummaryAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantCapacitySummary> UpdateTenantCapacityAsync(Guid tenantId, UpdateTenantCapacityInput input, string? changedBy, Guid? changedByUserId, CancellationToken cancellationToken = default);
    Task<TenantResourceCapacity> GetCurrentUsageAsync(Guid tenantId, string resourceType, CancellationToken cancellationToken = default);
    Task<bool> CanCreateAsync(Guid tenantId, string resourceType, CancellationToken cancellationToken = default);
    Task EnsureCanCreateAsync(Guid tenantId, string resourceType, CancellationToken cancellationToken = default);
}
