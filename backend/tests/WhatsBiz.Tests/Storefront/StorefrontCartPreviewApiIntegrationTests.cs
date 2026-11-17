using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Tests.Integration;

namespace WhatsBiz.Tests.Storefront;

[Collection("SQL storefront preview")]
public sealed class StorefrontCartPreviewApiIntegrationTests
{
    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task GuestPreviewWithNoPincodeReturnsBackendPricingWithoutCustomerSession()
    {
        // Preview must work on a normal connection without concurrent readers.
        var connectionString = new SqlConnectionStringBuilder(SqlIntegrationDatabase.ConnectionString)
        {
            MultipleActiveResultSets = false
        }.ConnectionString;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);

        var fixture = await ReadFixtureAsync(connection);
        await using var factory = new StorefrontPreviewFactory(connectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/store/{Uri.EscapeDataString(fixture.StoreKey)}/cart/preview")
        {
            Content = JsonContent.Create(new
            {
                pincode = (string?)null,
                promoCode = (string?)null,
                items = new[] { new { productId = fixture.ProductId, quantity = 1 } }
            })
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PreviewBody>();
        body.Should().NotBeNull();
        body!.MerchandiseSubtotal.Should().BeGreaterThanOrEqualTo(0);
        body.FinalPayableAmount.Should().BeGreaterThanOrEqualTo(0);
    }

    private static async Task<(string StoreKey, Guid ProductId)> ReadFixtureAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("""
            SELECT TOP (1) t.TenantKey, p.ProductId
            FROM core.Tenants t
            JOIN master.Products p ON p.TenantId=t.TenantId
            WHERE t.IsActive=1 AND p.IsActive=1 AND p.IsDeleted=0
            ORDER BY t.TenantKey, p.ProductName;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("The disposable integration database has no active tenant/product fixture for the guest cart preview test.");
        return (reader.GetString(0), reader.GetGuid(1));
    }

    private sealed record PreviewBody(decimal MerchandiseSubtotal, decimal FinalPayableAmount);

    private sealed class StorefrontPreviewFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:SigningKey"] = "storefront-preview-integration-test-key-32-characters-minimum",
                ["ProductImageStorage:Provider"] = "DATABASE"
            }));
    }
}
