using System.Security.Claims;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
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
        var identitySchema = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "WhatsBiz.Database", "Tables", "Identity.sql"));
        var trigger = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "WhatsBiz.Database", "Triggers", "TR_UserRoles_AccountScope.sql"));
        var qaEnvironment = File.ReadAllText(Path.Combine(RepositoryRoot(), "deployment", "qa.env.example"));

        identitySchema.Should().Contain("[TenantId] UNIQUEIDENTIFIER NULL")
            .And.Contain("[AccountType] NVARCHAR(30) NOT NULL")
            .And.Contain("[CK_Users_AccountScope]");
        migration.Should().Contain("CK_Users_AccountScope")
            .And.NotContain("CREATE OR ALTER TRIGGER core.TR_UserRoles_AccountScope");
        trigger.Should().Contain("CREATE TRIGGER [core].[TR_UserRoles_AccountScope]")
            .And.Contain("r.NormalizedName=N'APPLICATIONOWNER' AND u.AccountType<>N'APPLICATION_OWNER'")
            .And.Contain("r.NormalizedName<>N'APPLICATIONOWNER' AND u.AccountType=N'APPLICATION_OWNER'")
            .And.Contain("THROW 52704");
        qaEnvironment.Should().Contain("IdentityBootstrap__ApplicationOwner__Username=qa.owner")
            .And.NotContain("IdentityBootstrap__ApplicationOwner__TenantKey");
    }

    [Fact]
    public void V27UsesAtomicRepeatableAccountScopeMigrationAfterDacpacPublication()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "WhatsBiz.Database", "Scripts", "V27-PlatformOwnerIdentity.sql"));

        migration.Should().Contain("SET AccountType=N'APPLICATION_OWNER', TenantId=NULL")
            .And.Contain("WHERE u.AccountType<>N'APPLICATION_OWNER' OR u.TenantId IS NOT NULL")
            .And.NotContain("UPDATE core.Users SET AccountType=N'RETAILER' WHERE AccountType IS NULL")
            .And.NotContain("ALTER TABLE core.Users ALTER COLUMN TenantId")
            .And.NotContain("ALTER TABLE core.Users ALTER COLUMN AccountType")
            .And.Contain("AccountType IS NULL OR (AccountType=N'APPLICATION_OWNER' AND TenantId IS NOT NULL)")
            .And.Contain("is_disabled=0")
            .And.Contain("is_not_trusted=0")
            .And.Contain("THROW 52705");
    }

    [Fact]
    public void RepeatablePostDeploymentDoesNotTenantAssignPlatformOwnersOrRetightenTenantId()
    {
        var featureMigration = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "WhatsBiz.Database", "Scripts", "V2-FeatureEntitlements.sql"));
        var deployment = File.ReadAllText(Path.Combine(RepositoryRoot(), "deployment", "deploy-qa-database.ps1"));

        featureMigration.Should().Contain("IF COL_LENGTH(N'core.Users', N'AccountType') IS NULL")
            .And.Contain("u.TenantId IS NULL AND u.AccountType=N''RETAILER''")
            .And.NotContain("N'UPDATE core.Users SET TenantId=@id WHERE TenantId IS NULL'")
            .And.NotContain("ALTER TABLE core.Users ALTER COLUMN TenantId uniqueidentifier NOT NULL");
        deployment.Should().Contain("obsolete unconditional core.Users tenant migration")
            .And.Contain("$obsoleteIdentityStatements");
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
    public async Task AuthenticatedRetailerRequestContinuesWithItsTenantClaim()
    {
        var called = false;
        var retailer = Context(owner: false, tenantId: Guid.NewGuid(), platform: false);

        await new TenantContextAuthorizationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(retailer);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task OptionsRequestIsNeverRejectedByTenantContextAuthorization()
    {
        var called = false;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/whatsapp/configuration";
        context.Request.Method = HttpMethods.Options;

        await new TenantContextAuthorizationMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
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
            typeof(AdminController).GetMethod(nameof(AdminController.Logins)),
            typeof(SystemLogsController).GetMethod(nameof(SystemLogsController.Search))
        };

        operations.Should().NotContainNulls();
        foreach (var method in operations)
            method!.GetCustomAttributes(typeof(PlatformAuthorizeAttribute), true).Should().ContainSingle();
    }

    [Fact]
    public void WhatsAppBusinessAndEcommerceDemoAreOwnerOnlyExplicitTenantOperations()
    {
        var methods = new[] { typeof(WhatsAppController), typeof(WhatsAppCommerceController) }
            .SelectMany(type => type.GetMethods())
            .Select(method => new
            {
                Method = method,
                Route = method.GetCustomAttributes(typeof(HttpMethodAttribute), true)
                    .Cast<HttpMethodAttribute>().SelectMany(attribute => attribute.Template is null ? [] : new[] { attribute.Template }).FirstOrDefault()
            })
            .Where(item => item.Route?.Contains("administration/tenants/{tenantId:guid}", StringComparison.Ordinal) == true)
            .ToArray();

        methods.Should().NotBeEmpty();
        methods.Should().OnlyContain(item => item.Method.GetCustomAttributes(typeof(PlatformAuthorizeAttribute), true).Length == 1);
        methods.Should().OnlyContain(item => item.Method.GetParameters().Any(parameter => parameter.Name == "tenantId" && parameter.ParameterType == typeof(Guid)));
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
