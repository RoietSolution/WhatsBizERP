using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontCampaignBoundaryTests
{
    [Fact]
    public async Task AdminUsesAuthenticatedTenantNotClientTenant()
    {
        var tenant=Guid.NewGuid();var fake=new Campaigns();var controller=new StorefrontCampaignsController(fake,new User(tenant));
        await controller.List(CancellationToken.None);fake.Tenant.Should().Be(tenant);
        typeof(StorefrontCampaignsController).GetCustomAttributes<AuthorizeAttribute>().Should().NotBeEmpty();
        typeof(StorefrontCampaignsController).GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
    }
    [Fact]
    public async Task MissingAdminTenantCannotReachPersistence()
    {
        var fake=new Campaigns();var controller=new StorefrontCampaignsController(fake,new User(null));
        var call=()=>controller.List(CancellationToken.None);await call.Should().ThrowAsync<UnauthorizedAccessException>();fake.Tenant.Should().BeNull();
    }
    [Fact]
    public void PublicDtoContainsNoCustomerOrPrivateMediaIdentifiers()
    {
        typeof(PublicStorefrontCampaign).GetProperties().Select(x=>x.Name).Should().NotContain(["TenantId","CustomerId","MediaId","ObjectKey","Name"]);
        typeof(PublicStorefrontCampaignsController).GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }
    private sealed class User(Guid? tenant):ICurrentUserService
    {public Guid? UserId=>null;public Guid? TenantId=>tenant;public string? Username=>null;public string? Email=>null;public IReadOnlyCollection<string> Roles=>[];public IReadOnlyCollection<string> Permissions=>[];}
    private sealed class Campaigns:IStorefrontCampaignService
    {
        public Guid? Tenant {get;private set;}
        public Task<IReadOnlyCollection<StorefrontCampaign>> ListAsync(Guid tenant,CancellationToken token){Tenant=tenant;return Task.FromResult<IReadOnlyCollection<StorefrontCampaign>>([]);}
        public Task<StorefrontCampaign> SaveAsync(Guid tenant,Guid? id,SaveStorefrontCampaign input,CancellationToken token)=>throw new NotSupportedException();
        public Task UploadAsync(Guid tenant,Guid id,string name,Stream content,CancellationToken token)=>throw new NotSupportedException();
        public Task RemoveImageAsync(Guid tenant,Guid id,CancellationToken token)=>throw new NotSupportedException();
        public Task<StorefrontCampaignFeed?> FeedAsync(string key,CancellationToken token)=>throw new NotSupportedException();
        public Task<StorefrontImage?> ImageAsync(string key,Guid id,Guid media,CancellationToken token)=>throw new NotSupportedException();
    }
}
