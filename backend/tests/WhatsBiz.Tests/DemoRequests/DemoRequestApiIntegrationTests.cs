using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using WhatsBiz.Application.Features.DemoRequests;

namespace WhatsBiz.Tests.DemoRequests;

[Collection("SQL demo requests")]
public sealed class DemoRequestApiIntegrationTests
{
    private const string ConnectionString = "Server=DESKTOP-DQ0868S;Database=WhatsBizERP;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connection Timeout=10";

    [Fact]
    public async Task PublicEndpointPersistsLeadAndCorsAllowsOnlyConfiguredWebsite()
    {
        await using var factory = new DemoRequestFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var mobile = "91" + Random.Shared.NextInt64(7000000000, 9999999999);
        var input = new DemoRequestInput("API Demo Test", mobile, "api@example.test", "API Store", "Delhi", "Other", "API integration", "instagram", "paid", "api-test", null, "https://khatadhari.com/demo", null);
        var startedOn = DateTimeOffset.UtcNow;
        long id = 0;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/demo-requests") { Content = JsonContent.Create(input) };
            request.Headers.Add("Origin", "https://khatadhari.com");
            var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("https://khatadhari.com");
            var result = await response.Content.ReadFromJsonAsync<DemoRequestSubmissionResult>();
            result.Should().NotBeNull();
            result!.ReferenceNo.Should().MatchRegex("^KD-[0-9]{6,}$");
            id = result.LeadId;

            var unauthorizedUpdate = await client.PatchAsJsonAsync($"/api/demo-requests/{id}/status", new { status = "CONTACTED" });
            unauthorizedUpdate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var blocked = new HttpRequestMessage(HttpMethod.Options, "/api/demo-requests");
            blocked.Headers.Add("Origin", "https://evil.example");
            blocked.Headers.Add("Access-Control-Request-Method", "POST");
            var blockedResponse = await client.SendAsync(blocked);
            blockedResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        }
        finally
        {
            if (id > 0) await DeleteAsync(id, startedOn);
        }
    }

    private static async Task DeleteAsync(long id, DateTimeOffset startedOn)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("DELETE FROM marketing.DemoRequests WHERE Id=@id; DELETE FROM admin.AuditLogs WHERE UserName IS NULL AND RequestPath='/api/demo-requests' AND OccurredOn>=@started;", connection);
        command.Parameters.Add("@id", System.Data.SqlDbType.BigInt).Value = id;
        command.Parameters.Add("@started", System.Data.SqlDbType.DateTimeOffset).Value = startedOn;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class DemoRequestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Jwt:SigningKey"] = "demo-request-integration-test-key-32-characters-minimum",
                ["Cors:AllowedOrigins:0"] = "https://khatadhari.com",
                ["ProductImageStorage:Provider"] = "DATABASE",
                ["DemoRequests:Email:Enabled"] = "false",
                ["DemoRequests:Captcha:Enabled"] = "false"
            }));
    }
}
