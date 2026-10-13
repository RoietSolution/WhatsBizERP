using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Storefront;
using WhatsBiz.Tests.Integration;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontActivitySqlIntegrationTests
{
    [Fact(Skip = "Requires an explicitly configured approved disposable database via ConnectionStrings__IntegrationTests.", Timeout = 30_000)]
    public async Task EmptyPeriodReturnsZeroCountersInsteadOfNullMappingFailure()
    {
        var raw = Environment.GetEnvironmentVariable("ConnectionStrings__IntegrationTests");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("The disposable integration database is not configured.");

        string connectionString;
        try { connectionString = SqlIntegrationDatabase.Validate(raw); }
        catch (InvalidOperationException exception) { throw new InvalidOperationException($"The disposable integration database is not configured: {exception.Message}", exception); }
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
            .Build();
        var service = new StorefrontActivityService(configuration, null!, NullLogger<StorefrontActivityService>.Instance);
        var report = await service.ReportAsync(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, CancellationToken.None);

        report.Summary.ProductViews.Should().Be(0);
        report.Summary.AddToCarts.Should().Be(0);
        report.Summary.Checkouts.Should().Be(0);
        report.Summary.Orders.Should().Be(0);
    }

    [Fact(Skip = "Requires an explicitly configured approved disposable database via ConnectionStrings__IntegrationTests.", Timeout = 30_000)]
    public async Task ActivityRecordUsesTheAnalyticsEventDefaultAndPersistsAVisitorEvent()
    {
        var raw = Environment.GetEnvironmentVariable("ConnectionStrings__IntegrationTests");
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("The disposable integration database is not configured.");
        var connectionString = SqlIntegrationDatabase.Validate(raw);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);

        Guid tenantId;
        string storeKey;
        await using (var tenantCommand = new SqlCommand("SELECT TOP (1) TenantId, TenantKey FROM core.Tenants WHERE IsActive=1 ORDER BY TenantId;", connection))
        await using (var reader = await tenantCommand.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new InvalidOperationException("The disposable database has no active tenant fixture.");
            tenantId = reader.GetGuid(0);
            storeKey = reader.GetString(1);
        }

        var visitorSessionId = "integration-visitor-" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connectionString })
            .Build();
        var service = new StorefrontActivityService(configuration, null!, NullLogger<StorefrontActivityService>.Instance);
        try
        {
            await service.RecordAsync(storeKey, null, new StorefrontActivityInput(visitorSessionId, "STORE_VISIT"), CancellationToken.None);

            await using var countCommand = new SqlCommand("SELECT COUNT(*) FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND VisitorSessionId=@visitor AND EventType=N'STORE_VISIT';", connection);
            countCommand.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
            countCommand.Parameters.Add("@visitor", System.Data.SqlDbType.VarChar, 64).Value = visitorSessionId;
            ((int?)await countCommand.ExecuteScalarAsync()).Should().Be(1);
        }
        finally
        {
            await using var deleteCommand = new SqlCommand("DELETE FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND VisitorSessionId=@visitor;", connection);
            deleteCommand.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
            deleteCommand.Parameters.Add("@visitor", System.Data.SqlDbType.VarChar, 64).Value = visitorSessionId;
            await deleteCommand.ExecuteNonQueryAsync();
        }
    }
}
