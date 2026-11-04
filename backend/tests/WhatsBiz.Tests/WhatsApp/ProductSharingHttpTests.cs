using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using ImageMagick;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.WhatsApp;

// Actual MVC/auth/serialization/image encoding; the SQL-backed service boundary is doubled.
public sealed class ProductSharingHttpTests
{
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    [Fact]
    public async Task PreviewNeedsProductPermissionAndSendAlsoNeedsAdminPermissionAndEntitlement()
    {
        using var server = Server(); using var guest = server.CreateClient();
        (await guest.GetAsync($"/api/products/{Product}/whatsapp-share")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var viewer = Client(server, "viewer");
        var preview = await viewer.GetAsync($"/api/products/{Product}/whatsapp-share");
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await preview.Content.ReadFromJsonAsync<ProductSharePreview>();
        body!.ProductUrl.Should().Contain($"/store-a/products/{Product}"); body.SellingPrice.Should().Be(118m);
        (await viewer.PostAsJsonAsync($"/api/products/{Product}/whatsapp-share", new SendProductShareInput(Customer, Guid.NewGuid(), true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var noFeature = Client(server, "admin-no-feature");
        (await noFeature.PostAsJsonAsync($"/api/products/{Product}/whatsapp-share", new SendProductShareInput(Customer, Guid.NewGuid(), true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var admin = Client(server, "admin");
        (await admin.PostAsJsonAsync($"/api/products/{Product}/whatsapp-share", new SendProductShareInput(Customer, Guid.NewGuid(), true)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublicShareImageConvertsWebpToJpegAndRejectsAnotherStoreOrProduct()
    {
        using var server = Server(); using var guest = server.CreateClient();
        var response = await guest.GetAsync($"/api/store/store-a/products/{Product}/share-image");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        using var image = new MagickImage(await response.Content.ReadAsByteArrayAsync());
        image.Format.Should().Be(MagickFormat.Jpeg);
        response.Headers.ETag.Should().NotBeNull();
        (await guest.GetAsync($"/api/store/store-b/products/{Product}/share-image")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await guest.GetAsync($"/api/store/store-a/products/{Guid.NewGuid()}/share-image")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RichPreviewHasServerRenderedEscapedMetadataAndOnlyTrustedProductDestination()
    {
        using var server = Server(); using var guest = server.CreateClient();
        var response = await guest.GetAsync($"/api/store/store-a/products/{Product}/share?destination=https://untrusted.example/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("property=\"og:image\"").And.Contain("property=\"og:title\"")
            .And.Contain($"https://shop.example.test/store-a/products/{Product}")
            .And.Contain("&lt;script&gt;").And.NotContain("<script>").And.NotContain("untrusted.example");
        html.Should().Contain("/share-image?v=").And.Contain("property=\"og:image:width\" content=\"4\"")
            .And.Contain("property=\"og:image:height\" content=\"4\"").And.Contain("property=\"og:image:alt\"")
            .And.Contain("name=\"twitter:card\" content=\"summary_large_image\"")
            .And.Contain("window.location.replace(").And.NotContain("http-equiv=\"refresh\"")
            .And.NotContain("KhataDhari Shop");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("script-src 'nonce-");
        (await guest.GetAsync($"/api/store/store-b/products/{Product}/share")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CrawlerAndBrowserReceiveTheSamePublicMetadataWithoutHttpRedirects()
    {
        using var server = Server(); using var browser = server.CreateClient(); using var crawler = server.CreateClient();
        crawler.DefaultRequestHeaders.UserAgent.ParseAdd("facebookexternalhit/1.1");
        var url = $"/api/store/store-a/products/{Product}/share?v=old-version";
        var browserResponse = await browser.GetAsync(url); var crawlerResponse = await crawler.GetAsync(url);
        browserResponse.StatusCode.Should().Be(HttpStatusCode.OK); crawlerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        browserResponse.Headers.Location.Should().BeNull(); crawlerResponse.Headers.Location.Should().BeNull();
        var first = await browserResponse.Content.ReadAsStringAsync(); var second = await crawlerResponse.Content.ReadAsStringAsync();
        // Only the per-response CSP nonce differs; there is no crawler-specific security bypass.
        System.Text.RegularExpressions.Regex.Replace(first, "nonce=\"[^\"]+\"", "nonce=\"redacted\"")
            .Should().Be(System.Text.RegularExpressions.Regex.Replace(second, "nonce=\"[^\"]+\"", "nonce=\"redacted\""));
        second.Should().Contain($"/products/{Product}\"").And.NotContain("http-equiv=\"refresh\"");
    }

    private static TestServer Server()
    {
        var commerce = DispatchProxy.Create<IWhatsAppCommerceService, Proxy>();
        ((Proxy)(object)commerce).Call = (method, args) => method.Name switch {
            nameof(IWhatsAppCommerceService.GetProductShareAsync) when (Guid)args![0]! == Tenant => Task.FromResult(new ProductSharePreview(Product,"Product",118,"₹118.00","Details","Store",
                $"https://shop.example.test/store-a/products/{Product}",null,"Product link","https://wa.me/?text=Product",true,null)),
            nameof(IWhatsAppCommerceService.SendProductShareAsync) when (Guid)args![0]! == Tenant => Task.FromResult(new ProductShareSendResult(((SendProductShareInput)args[2]!).RequestId,"ACCEPTED","mock-fixture","MOCK")),
            _ => throw new InvalidOperationException("Unexpected service call.") };
        var storefront = DispatchProxy.Create<IStorefrontService, Proxy>();
        ((Proxy)(object)storefront).Call = (method, args) => {
            var eligible = (string)args![0]! == "store-a" && (args.Length < 3 || (Guid)args[1]! == Product);
            if (method.Name == nameof(IStorefrontService.GetProductImageAsync)) {
                using var image = new MagickImage(MagickColors.Green, 4, 4);
                return Task.FromResult<StorefrontImage?>(eligible ? new("image/webp", image.ToByteArray(MagickFormat.WebP)) : null);
            }
            if (method.Name == nameof(IStorefrontService.GetProductAsync)) return Task.FromResult<StorefrontProductDto?>(eligible
                ? new(Product,Guid.NewGuid(),"<script>Product</script>","Details",null,[],null,118,null,"IN_STOCK",null,null,0) : null);
            if (method.Name == nameof(IStorefrontService.GetStoreAsync)) return Task.FromResult<StorefrontStoreDto?>(eligible
                ? new("store-a","Store","",null,"#145c43","",null,false,false,[],[]) : null);
            throw new InvalidOperationException("Unexpected storefront call.");
        };
        return new TestServer(new WebHostBuilder().ConfigureServices(services => {
            services.AddLogging(); services.AddHttpContextAccessor(); services.AddSingleton(commerce); services.AddSingleton(storefront);
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["Storefront:PublicBaseUrl"]="https://shop.example.test/",["Storefront:PublicApiBaseUrl"]="https://api.example.test/" }).Build());
            services.AddScoped<ICurrentUserService, User>();
            services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, Auth>("Fixture", _ => {});
            services.AddAuthorization(options => {
                options.AddPolicy(PermissionPolicyProvider.Prefix+Permissions.Product.View, p => p.RequireAuthenticatedUser());
                options.AddPolicy(PermissionPolicyProvider.Prefix+Permissions.Admin.Settings, p => p.RequireClaim("admin"));
                options.AddPolicy(PermissionPolicyProvider.FeaturePrefix+FeatureKeys.WhatsAppCommerce, p => p.RequireClaim("commerce"));
            });
            services.AddControllers().AddApplicationPart(typeof(ProductSharingController).Assembly);
        }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllers()); }));
    }
    private static HttpClient Client(TestServer server, string role) { var client=server.CreateClient();client.DefaultRequestHeaders.Add("X-Fixture-Role",role);return client; }
    public class Proxy : DispatchProxy { public Func<MethodInfo,object?[]?,object?> Call {get;set;}=null!;protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Call(targetMethod!,args); }
    private sealed class User : ICurrentUserService { public Guid? TenantId=>Tenant;public Guid? UserId=>Guid.NewGuid();public string? Username=>"fixture";public string? Email=>null;public IReadOnlyCollection<string> Roles=>[];public IReadOnlyCollection<string> Permissions=>[]; }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var role=Request.Headers["X-Fixture-Role"].ToString();if(role.Length==0)return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new List<Claim>{new(ClaimTypes.Name,"fixture")};if(role.StartsWith("admin",StringComparison.Ordinal))claims.Add(new("admin","yes"));if(role!="admin-no-feature")claims.Add(new("commerce","yes"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }
}
