using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class CustomerDemandManagementService(IConfiguration configuration) : ICustomerDemandManagementService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");
    public async Task<bool> UpdateMissingProductStatusAsync(Guid tenantId, Guid requestId, string status, CancellationToken token)
    {
        var normalized = status.Trim().ToUpperInvariant();
        if (normalized is not ("NEW" or "REVIEWING" or "SOURCING" or "AVAILABLE" or "CLOSED")) throw new BusinessRuleException("The missing-product status is invalid.");
        await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        string? current;
        await using (var read = new SqlCommand("SELECT RequestStatus FROM commerce.MissingProductRequests WHERE TenantId=@tenant AND MissingProductRequestId=@id", c))
        { read.Parameters.AddWithValue("@tenant", tenantId); read.Parameters.AddWithValue("@id", requestId); current = await read.ExecuteScalarAsync(token) as string; }
        if (current is null) return false;
        var allowed = current switch { "NEW" => normalized is "NEW" or "REVIEWING" or "CLOSED", "REVIEWING" => normalized is "REVIEWING" or "SOURCING" or "CLOSED", "SOURCING" => normalized is "SOURCING" or "AVAILABLE" or "CLOSED", "AVAILABLE" => normalized is "AVAILABLE" or "CLOSED", "CLOSED" => normalized == "CLOSED", _ => false };
        if (!allowed) throw new BusinessRuleException($"A missing-product request cannot move from {current} to {normalized}.");
        await using var q = new SqlCommand("UPDATE commerce.MissingProductRequests SET RequestStatus=@status,UpdatedAtUtc=SYSUTCDATETIME() WHERE TenantId=@tenant AND MissingProductRequestId=@id AND RequestStatus=@current", c);
        q.Parameters.AddWithValue("@status", normalized); q.Parameters.AddWithValue("@tenant", tenantId); q.Parameters.AddWithValue("@id", requestId); q.Parameters.AddWithValue("@current", current);
        return await q.ExecuteNonQueryAsync(token) == 1;
    }
}
