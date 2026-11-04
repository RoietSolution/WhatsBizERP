using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class PartnerProductSourceEntitlementTests
{
    [Fact]
    public async Task RetailerWithoutEffectiveEntitlementCannotAssociatePartnerProduct()
    {
        await using var db = CreateDb();
        var tenant = Guid.NewGuid(); var product = Guid.NewGuid();
        db.Products.Add(new Product { TenantId=tenant, ProductId=product, ProductCode="P1", ProductName="Product" });
        await db.SaveChangesAsync();
        var features = new TestFeatures(false);
        var service = new PartnerSellerService(db,new TestUser(tenant),features);

        var act = () => service.SetProductListing(product,Guid.NewGuid(),"test",default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        features.LastFeatureKey.Should().Be(FeatureKeys.PartnerSellerManagement);
        db.PartnerProductListings.Should().BeEmpty();
    }

    [Fact]
    public async Task EntitledRetailerCanAssociateOnlyItsVerifiedActivePartner()
    {
        await using var db = CreateDb();
        var tenant = Guid.NewGuid(); var product = Guid.NewGuid(); var seller = Guid.NewGuid();
        db.Products.Add(new Product { TenantId=tenant, ProductId=product, ProductCode="P1", ProductName="Product" });
        db.PartnerSellers.Add(new PartnerSeller { PartnerSellerId=seller, OperatingTenantId=tenant, SellerName="Partner", SellerType="BUSINESS", SellerStatus="ACTIVE", VerificationStatus="VERIFIED", Version=[1] });
        await db.SaveChangesAsync();
        var service = new PartnerSellerService(db,new TestUser(tenant),new TestFeatures(true));

        await service.SetProductListing(product,seller,"test",default);

        db.PartnerProductListings.Should().ContainSingle().Which.Should().Match<PartnerProductListing>(x=>x.OperatingTenantId==tenant&&x.ProductId==product&&x.PartnerSellerId==seller&&x.ListingStatus=="PENDING_APPROVAL");
    }

    [Fact]
    public async Task DisabledEntitlementCannotRemovePreviouslySavedPartnerAssociation()
    {
        await using var db = CreateDb();
        var tenant = Guid.NewGuid(); var product = Guid.NewGuid(); var seller = Guid.NewGuid();
        db.Products.Add(new Product { TenantId=tenant, ProductId=product, ProductCode="P1", ProductName="Product" });
        db.PartnerProductListings.Add(new PartnerProductListing { OperatingTenantId=tenant, ProductId=product, PartnerSellerId=seller, ListingStatus="PENDING_APPROVAL", CreatedBy="test", CreatedAt=DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var service = new PartnerSellerService(db,new TestUser(tenant),new TestFeatures(false));

        var act = () => service.SetProductListing(product,null,"test",default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        db.ChangeTracker.Clear();
        (await db.PartnerProductListings.SingleAsync()).PartnerSellerId.Should().Be(seller);
    }

    private static ApplicationDbContext CreateDb()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestUser(Guid tenant):ICurrentUserService
    {
        public Guid? UserId=>Guid.NewGuid();public Guid? TenantId=>tenant;public string? Username=>"test";public string? Email=>null;
        public IReadOnlyCollection<string> Roles=>[];public IReadOnlyCollection<string> Permissions=>[];
    }

    private sealed class TestFeatures(bool enabled):IFeatureService
    {
        public string? LastFeatureKey { get; private set; }
        public Task<bool> IsEnabledAsync(Guid tenantId,string featureKey,CancellationToken cancellationToken=default){LastFeatureKey=featureKey;return Task.FromResult(enabled);}
        public Task<IReadOnlyDictionary<string,bool>> GetEffectiveFeaturesAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId,IReadOnlyCollection<TenantFeatureUpdate> updates,string? changedBy,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantDisplayNameAsync(Guid tenantId,string? displayName,string? changedBy,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public void InvalidateTenant(Guid tenantId){} public void InvalidateAll(){}
    }
}
