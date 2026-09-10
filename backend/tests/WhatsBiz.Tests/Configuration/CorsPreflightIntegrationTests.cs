using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WhatsBiz.Api.Extensions;
using WhatsBiz.Api.Middleware;

namespace WhatsBiz.Tests.Configuration;

public sealed class CorsPreflightIntegrationTests
{
    private const string AllowedOrigin = "https://qa.khatadhari.com";

    [Fact]
    public async Task CorsPreflightRunsBeforeTenantSecurityWhileActualApiRemainsProtected()
    {
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddCors(options => options.AddPolicy(ApiServiceCollectionExtensions.CorsPolicyName,
                    policy => policy.WithOrigins(AllowedOrigin).AllowAnyHeader().AllowAnyMethod()));
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseCors(ApiServiceCollectionExtensions.CorsPolicyName);
                app.UseMiddleware<TenantContextAuthorizationMiddleware>();
                app.UseEndpoints(endpoints => endpoints.MapGet("/api/whatsapp/configuration", () => Results.Ok()));
            }));
        using var client = server.CreateClient();

        using var preflightRequest = Preflight(AllowedOrigin);
        using var response = await client.SendAsync(preflightRequest);

        var preflightBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, preflightBody);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle(AllowedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Headers").Single()
            .Split(',', StringSplitOptions.TrimEntries).Should().Contain(["authorization", "content-type"]);

        using var actualRequest = new HttpRequestMessage(HttpMethod.Get, "/api/whatsapp/configuration");
        actualRequest.Headers.Add("Origin", AllowedOrigin);
        using var actualResponse = await client.SendAsync(actualRequest);

        actualResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        actualResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle(AllowedOrigin);

        using var blockedRequest = Preflight("https://unapproved.example");
        using var blockedResponse = await client.SendAsync(blockedRequest);

        blockedResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/whatsapp/configuration");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }
}
