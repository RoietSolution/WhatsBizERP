using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontOfferTests {
    private static readonly string[] InternalFields = ["TenantId", "IsDeleted", "OfferType", "DiscountValue"];
    [Fact]
    public async Task OfferDetailsAreTenantScopedExposeOnlyPresentationFieldsAndDistinguishExpiry()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "OFFERONE", Name = "Offer One", IsActive = true };
        var other = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "OFFERTWO", Name = "Offer Two", IsActive = true };
        var now = DateTimeOffset.UtcNow;
        var active = new StorefrontPromotion { TenantId = tenant.TenantId, PromotionId = Guid.NewGuid(), OfferName = "Spring", OfferType = "MINIMUM_PURCHASE", DiscountType = "PERCENTAGE", DiscountValue = 15, MinimumPurchaseAmount = 500, MaximumDiscount = 200, StartsAt = now.AddHours(-1), EndsAt = now.AddDays(1), IsActive = true, ShortDescription = "Seasonal saving", DetailedDescription = "Details", PromoCode = "SPRING", TermsAndConditions = "Conditions", CtaLabel = "Shop now", EligibleItemsDescription = "Selected products", UsageLimitPerCustomer = 1 };
        var expired = new StorefrontPromotion { TenantId = tenant.TenantId, PromotionId = Guid.NewGuid(), OfferName = "Past", OfferType = "MINIMUM_PURCHASE", DiscountType = "FLAT", DiscountValue = 20, MinimumPurchaseAmount = 0, EndsAt = now.AddDays(-1), IsActive = true };
        var inactive = new StorefrontPromotion { TenantId = tenant.TenantId, PromotionId = Guid.NewGuid(), OfferName = "Hidden", OfferType = "MINIMUM_PURCHASE", DiscountType = "FLAT", DiscountValue = 20, MinimumPurchaseAmount = 0, IsActive = false };
        db.AddRange(tenant, other, active, expired, inactive);
        db.StorefrontBanners.Add(new StorefrontBanner { BannerId = Guid.NewGuid(), TenantId = tenant.TenantId, Slot = StorefrontBannerSlots.Primary, PromotionId = active.PromotionId, MediaId = Guid.NewGuid(), IsEnabled = true, TargetUrl = "https://shop.khatadhari.com" });
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new NoProductImages());

        var details = await service.GetOfferAsync("offerone", active.PromotionId, default);
        details.Should().NotBeNull();
        details!.Title.Should().Be("Spring");
        details.ShortDescription.Should().Be("Seasonal saving");
        details.PromoCode.Should().Be("SPRING");
        details.BenefitDescription.Should().Contain("15% off");
        details.Status.Should().Be("ACTIVE");
        details.BannerImageUrl.Should().EndWith("/presentation/banners/primary");
        var banner = (await service.GetStoreAsync("offerone", default))!.Banners.Single();
        banner.PromotionId.Should().Be(active.PromotionId);
        banner.TargetUrl.Should().BeNull("linked offer banners must use the tenant storefront offer route");
        (await service.GetOfferAsync("offertwo", active.PromotionId, default)).Should().BeNull();
        (await service.GetOfferAsync("offerone", inactive.PromotionId, default)).Should().BeNull();
        (await service.GetOfferAsync("offerone", expired.PromotionId, default))!.Status.Should().Be("EXPIRED");
        typeof(WhatsBiz.Application.Features.Storefront.StorefrontOfferDto).GetProperties().Select(x => x.Name)
            .Should().NotContain(InternalFields);
    }

    private sealed class NoProductImages : WhatsBiz.Application.Common.Interfaces.IProductImageStorage
    {
        public string ActiveProvider => "DATABASE";
        public Task<WhatsBiz.Application.Common.Interfaces.StoredProductImage> StoreAsync(WhatsBiz.Application.Common.Interfaces.ProductImageStorageWriteRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<WhatsBiz.Application.Common.Interfaces.ProductImageStorageContent?> ReadAsync(WhatsBiz.Application.Common.Interfaces.ProductImageStorageReadRequest request, CancellationToken token) => Task.FromResult<WhatsBiz.Application.Common.Interfaces.ProductImageStorageContent?>(null);
        public Task DeleteAsync(WhatsBiz.Application.Common.Interfaces.ProductImageStorageDeleteRequest request, CancellationToken token) => Task.CompletedTask;
    }
}
