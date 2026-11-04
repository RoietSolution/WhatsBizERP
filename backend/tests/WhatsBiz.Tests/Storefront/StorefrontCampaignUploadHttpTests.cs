using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using ImageMagick;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Products;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Storefront;

// Real MVC multipart/auth/exception pipeline; persistence is an isolated in-memory service double.
// These tests do NOT establish QA SQL/storage health or actual campaign association persistence.
public sealed class StorefrontCampaignUploadHttpTests
{
    [Fact]
    public async Task DisabledEntitlementRejectsCampaignManagementBeforeServiceExecution()
    {
        var fake=new Campaigns();using var server=Server(fake,false);using var client=Client(server,fake.Owner);
        (await client.GetAsync("/api/storefront-administration/campaigns")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var data=Form([1],"png");(await client.PostAsync(Path(fake),data)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(Path(fake))).StatusCode.Should().Be(HttpStatusCode.Forbidden);fake.Uploads.Should().Be(0);
    }
    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task ValidMultipartUploadAndReplacementCanBeRetrieved(string format)
    {
        var fake=new Campaigns();using var server=Server(fake);using var client=Client(server,fake.Owner);
        using var source=new MagickImage(MagickColors.Green,32,20);
        var bytes=source.ToByteArray(format switch{"jpeg"=>MagickFormat.Jpeg,"png"=>MagickFormat.Png,_=>MagickFormat.WebP});
        using var upload=Form(bytes,format);var response=await client.PostAsync(Path(fake),upload);response.StatusCode.Should().Be(HttpStatusCode.NoContent);fake.Uploads.Should().Be(1);
        var image=await client.GetAsync(Path(fake));image.StatusCode.Should().Be(HttpStatusCode.OK);image.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");(await image.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
        using var replacement=Form(bytes,format);(await client.PostAsync(Path(fake),replacement)).StatusCode.Should().Be(HttpStatusCode.NoContent);fake.Uploads.Should().Be(2);
    }
    [Fact]
    public async Task InvalidAndOversizedImagesProduceActionable4xxAndKeepOldImage()
    {
        var fake=new Campaigns();using var server=Server(fake);using var client=Client(server,fake.Owner);var original=fake.Image;
        using var corrupt=Form([1,2,3],"jpeg");var response=await client.PostAsync(Path(fake),corrupt);response.StatusCode.Should().Be(HttpStatusCode.Conflict);(await response.Content.ReadAsStringAsync()).Should().Contain("not a valid JPEG");fake.Image.Should().BeSameAs(original);
        using var large=Form(new byte[5242881],"png");response=await client.PostAsync(Path(fake),large);response.StatusCode.Should().Be(HttpStatusCode.BadRequest);fake.Image.Should().BeSameAs(original);fake.Uploads.Should().Be(0);
    }
    [Fact]
    public async Task MissingMultipartFieldAndUnauthorizedTenantsCannotUpload()
    {
        var fake=new Campaigns();using var server=Server(fake);using var client=Client(server,fake.Owner);
        using var wrongField=new MultipartFormDataContent();wrongField.Add(new ByteArrayContent([1]),"wrongField","test.png");(await client.PostAsync(Path(fake),wrongField)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var guest=server.CreateClient();using var guestForm=Form([1],"png");(await guest.PostAsync(Path(fake),guestForm)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var other=Client(server,Guid.NewGuid());using var otherForm=Form([1],"png");(await other.PostAsync(Path(fake),otherForm)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await other.GetAsync(Path(fake))).StatusCode.Should().Be(HttpStatusCode.NotFound);fake.Uploads.Should().Be(0);
    }
    [Fact]
    public async Task StorageFailureRetainsImageAndReturnsSafeProblemWithCorrelation()
    {
        var fake=new Campaigns{Fail=true};using var server=Server(fake);using var client=Client(server,fake.Owner);var original=fake.Image;client.DefaultRequestHeaders.Add("X-Correlation-ID","campaign-upload-test");
        using var data=Form([1],"png");var response=await client.PostAsync(Path(fake),data);response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);response.Headers.GetValues("X-Correlation-ID").Should().ContainSingle().Which.Should().Be("campaign-upload-test");(await response.Content.ReadAsStringAsync()).Should().NotContain("private storage failure");fake.Image.Should().BeSameAs(original);
    }
    private static string Path(Campaigns fake)=>$"/api/storefront-administration/campaigns/{fake.Id}/image";
    private static MultipartFormDataContent Form(byte[] bytes,string format){var body=new MultipartFormDataContent();var file=new ByteArrayContent(bytes);file.Headers.ContentType=new MediaTypeHeaderValue("image/"+format);body.Add(file,"file","test."+format);return body;}
    private static HttpClient Client(TestServer server,Guid tenant){var client=server.CreateClient();client.DefaultRequestHeaders.Add("X-Test-Tenant",tenant.ToString());return client;}
    private static TestServer Server(Campaigns fake,bool entitled=true)=>new(new WebHostBuilder().ConfigureServices(services=>{
        services.AddLogging();services.AddHttpContextAccessor();services.AddSingleton<IStorefrontCampaignService>(fake);services.AddScoped<ICurrentUserService,User>();
        services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuthentication>("Fixture",_=>{});
        services.AddAuthorization(options=>{options.AddPolicy(PermissionPolicyProvider.Prefix+Permissions.Admin.Settings,policy=>policy.RequireAuthenticatedUser());options.AddPolicy(PermissionPolicyProvider.FeaturePrefix+WhatsBiz.Application.Common.Features.FeatureKeys.StorefrontPopupCampaigns,policy=>policy.RequireAuthenticatedUser().RequireAssertion(_=>entitled));});
        services.AddControllers().AddApplicationPart(typeof(StorefrontCampaignsController).Assembly);
    }).Configure(app=>{app.UseMiddleware<GlobalExceptionMiddleware>();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseEndpoints(endpoints=>endpoints.MapControllers());}));
    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Guid.TryParse(Request.Headers["X-Test-Tenant"],out var tenant)?AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id",tenant.ToString())],Scheme.Name)),Scheme.Name)):AuthenticateResult.NoResult());}
    private sealed class User(IHttpContextAccessor context):ICurrentUserService
    {public Guid? UserId=>null;public Guid? TenantId=>Guid.TryParse(context.HttpContext?.User.FindFirst("tenant_id")?.Value,out var tenant)?tenant:null;public string? Username=>null;public string? Email=>null;public IReadOnlyCollection<string> Roles=>[];public IReadOnlyCollection<string> Permissions=>[];}
    private sealed class Campaigns:IStorefrontCampaignService
    {
        public Guid Owner {get;}=Guid.NewGuid();public Guid Id {get;}=Guid.NewGuid();public int Uploads {get;private set;}public bool Fail {get;init;}public StorefrontImage Image {get;private set;}=new("image/webp",[9]);
        public async Task UploadAsync(Guid tenant,Guid id,string name,Stream content,CancellationToken token){if(tenant!=Owner||id!=Id)throw new BusinessRuleException("Campaign was not found.");if(Fail)throw new InvalidOperationException("private storage failure");using var buffer=new MemoryStream();await content.CopyToAsync(buffer,token);var result=await new ProductImageOptimizer().OptimizeAsync(name,null,buffer.ToArray(),token);Image=new(result.ContentType,result.CatalogData);Uploads++;}
        public Task<StorefrontImage?> GetAdminImageAsync(Guid tenant,Guid id,CancellationToken token)=>Task.FromResult<StorefrontImage?>(tenant==Owner&&id==Id?Image:null);
        public Task<IReadOnlyCollection<StorefrontCampaign>> ListAsync(Guid tenant,CancellationToken token)=>throw new NotSupportedException();
        public Task<StorefrontCampaign> SaveAsync(Guid tenant,Guid? id,SaveStorefrontCampaign input,CancellationToken token)=>throw new NotSupportedException();
        public Task RemoveImageAsync(Guid tenant,Guid id,CancellationToken token)=>throw new NotSupportedException();
        public Task<StorefrontCampaignFeed?> FeedAsync(string key,CancellationToken token)=>throw new NotSupportedException();
        public Task<StorefrontImage?> ImageAsync(string key,Guid id,Guid media,CancellationToken token)=>throw new NotSupportedException();
    }
}
