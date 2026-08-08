using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Features.Authentication.DTOs;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Tests.Release; public sealed class ReleaseReadinessTests { [Fact] public async Task ApiSecurityHealthAuthenticationAndPermissionsAreReleaseReady() { Permissions.All.Should().OnlyHaveUniqueItems(); Permissions.All.Should().NotContain(string.Empty); await using var factory = new ReleaseFactory(); using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }); var health = await client.GetAsync("/health"); health.StatusCode.Should().Be(HttpStatusCode.OK); health.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff"); (await client.GetAsync("/api/admin/company")).StatusCode.Should().Be(HttpStatusCode.Unauthorized); var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "Admin@123456")); login.EnsureSuccessStatusCode(); var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken); (await client.GetAsync("/api/admin/company")).EnsureSuccessStatusCode(); } private sealed class ReleaseFactory : WebApplicationFactory<Program> { protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { { "ConnectionStrings:DefaultConnection", "Server=localhost;Database=WhatsBizERP;Integrated Security=True;TrustServerCertificate=True" } })); } }
