using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontAdministrationServiceTests
{
    [Fact]
    public async Task LogoUploadPersistsTenantAssociationAndSurvivesOtherSettingsUpdates()
    {
        await using var db = CreateDb();
        var tenant = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "GUTURGO", Name = "GuturGo", IsActive = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var service = new StorefrontAdministrationService(db, new CurrentUser(tenant.TenantId), new TestOptimizer(), new TestStorage());

        await service.UploadImageAsync("logo", null, "logo.png", new MemoryStream([1, 2, 3]), default);

        var configuration = await db.StorefrontConfigurations.SingleAsync(x => x.TenantId == tenant.TenantId);
        configuration.LogoMediaId.Should().NotBeNull();
        var media = await db.StorefrontMedia.SingleAsync(x => x.TenantId == tenant.TenantId && x.MediaId == configuration.LogoMediaId);
        media.ImageData.Should().Equal(1, 2, 3);
        var admin = await service.GetAsync(default);
        admin.StoreName.Should().Be("GuturGo");
        admin.LogoUrl.Should().Be("/api/storefront-administration/logo");

        var storefront = new StorefrontService(db, new NoProductImages(), new TestStorage());
        var publicStore = await storefront.GetStoreAsync("guturgo", default);
        publicStore!.LogoUrl.Should().Be("/api/store/GUTURGO/presentation/logo");
        (await storefront.GetPresentationImageAsync("guturgo", "logo", null, default))!.Content.Should().Equal(1, 2, 3);

        await service.UpdateBannerAsync(StorefrontBannerSlots.Primary,
            new(false, null, null, "Seasonal", null, null, 1), default);

        (await db.StorefrontConfigurations.SingleAsync(x => x.TenantId == tenant.TenantId)).LogoMediaId.Should().Be(configuration.LogoMediaId);
        (await service.GetAsync(default)).LogoUrl.Should().Be(admin.LogoUrl);
    }

    [Fact]
    public async Task BannerImageAssociationSurvivesSavingBannerSettingsAndAdminDtoReturnsBothSlots()
    {
        await using var db = CreateDb();
        var tenant = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "GUTURGO", Name = "GuturGo", IsActive = true };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var service = new StorefrontAdministrationService(db, new CurrentUser(tenant.TenantId), new TestOptimizer(), new TestStorage());

        await service.UploadImageAsync("banner-primary", null, "primary.png", new MemoryStream([4, 5, 6]), default);
        var beforeSave = await db.StorefrontBanners.SingleAsync(x => x.TenantId == tenant.TenantId && x.Slot == StorefrontBannerSlots.Primary);
        var mediaId = beforeSave.MediaId;
        mediaId.Should().NotBeNull();

        var starts = DateTimeOffset.UtcNow.AddMinutes(-1);
        var ends = DateTimeOffset.UtcNow.AddMinutes(10);
        await service.UpdateBannerAsync("primary", new(true, starts, ends, "Fresh picks", "Today", "/offers", 7), default);

        var afterSave = await db.StorefrontBanners.SingleAsync(x => x.TenantId == tenant.TenantId && x.Slot == StorefrontBannerSlots.Primary);
        afterSave.MediaId.Should().Be(mediaId);
        afterSave.IsEnabled.Should().BeTrue();
        afterSave.StartsAt.Should().Be(starts);
        afterSave.EndsAt.Should().Be(ends);
        afterSave.DisplayOrder.Should().Be(7);
        var admin = await service.GetAsync(default);
        admin.Banners.Should().HaveCount(2);
        admin.Banners.Single(x => x.Slot == StorefrontBannerSlots.Primary).ImageUrl
            .Should().Be("/api/storefront-administration/banners/PRIMARY/image");
        admin.Banners.Single(x => x.Slot == StorefrontBannerSlots.Secondary).ImageUrl.Should().BeNull();

        var publicService = new StorefrontService(db, new NoProductImages(), new TestStorage());
        var store = await publicService.GetStoreAsync("GUTURGO", default);
        store!.Banners.Should().ContainSingle(x => x.Slot == StorefrontBannerSlots.Primary);
        (await publicService.GetPresentationImageAsync("guturgo", "banner-primary", null, default))!.Content.Should().Equal(4, 5, 6);
        (await publicService.GetPresentationImageAsync("guturgo", "banner-secondary", null, default)).Should().BeNull();
    }

    [Fact]
    public async Task OpenEndedAndCurrentlyScheduledBannersArePublicAndSlotsAreIndependent()
    {
        await using var db = CreateDb();
        var tenant = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "GUTURGO", Name = "GuturGo", IsActive = true };
        db.Tenants.Add(tenant);
        var primary = Media(tenant.TenantId, "banner-primary", [7]);
        var secondary = Media(tenant.TenantId, "banner-secondary", [8]);
        db.StorefrontMedia.AddRange(primary, secondary);
        var now = DateTimeOffset.UtcNow;
        db.StorefrontBanners.AddRange(
            new StorefrontBanner { BannerId = Guid.NewGuid(), TenantId = tenant.TenantId, Slot = StorefrontBannerSlots.Primary, MediaId = primary.MediaId, IsEnabled = true },
            new StorefrontBanner { BannerId = Guid.NewGuid(), TenantId = tenant.TenantId, Slot = StorefrontBannerSlots.Secondary, MediaId = secondary.MediaId, IsEnabled = true, StartsAt = now.AddMinutes(-2), EndsAt = now.AddMinutes(2) });
        await db.SaveChangesAsync();

        var store = await new StorefrontService(db, new NoProductImages(), new TestStorage()).GetStoreAsync("guturgo", default);
        store!.Banners.Select(x => x.Slot).Should().BeEquivalentTo(StorefrontBannerSlots.Primary, StorefrontBannerSlots.Secondary);
    }

    private static StorefrontMedia Media(Guid tenantId, string type, byte[] data) => new()
    {
        MediaId = Guid.NewGuid(), TenantId = tenantId, ResourceType = type, StorageProvider = "DATABASE",
        ContentType = "image/webp", ImageData = data, ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data))
    };

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CurrentUser(Guid tenantId) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenantId;
        public string? Username => "storefront-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }

    private sealed class TestOptimizer : IProductImageOptimizer
    {
        public Task<OptimizedProductImage> OptimizeAsync(string fileName, string? suppliedContentType, byte[] content, CancellationToken cancellationToken)
            => Task.FromResult(new OptimizedProductImage("optimized.webp", "image/webp", content, content, 1, 1, 1, 1));
    }

    private sealed class TestStorage : IStorefrontMediaStorage
    {
        public string ActiveProvider => "DATABASE";
        public Task<StoredStorefrontMedia> StoreStorefrontAsync(StorefrontMediaStorageWriteRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new StoredStorefrontMedia("DATABASE", null, null, request.CatalogContent.Length, request.ThumbnailContent.Length,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.CatalogContent))));
        public Task<ProductImageStorageContent?> ReadStorefrontAsync(StorefrontMediaStorageReadRequest request, CancellationToken cancellationToken)
            => Task.FromResult<ProductImageStorageContent?>(request.DatabaseContent.Length == 0 ? null : new(request.DatabaseContent, request.ContentType));
        public Task DeleteStorefrontAsync(StorefrontMediaStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoProductImages : IProductImageStorage
    {
        public string ActiveProvider => "DATABASE";
        public Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest request, CancellationToken cancellationToken) => Task.FromResult<ProductImageStorageContent?>(null);
        public Task DeleteAsync(ProductImageStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
