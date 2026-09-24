using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Infrastructure.WhatsAppCommerce;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.WhatsApp;

public sealed class MetaCatalogDiscoveryTests
{
    private const string Token = "EAA_TEST_TOKEN_MUST_NEVER_BE_RETURNED";

    [Fact]
    public void EndpointRequiresTenantAdminSettingsAndAcceptsNoTenantOrMetaIdentifiers()
    {
        var method = typeof(WhatsAppCommerceController).GetMethod(
            nameof(WhatsAppCommerceController.MetaCatalogDiscovery))!;

        method.GetCustomAttribute<HasPermissionAttribute>()!.Policy.Should()
            .Be("Permission:" + Permissions.Admin.Settings);
        method.GetCustomAttributes<PlatformAuthorizeAttribute>().Should().BeEmpty();
        method.GetParameters().Should().ContainSingle(x => x.ParameterType == typeof(CancellationToken));
        method.GetCustomAttributes<RequireFeatureAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void TenantIsolationRejectsAnotherTenant()
    {
        var authenticated = Guid.NewGuid();
        var requested = Guid.NewGuid();
        var current = new CurrentUserStub(authenticated, ["SystemAdministrator"]);

        var action = () => WhatsAppCommerceService.ResolveAuthenticatedTenant(current, requested);

        action.Should().Throw<WhatsBiz.Application.Common.Exceptions.EntityNotFoundException>();
    }

    [Fact]
    public async Task ExactlyOneLinkedCatalogBecomesCandidateAndTokenIsNeverReturned()
    {
        var provider = Provider([
            Ok("""{"id":"waba-1","owner_business_info":{"id":"business-1","name":"GuturGo"}}"""),
            Ok(PermissionsJson("business_management", "catalog_management")),
            Ok(Data(("catalog-1", "GuturGo Catalog"))),
            Ok(Data()),
            Ok(Data(("catalog-1", "GuturGo Catalog")))
        ], out var handler);

        var result = await provider.DiscoverCatalogsAsync(new("v23.0", "waba-1", Token), default);

        result.Diagnostic.Should().Be("ONE_ELIGIBLE_CATALOG");
        result.EligibleCatalogCount.Should().Be(1);
        result.CandidateCatalogId.Should().Be("catalog-1");
        result.Catalogs.Should().ContainSingle().Which.Should().Be(
            new MetaCatalogDiscoveryCatalog("catalog-1", "GuturGo Catalog", "OWNED_AND_WABA_LINKED", true));
        result.TokenCapabilities.CatalogDiscovery.Should().Be("GRANTED");
        result.TokenCapabilities.CatalogProductManagement.Should().Be("PERMISSION_GRANTED_ASSET_ACCESS_UNVERIFIED");
        JsonSerializer.Serialize(result).Should().NotContain(Token);
        handler.AuthorizationSchemes.Should().OnlyContain(x => x == "Bearer");
        handler.Paths.Should().Equal(
            "/v23.0/waba-1?fields=id,name,owner_business_info,on_behalf_of_business_info",
            "/v23.0/me/permissions",
            "/v23.0/business-1/owned_product_catalogs?fields=id,name,vertical&limit=100",
            "/v23.0/business-1/client_product_catalogs?fields=id,name,vertical&limit=100",
            "/v23.0/waba-1/product_catalogs?fields=id,name,vertical&limit=100");
    }

    [Fact]
    public async Task ZeroCatalogsIsReportedOnlyWhenBusinessAndWabaEdgesSucceed()
    {
        var provider = Provider([
            Ok("""{"id":"waba-1","owner_business_info":{"id":"business-1","name":"GuturGo"}}"""),
            Ok(PermissionsJson("business_management")), Ok(Data()), Ok(Data()), Ok(Data())
        ], out _);

        var result = await provider.DiscoverCatalogsAsync(new("v23.0", "waba-1", Token), default);

        result.Diagnostic.Should().Be("NO_CATALOG");
        result.EligibleCatalogCount.Should().Be(0);
        result.CandidateCatalogId.Should().BeNull();
    }

    [Fact]
    public async Task MultipleLinkedCatalogsRequireSelection()
    {
        var provider = Provider([
            Ok("""{"id":"waba-1","owner_business_info":{"id":"business-1"}}"""),
            Ok(PermissionsJson("business_management", "catalog_management")),
            Ok(Data(("catalog-1", "One"), ("catalog-2", "Two"))), Ok(Data()),
            Ok(Data(("catalog-1", "One"), ("catalog-2", "Two")))
        ], out _);

        var result = await provider.DiscoverCatalogsAsync(new("v23.0", "waba-1", Token), default);

        result.Diagnostic.Should().Be("MULTIPLE_CATALOGS");
        result.EligibleCatalogCount.Should().Be(2);
        result.CandidateCatalogId.Should().BeNull();
    }

    [Fact]
    public async Task PermissionFailureDoesNotPretendThereAreZeroCatalogs()
    {
        var provider = Provider([
            Ok("""{"id":"waba-1","owner_business_info":{"id":"business-1"}}"""),
            Ok(PermissionsJson("whatsapp_business_management")),
            MetaError(HttpStatusCode.Forbidden, 200, 10, "OAuthException", "Requires business_management or catalog_management permission."),
            MetaError(HttpStatusCode.Forbidden, 200, 10, "OAuthException", "Requires business_management or catalog_management permission."),
            MetaError(HttpStatusCode.Forbidden, 200, 10, "OAuthException", "Requires business_management or catalog_management permission.")
        ], out _);

        var result = await provider.DiscoverCatalogsAsync(new("v23.0", "waba-1", Token), default);

        result.EligibleCatalogCount.Should().BeNull();
        result.Diagnostic.Should().Be("CATALOG_PERMISSION_MISSING");
        result.TokenCapabilities.CatalogDiscovery.Should().Be("DENIED");
        result.TokenCapabilities.CatalogProductManagement.Should().Be("PERMISSION_NOT_GRANTED");
        result.Errors.Should().OnlyContain(x => x.MetaCode == 200 && x.MetaSubcode == 10);
    }

    [Fact]
    public async Task MalformedMetaResponseReturnsSanitizedUnknownResult()
    {
        var provider = Provider([Ok("{}")], out _);

        var result = await provider.DiscoverCatalogsAsync(new("v23.0", "waba-1", Token), default);

        result.Diagnostic.Should().Be("UNKNOWN_META_ERROR");
        result.EligibleCatalogCount.Should().BeNull();
        result.Errors.Should().ContainSingle(x => x.Operation == "WABA_DETAILS");
    }

    [Fact]
    public async Task SensitiveMetaErrorContentIsRedacted()
    {
        var provider = Provider([MetaError(HttpStatusCode.BadRequest, 190, null, "OAuthException",
            "Invalid access_token=EAA123456789012345678901234 for Bearer secret-value")], out _);

        var result = await provider.DiscoverCatalogsAsync(new("v23.0", "waba-1", Token), default);
        var json = JsonSerializer.Serialize(result);

        json.Should().NotContain("EAA123456789012345678901234")
            .And.NotContain("secret-value").And.NotContain(Token);
        result.Errors.Should().ContainSingle().Which.SafeMessage.Should().Contain("REDACTED");
    }

    [Fact]
    public void TokenDecryptionFailureReturnsNoPlaintext()
    {
        var token = WhatsAppCommerceService.TryDecryptCatalogToken(
            new ThrowingProtectionProvider(), "protected-token");

        token.Should().BeNull();
    }

    private static MetaCloudApiWhatsAppProvider Provider(IEnumerable<HttpResponseMessage> responses,
        out QueueHandler handler)
    {
        handler = new(responses);
        var client = new HttpClient(handler);
        var settings = new Dictionary<string, string?> { ["WhatsApp:Meta:GraphBaseUrl"] = "https://graph.facebook.test" };
        return new(new Factory(client), new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<MetaCloudApiWhatsAppProvider>.Instance);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage MetaError(HttpStatusCode status, int code, int? subcode,
        string type, string message) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            error = new { code, error_subcode = subcode, type, message }
        }), Encoding.UTF8, "application/json")
    };
    private static string PermissionsJson(params string[] permissions) => JsonSerializer.Serialize(new
    { data = permissions.Select(x => new { permission = x, status = "granted" }) });
    private static string Data(params (string Id, string Name)[] items) => JsonSerializer.Serialize(new
    { data = items.Select(x => new { id = x.Id, name = x.Name }) });

    private sealed class QueueHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<string?> AuthorizationSchemes { get; } = [];
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Get);
            AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
            Paths.Add(request.RequestUri!.PathAndQuery);
            responses.Should().NotBeEmpty();
            return Task.FromResult(responses.Dequeue());
        }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }
    private sealed class ThrowingProtectionProvider : IDataProtectionProvider
    { public IDataProtector CreateProtector(string purpose) => new ThrowingProtector(); }
    private sealed class ThrowingProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => throw new NotSupportedException();
        public byte[] Unprotect(byte[] protectedData) => throw new CryptographicException("unavailable");
    }
    private sealed class CurrentUserStub(Guid tenantId, IReadOnlyCollection<string> roles) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenantId;
        public string? Username => "admin";
        public string? Email => "admin@example.test";
        public IReadOnlyCollection<string> Roles => roles;
        public IReadOnlyCollection<string> Permissions => [WhatsBiz.SharedKernel.Permissions.Admin.Settings];
    }
}
