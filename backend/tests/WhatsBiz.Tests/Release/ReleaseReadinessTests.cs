using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsBiz.Application.Features.Authentication.DTOs;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Release;

public sealed class ReleaseReadinessTests
{
    [Fact]
    public async Task ApiSecurityHealthAuthenticationAndPermissionsAreReleaseReady()
    {
        Permissions.All.Should().OnlyHaveUniqueItems();
        Permissions.All.Should().NotContain(string.Empty);

        await using var factory = new ReleaseFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var testUser = await factory.CreateTestUserAsync();
        try
        {
            var health = await client.GetAsync("/health");
            health.StatusCode.Should().Be(HttpStatusCode.OK);
            health.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
            (await client.GetAsync("/api/admin/company")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(testUser.UserName, testUser.Password));
            login.EnsureSuccessStatusCode();
            var auth = await login.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
            (await client.GetAsync("/api/admin/company")).EnsureSuccessStatusCode();
        }
        finally
        {
            await factory.DeleteTestUserAsync(testUser.UserId);
        }
    }

    private sealed class ReleaseFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Server=DESKTOP-DQ0868S;Database=WhatsBizERP;Integrated Security=True;Encrypt=False;TrustServerCertificate=True" }
            }));

        public async Task<(Guid UserId, string UserName, string Password)> CreateTestUserAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantId = await db.Database.SqlQuery<Guid>($"SELECT TOP (1) TenantId AS Value FROM core.Tenants WHERE IsActive = 1 ORDER BY CreatedOn").FirstAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var name = $"release-test-{Guid.NewGuid():N}";
            const string password = "ReleaseTest@123456";
            var user = new ApplicationUser { UserName = name, Email = $"{name}@whatsbiz.local", EmailConfirmed = true, TenantId = tenantId };
            (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
            (await users.AddToRoleAsync(user, "Administrator")).Succeeded.Should().BeTrue();
            return (user.Id, name, password);
        }

        public async Task DeleteTestUserAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            if (user is not null)
            {
                var roles = await users.GetRolesAsync(user);
                (await users.RemoveFromRolesAsync(user, roles)).Succeeded.Should().BeTrue();
                await db.RefreshTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                (await users.DeleteAsync(user)).Succeeded.Should().BeTrue();
            }
        }
    }
}
