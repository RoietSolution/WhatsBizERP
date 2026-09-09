using System.Security.Claims;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Infrastructure.Identity;

namespace WhatsBiz.Tests.Authentication;

public sealed class PlatformIdentityTests
{
    [Fact]
    public void OwnerBootstrapOptionsDoNotContainTenantConfiguration()
        => typeof(BootstrapApplicationOwnerOptions).GetProperty("TenantKey").Should().BeNull();

    [Fact]
    public void OwnerBootstrapCreatesPlatformUserWithoutTenant()
    {
        var owner = IdentitySeeder.CreateApplicationOwner("qa.owner", "qa.owner@khatadhari.com");

        owner.TenantId.Should().BeNull();
        owner.AccountType.Should().Be(AccountTypes.ApplicationOwner);
    }

    [Fact]
    public void DatabaseMigrationEnforcesOwnerAndRetailerAccountScopes()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "WhatsBiz.Database", "Scripts", "V27-PlatformOwnerIdentity.sql"));
        var qaEnvironment = File.ReadAllText(Path.Combine(RepositoryRoot(), "deployment", "qa.env.example"));

        migration.Should().Contain("ALTER TABLE core.Users ALTER COLUMN TenantId uniqueidentifier NULL")
            .And.Contain("CK_Users_AccountScope")
            .And.Contain("TR_UserRoles_AccountScope");
        qaEnvironment.Should().Contain("IdentityBootstrap__ApplicationOwner__Username=qa.owner")
            .And.NotContain("IdentityBootstrap__ApplicationOwner__TenantKey");
    }

    [Theory]
    [InlineData(true, "ApplicationOwner", true)]
    [InlineData(true, "Retailer", false)]
    [InlineData(false, "ApplicationOwner", false)]
    [InlineData(false, "Retailer", true)]
    public void LoginPortalMustMatchAccountScope(bool owner, string portal, bool expected)
        => AuthenticationService.IsCorrectPortal(owner, portal).Should().Be(expected);

    [Fact]
    public async Task OwnerCanUsePlatformApiWithoutTenantContext()
    {
        var called = false;
        var context = Context(owner: true, tenantId: null, platform: true);

        await new TenantContextAuthorizationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task OwnerCannotUseRetailerApiWithoutSupportedOwnerWorkflow()
    {
        var called = false;
        var context = Context(owner: true, tenantId: null, platform: false);
        context.Response.Body = new MemoryStream();

        await new TenantContextAuthorizationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task RetailerApiRequiresTenantAndRetailerCannotUsePlatformApi()
    {
        var tenantless = Context(owner: false, tenantId: null, platform: false);
        tenantless.Response.Body = new MemoryStream();
        await new TenantContextAuthorizationMiddleware(_ => Task.CompletedTask).InvokeAsync(tenantless);
        tenantless.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var retailerOnPlatform = Context(owner: false, tenantId: Guid.NewGuid(), platform: true);
        retailerOnPlatform.Response.Body = new MemoryStream();
        await new TenantContextAuthorizationMiddleware(_ => Task.CompletedTask).InvokeAsync(retailerOnPlatform);
        retailerOnPlatform.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void GlobalOperationsUseExplicitPlatformAuthorization()
    {
        var operations = new[]
        {
            typeof(FeatureAccessController).GetMethod(nameof(FeatureAccessController.EnrollTenant)),
            typeof(FeatureAccessController).GetMethod(nameof(FeatureAccessController.Update)),
            typeof(DemoRequestsController).GetMethod(nameof(DemoRequestsController.Search)),
            typeof(WhatsAppController).GetMethod(nameof(WhatsAppController.Platform), [typeof(CancellationToken)]),
            typeof(AdminController).GetMethod(nameof(AdminController.Backup)),
            typeof(AdminController).GetMethod(nameof(AdminController.Restore)),
            typeof(AdminController).GetMethod(nameof(AdminController.Audit)),
            typeof(AdminController).GetMethod(nameof(AdminController.Logins))
        };

        operations.Should().NotContainNulls();
        foreach (var method in operations)
            method!.GetCustomAttributes(typeof(PlatformAuthorizeAttribute), true).Should().ContainSingle();
    }

    private static DefaultHttpContext Context(bool owner, Guid? tenantId, bool platform)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (owner) claims.Add(new Claim(ClaimTypes.Role, "ApplicationOwner"));
        if (tenantId is Guid id) claims.Add(new Claim(CustomClaimTypes.TenantId, id.ToString()));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        context.Request.Path = "/api/test";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(platform ? [new PlatformAuthorizeAttribute()] : []), "test"));
        return context;
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../"));
}
