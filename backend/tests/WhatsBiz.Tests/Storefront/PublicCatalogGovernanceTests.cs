using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Api.Middleware;
using WhatsBiz.Infrastructure.Features;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Storefront;

public sealed class PublicCatalogGovernanceTests
{
    [Theory]
    [InlineData(null,null,"DRAFT")]
    [InlineData("APPROVED",false,"CHANGES_PENDING")]
    [InlineData("PENDING_APPROVAL",false,"CHANGES_PENDING")]
    [InlineData("LEGACY",false,"CHANGES_PENDING")]
    [InlineData("APPROVED",true,"APPROVED")]
    [InlineData("LEGACY",true,"LEGACY")]
    [InlineData("REJECTED",false,"REJECTED")]
    [InlineData("SUSPENDED",false,"SUSPENDED")]
    public void EffectiveStatusRejectsPendingContent(string? state,bool? same,string expected)
        => PublicCatalogPolicy.EffectiveStatus(state,same is null?null:same.Value?[1,2]:[3,4],[1,2]).Should().Be(expected);

    [Theory]
    [InlineData("DRAFT","SUBMIT",false,null,"PENDING_APPROVAL")]
    [InlineData("REJECTED","SUBMIT",false,null,"PENDING_APPROVAL")]
    [InlineData("CHANGES_PENDING","SUBMIT",false,null,"PENDING_APPROVAL")]
    [InlineData("LEGACY","SUBMIT",true,null,"PENDING_APPROVAL")]
    [InlineData("PENDING_APPROVAL","APPROVE",true,null,"APPROVED")]
    [InlineData("PENDING_APPROVAL","REJECT",true,"Please correct the description","REJECTED")]
    [InlineData("APPROVED","SUSPEND",true,"Listing suspended","SUSPENDED")]
    [InlineData("SUSPENDED","SUBMIT",true,null,"PENDING_APPROVAL")]
    public void ExplicitTransitions(string state,string action,bool owner,string? reason,string expected)
        => PublicCatalogPolicy.Transition(state,action,owner,reason).Should().Be(expected);

    [Theory]
    [InlineData("APPROVE")][InlineData("REJECT")][InlineData("SUSPEND")]
    public void RetailersCannotReview(string action)
    {var act=()=>PublicCatalogPolicy.Transition("PENDING_APPROVAL",action,false,"reason");act.Should().Throw<System.UnauthorizedAccessException>();}

    [Theory]
    [InlineData("PENDING_APPROVAL","REJECT",null)]
    [InlineData("APPROVED","SUSPEND","")]
    [InlineData("CHANGES_PENDING","APPROVE",null)]
    [InlineData("DRAFT","APPROVE",null)]
    [InlineData("SUSPENDED","SUBMIT",null)]
    public void InvalidReviewOrMissingReasonIsRejected(string state,string action,string? reason)
    {var act=()=>PublicCatalogPolicy.Transition(state,action,action!="SUBMIT",reason);act.Should().Throw<BusinessRuleException>();}

    [Fact]
    public void RealPublicProductQueryTranslatesApprovalAndTenantEligibilityWithoutOpeningSql()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=TranslationOnly;Integrated Security=True;Encrypt=True").Options);
        var store=new StorefrontService(db,null!);
        var method=typeof(StorefrontService).GetMethod("EligibleProducts",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var query=(IQueryable<WhatsBiz.Domain.Products.Product>)method.Invoke(store,[Guid.NewGuid()])!;
        var sql=query.ToQueryString();
        sql.Should().Contain("[commerce].[IsProductPubliclyApproved]").And.Contain("TenantProductCategories").And.Contain("TenantUnitsOfMeasure");
        sql.Should().Contain("IsWhatsAppVisible").And.Contain("IsActive").And.Contain("IsDeleted");
    }

    [Fact]
    public async Task DirectoryGuestHttpListsOnlyActiveOptedInOwnerApprovedStores()
    {
        using var server=Server();using var client=server.CreateClient();
        var response=await client.GetAsync("/api/store-directory");response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body=await response.Content.ReadAsStringAsync();
        body.Should().Contain("public-store").And.NotContain("hidden-store").And.NotContain("inactive-store").And.NotContain("private-store").And.NotContain("invalid/key").And.NotContain("TenantId");
    }

    [Fact]
    public async Task OwnerHttpRequiresRoleAndPermissionAndGuestCannotSubmit()
    {
        using var server=Server();using var guest=server.CreateClient();
        (await guest.GetAsync("/api/public-catalog/owner/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await guest.PostAsJsonAsync($"/api/public-catalog/products/{Guid.NewGuid()}/submit",new{action="APPROVE"})).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var retailer=server.CreateClient();retailer.DefaultRequestHeaders.Add("X-Role","Retailer");
        (await retailer.GetAsync("/api/public-catalog/owner/directory")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var ownerWithoutPermission=server.CreateClient();ownerWithoutPermission.DefaultRequestHeaders.Add("X-Role","ApplicationOwner");
        (await ownerWithoutPermission.GetAsync("/api/public-catalog/owner/directory")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var owner=server.CreateClient();owner.DefaultRequestHeaders.Add("X-Role","ApplicationOwner");owner.DefaultRequestHeaders.Add("X-Manage","yes");
        (await owner.GetAsync("/api/public-catalog/owner/directory")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await retailer.GetAsync("/api/public-catalog/owner/business-types")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await guest.GetAsync("/api/public-catalog/owner/business-types")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await owner.GetAsync("/api/public-catalog/owner/business-types")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServiceRejectsCrossTenantDetailsBeforeReadingProducts()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var catalog=new PublicCatalogService(db,new TestUser());
        var act=()=>catalog.Details(Guid.NewGuid(),Guid.NewGuid(),default);
        await act.Should().ThrowAsync<System.UnauthorizedAccessException>();
    }

    private static TestServer Server()
    {
        var name=Guid.NewGuid().ToString();
        var server=new TestServer(new WebHostBuilder().ConfigureServices(s=>{
            s.AddDbContext<ApplicationDbContext>(o=>o.UseInMemoryDatabase(name));s.AddScoped<PublicCatalogService>();s.AddScoped<BusinessTypeCatalogService>();s.AddScoped<ICurrentUserService,TestUser>();s.AddHttpContextAccessor();
            s.AddScoped<IProductImageStorage,_UnusedImages>();
            s.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("Fixture",_=>{});
            s.AddAuthorization(o=>{
                o.AddPolicy(PermissionPolicyProvider.Prefix+Permissions.Features.Manage,p=>p.RequireAuthenticatedUser().RequireClaim("manage","yes"));
                o.AddPolicy(PermissionPolicyProvider.Prefix+Permissions.Product.Edit,p=>p.RequireAuthenticatedUser());
                o.AddPolicy(PermissionPolicyProvider.FeaturePrefix+FeatureKeys.Products,p=>p.RequireAuthenticatedUser());
            });
            s.AddControllers().AddApplicationPart(typeof(StoreDirectoryController).Assembly);
        }).Configure(app=>{app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseMiddleware<TenantContextAuthorizationMiddleware>();app.UseEndpoints(e=>e.MapControllers());}));
        using var scope=server.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Add("public-store",true,true,true);Add("hidden-store",true,true,false);Add("inactive-store",false,true,true);Add("private-store",true,false,true);Add("invalid/key",true,true,true);db.SaveChanges();
        void Add(string key,bool active,bool opted,bool approved){var id=Guid.NewGuid();db.Add(new Tenant{TenantId=id,TenantKey=key,Name=key,IsActive=active});db.Add(new StoreDirectoryEntry{TenantId=id,BusinessType="Grocer",ShortDescription="Neighbourhood groceries",OptedIn=opted,OwnerApproved=approved,Version=[1]});}
        return server;
    }
    private sealed class TestUser(IHttpContextAccessor? context=null):ICurrentUserService {public Guid? UserId=>Guid.Parse("00000000-0000-0000-0000-000000000001");public Guid? TenantId=>Guid.Parse("00000000-0000-0000-0000-000000000002");public string? Username=>"fixture";public string? Email=>null;public IReadOnlyCollection<string> Roles=>context?.HttpContext?.User.FindAll(ClaimTypes.Role).Select(c=>c.Value).ToArray()??[];public IReadOnlyCollection<string> Permissions=>[];}
    private sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync(){var role=Request.Headers["X-Role"].ToString();if(role.Length==0)return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new List<Claim>{new(ClaimTypes.Name,"fixture"),new(ClaimTypes.Role,role)};if(Request.Headers["X-Manage"]=="yes")claims.Add(new("manage","yes"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));}
    }
    private sealed class _UnusedImages:IProductImageStorage
    {
        public string ActiveProvider=>"DATABASE";
        public Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest r,CancellationToken c)=>throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest r,CancellationToken c)=>throw new NotSupportedException();
        public Task DeleteAsync(ProductImageStorageDeleteRequest r,CancellationToken c)=>throw new NotSupportedException();
    }
}
