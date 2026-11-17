using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Domain.Inventory;
using WhatsBiz.Domain.Products;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Warehouses;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class StorefrontServiceTests
{
    private static readonly string[] SensitiveFieldNames = ["TenantId", "PurchasePrice", "Cost", "QuantityOnHand", "QuantityAvailable", "SupplierId"];
    [Fact]
    public async Task StoreKeyResolvesOnlyAnActiveTenantAndRejectsInvalidKeys()
    {
        await using var db = CreateDb();
        var active = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "GUTURGO", Name = "GuturGo", IsActive = true };
        var inactive = new Tenant { TenantId = Guid.NewGuid(), TenantKey = "CLOSED", Name = "Closed Store", IsActive = false };
        db.Tenants.AddRange(active, inactive);
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new NoProductImages());

        (await service.GetStoreAsync("guturgo", default))!.StoreKey.Should().Be("guturgo");
        (await service.GetStoreAsync("closed", default)).Should().BeNull();
        (await service.GetStoreAsync("unknown", default)).Should().BeNull();
        (await service.GetStoreAsync("../GUTURGO", default)).Should().BeNull();
    }

    [Fact]
    public async Task ProductAndCategoryQueriesAreTenantScopedAndHonorVisibility()
    {
        await using var db = CreateDb();
        var first = await AddCatalogAsync(db, "FIRST", "Store One", withStock: false);
        var other = await AddCatalogAsync(db, "OTHER", "Store Two", withStock: false);
        var hidden = await AddProductAsync(db, first, "Hidden", isVisible: false, isActive: true);
        var inactive = await AddProductAsync(db, first, "Inactive", isVisible: true, isActive: false);
        db.ProductImages.AddRange(
            new ProductImage { TenantId = first.Tenant.TenantId, ProductId = first.Product.ProductId, IsPrimary = true, ContentType = "image/webp", ImageData = [1], Width = 1000, Height = 1200, ThumbnailWidth = 250, ThumbnailHeight = 300 },
            new ProductImage { TenantId = first.Tenant.TenantId, ProductId = inactive.Product.ProductId, IsPrimary = true, ContentType = "image/webp", ImageData = [2] });
        db.ProductImages.Add(new ProductImage { TenantId = other.Tenant.TenantId, ProductId = other.Product.ProductId, IsPrimary = true, ContentType = "image/webp", ImageData = [3] });
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new MemoryProductImages());

        var products = (await service.GetProductsAsync("FIRST", default))!;
        products.Should().ContainSingle().Which.Id.Should().Be(first.Product.ProductId);
        (await service.GetProductAsync("FIRST", other.Product.ProductId, default)).Should().BeNull();
        (await service.GetProductAsync("FIRST", hidden.Product.ProductId, default)).Should().BeNull();
        (await service.GetProductAsync("FIRST", inactive.Product.ProductId, default)).Should().BeNull();
        (await service.GetProductImageAsync("FIRST", first.Product.ProductId, null, false, default)).Should().NotBeNull();
        (await service.GetProductImageAsync("FIRST", inactive.Product.ProductId, null, false, default)).Should().BeNull();
        (await service.GetProductImageAsync("FIRST", other.Product.ProductId, null, false, default)).Should().BeNull();
        var details = (await service.GetProductAsync("FIRST", first.Product.ProductId, default))!;
        details.Images.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
        var listImage = (await service.GetProductsAsync("FIRST", 1, 20, default))!.Items.Single().Images.Should().ContainSingle().Which;
        listImage.Width.Should().Be(1000);
        listImage.ThumbnailWidth.Should().Be(250);
        (await service.GetCategoriesAsync("FIRST", default))!.Should().ContainSingle().Which.Id.Should().Be(first.Category.ProductCategoryId);
    }

    [Fact]
    public async Task ProductImageFallsBackToOriginalWhenStoredThumbnailIsUnavailable()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "IMAGEFALLBACK", "Image Fallback", withStock: false);
        db.ProductImages.Add(new ProductImage
        {
            TenantId = catalog.Tenant.TenantId,
            ProductId = catalog.Product.ProductId,
            IsPrimary = true,
            StorageProvider = "S3",
            ObjectKey = "catalog/original.webp",
            ThumbnailObjectKey = "catalog/missing-thumbnail.webp",
            ContentType = "image/webp",
            ThumbnailContentType = "image/webp"
        });
        db.SaveChanges();
        var storage = new MissingThumbnailProductImages();
        var service = new StorefrontService(db, storage);

        var result = await service.GetProductImageAsync("IMAGEFALLBACK", catalog.Product.ProductId, null, thumbnail: true, default);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/webp");
        result.Content.Should().Equal(42);
        storage.Requests.Select(request => request.ObjectKey).Should().Equal("catalog/missing-thumbnail.webp", "catalog/original.webp");
        storage.Requests.Should().OnlyContain(request => request.TenantId == catalog.Tenant.TenantId);
    }

    [Fact]
    public async Task MissingStoredThumbnailIsGeneratedFromTheTenantScopedOriginal()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "IMAGEGENERATE", "Image Generate", withStock: false);
        var image = new ProductImage
        {
            TenantId = catalog.Tenant.TenantId, ProductId = catalog.Product.ProductId, IsPrimary = true,
            StorageProvider = "S3", ObjectKey = "catalog/original.webp", ThumbnailObjectKey = "catalog/missing-thumbnail.webp",
            ContentType = "image/webp", ThumbnailContentType = "image/webp"
        };
        db.ProductImages.Add(image);
        await db.SaveChangesAsync();
        var storage = new MissingThumbnailProductImages();
        var optimizer = new FixedProductImageOptimizer();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var service = new StorefrontService(db, storage, imageOptimizer: optimizer, imageThumbnailCache: cache);

        var result = await service.GetProductImageAsync("IMAGEGENERATE", catalog.Product.ProductId, image.ProductImageId, thumbnail: true, default);
        var cachedResult = await service.GetProductImageAsync("IMAGEGENERATE", catalog.Product.ProductId, image.ProductImageId, thumbnail: true, default);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/webp");
        result.Content.Should().Equal(9);
        cachedResult!.Content.Should().Equal(9);
        optimizer.ReceivedContent.Should().Equal(42);
        optimizer.Calls.Should().Be(1);
        storage.Requests.Select(request => request.ObjectKey).Should().Equal("catalog/missing-thumbnail.webp", "catalog/original.webp", "catalog/missing-thumbnail.webp");
        storage.Requests.Should().OnlyContain(request => request.TenantId == catalog.Tenant.TenantId);
        (await db.ProductImages.SingleAsync()).ThumbnailObjectKey.Should().Be("catalog/missing-thumbnail.webp");
    }

    [Fact]
    public async Task LegacyImageWithoutThumbnailMetadataIsGeneratedWithoutStorageMutation()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "IMAGELEGACY", "Image Legacy", withStock: false);
        db.ProductImages.Add(new ProductImage
        {
            TenantId = catalog.Tenant.TenantId, ProductId = catalog.Product.ProductId, IsPrimary = true,
            StorageProvider = "S3", ObjectKey = "catalog/original.webp", ContentType = "image/webp"
        });
        await db.SaveChangesAsync();
        var storage = new MissingThumbnailProductImages();
        var optimizer = new FixedProductImageOptimizer();
        var service = new StorefrontService(db, storage, imageOptimizer: optimizer);

        var result = await service.GetProductImageAsync("IMAGELEGACY", catalog.Product.ProductId, null, thumbnail: true, default);

        result.Should().NotBeNull();
        result!.Content.Should().Equal(9);
        optimizer.ReceivedContent.Should().Equal(42);
        storage.Requests.Select(request => request.ObjectKey).Should().Equal("catalog/original.webp");
        var stored = await db.ProductImages.SingleAsync();
        stored.ThumbnailObjectKey.Should().BeNull();
        stored.ThumbnailData.Should().BeEmpty();
    }

    [Fact]
    public async Task UntransformableLegacyThumbnailRequestRetainsOriginalImageFallback()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "IMAGEUNSUPPORTED", "Image Unsupported", withStock: false);
        db.ProductImages.Add(new ProductImage
        {
            TenantId = catalog.Tenant.TenantId, ProductId = catalog.Product.ProductId, IsPrimary = true,
            StorageProvider = "S3", ObjectKey = "catalog/original.webp", ContentType = "image/webp"
        });
        await db.SaveChangesAsync();
        var optimizer = new FixedProductImageOptimizer { Reject = true };
        var service = new StorefrontService(db, new MissingThumbnailProductImages(), imageOptimizer: optimizer);

        var result = await service.GetProductImageAsync("IMAGEUNSUPPORTED", catalog.Product.ProductId, null, thumbnail: true, default);

        result.Should().NotBeNull();
        result!.Content.Should().Equal(42);
    }

    [Fact]
    public async Task ProductDetailsPreserveLegacyAndRichDescriptionsAndReturnTenantSpecifications()
    {
        await using var db = CreateDb();
        var first = await AddCatalogAsync(db, "DETAILS", "Details Store", withStock: false);
        first.Product.ShortDescription = "Legacy summary";
        first.Product.LongDescription = "<h2>Details</h2><p><strong>Fresh</strong></p>";
        first.Product.Specifications.Add(new ProductSpecification { TenantId = first.Tenant.TenantId, ProductId = first.Product.ProductId, Name = "Material", Value = "Cotton", DisplayOrder = 0 });
        first.Product.Weight = 280;
        first.Product.Specifications.Add(new ProductSpecification { TenantId = first.Tenant.TenantId, ProductId = first.Product.ProductId, Name = "Physical Weight Unit", Value = "g", DisplayOrder = 1 });
        var other = await AddCatalogAsync(db, "DETAILS2", "Other Details Store", withStock: false);
        other.Product.Specifications.Add(new ProductSpecification { TenantId = other.Tenant.TenantId, ProductId = other.Product.ProductId, Name = "Material", Value = "Wool", DisplayOrder = 0 });
        await db.SaveChangesAsync();

        var service = new StorefrontService(db, new NoProductImages());
        var details = (await service.GetProductAsync("DETAILS", first.Product.ProductId, default))!;

        details.Description.Should().Be("Legacy summary");
        details.LongDescription.Should().Contain("<strong>Fresh</strong>");
        details.Specifications.Should().Contain(new StorefrontProductSpecificationDto("Material", "Cotton"));
        details.Specifications.Should().Contain(new StorefrontProductSpecificationDto("Weight", "280 g"));
        details.Specifications.Should().NotContain(specification => specification.Name == "Physical Weight Unit");
        (await service.GetProductAsync("DETAILS", other.Product.ProductId, default)).Should().BeNull();
    }

    [Fact]
    public async Task AvailabilityUsesTenantInventoryAndPublicDtoOmitsSensitiveFields()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "STOCKED", "Stocked Store", withStock: true);
        var empty = await AddProductAsync(db, catalog, "No Stock", isVisible: true, isActive: true);
        empty.Product.CategoryId = catalog.Category.ProductCategoryId;
        empty.Product.Category = catalog.Category;
        empty.Product.Unit = catalog.Unit;
        empty.Product.Brand = catalog.Brand;
        var otherTenant = await AddCatalogAsync(db, "OTHER", "Other Store", withStock: false);
        var otherWarehouse = new Warehouse { WarehouseId = Guid.NewGuid(), WarehouseCode = "WH-OTHER", WarehouseName = "Other Store", IsActive = true };
        db.Warehouses.Add(otherWarehouse);
        db.Entry(otherWarehouse).Property<Guid?>("TenantId").CurrentValue = otherTenant.Tenant.TenantId;
        await AddBalanceAsync(db, catalog.Tenant.TenantId, catalog.Product.ProductId, catalog.Warehouse!, onHand: 5, reserved: 2);
        await AddBalanceAsync(db, otherTenant.Tenant.TenantId, empty.Product.ProductId, otherWarehouse, onHand: 50, reserved: 0);
        db.SaveChanges();
        var service = new StorefrontService(db, new NoProductImages());

        var products = (await service.GetProductsAsync("STOCKED", default))!.ToDictionary(item => item.Id);
        products[catalog.Product.ProductId].Availability.Should().Be("IN_STOCK");
        products[empty.Product.ProductId].Availability.Should().Be("OUT_OF_STOCK");
        var publicFields = typeof(StorefrontProductDto).GetProperties().Select(property => property.Name);
        publicFields.Should().NotContain(name => SensitiveFieldNames.Contains(name));
    }

    [Fact]
    public async Task ProductFiltersApplyBeforePaginationAndUseStableEffectivePriceOrdering()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "FILTERPAGE", "Filter Page Store", withStock: true);
        catalog.Product.ProductName = "Middle price";
        catalog.Product.SellingPrice = 20;
        catalog.Product.CreatedOn = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var low = await AddProductAsync(db, catalog, "Low price", isVisible: true, isActive: true);
        low.Product.SellingPrice = 10;
        low.Product.CreatedOn = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
        var high = await AddProductAsync(db, catalog, "High price", isVisible: true, isActive: true);
        high.Product.SellingPrice = 30;
        high.Product.CreatedOn = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var outOfRange = await AddProductAsync(db, catalog, "Out of range", isVisible: true, isActive: true);
        outOfRange.Product.SellingPrice = 40;
        foreach (var product in new[] { catalog.Product, low.Product, high.Product })
            await AddBalanceAsync(db, catalog.Tenant.TenantId, product.ProductId, catalog.Warehouse!, onHand: 2, reserved: 0);
        db.SaveChanges();

        var service = new StorefrontService(db, new NoProductImages());
        var filters = new StorefrontProductQuery(catalog.Category.ProductCategoryId, MinPrice: 10, MaxPrice: 30, InStockOnly: true, SortBy: "PRICE_LOW_TO_HIGH");
        var firstPage = (await service.GetProductsAsync("FILTERPAGE", 1, 2, filters, default))!;
        var secondPage = (await service.GetProductsAsync("FILTERPAGE", 2, 2, filters, default))!;
        var newest = (await service.GetProductsAsync("FILTERPAGE", 1, 3, filters with { SortBy = "NEWEST" }, default))!;

        firstPage.TotalCount.Should().Be(3);
        firstPage.HasMore.Should().BeTrue();
        firstPage.Items.Select(item => item.SellingPrice).Should().Equal(10, 20);
        secondPage.TotalCount.Should().Be(3);
        secondPage.Items.Select(item => item.SellingPrice).Should().Equal(30);
        firstPage.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)).Should().BeEmpty();
        firstPage.MinimumPrice.Should().Be(10);
        firstPage.MaximumPrice.Should().Be(40);
        firstPage.SupportsNewestSort.Should().BeTrue();
        newest.Items.Select(item => item.SellingPrice).Should().Equal(10, 20, 30);
    }

    [Fact]
    public async Task StockFilterDoesNotUseAnotherTenantsWarehouseInventory()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "FILTERTENANT", "Filter Tenant Store", withStock: true);
        var otherTenant = await AddCatalogAsync(db, "FILTEROTHER", "Other Filter Store", withStock: true);
        var target = await AddProductAsync(db, catalog, "No local stock", isVisible: true, isActive: true);
        target.Product.SellingPrice = 12;
        await AddBalanceAsync(db, otherTenant.Tenant.TenantId, target.Product.ProductId, otherTenant.Warehouse!, onHand: 50, reserved: 0);
        db.SaveChanges();

        var service = new StorefrontService(db, new NoProductImages());
        var page = (await service.GetProductsAsync("FILTERTENANT", 1, 24,
            new StorefrontProductQuery(InStockOnly: true), default))!;

        page.Items.Should().NotContain(item => item.Id == target.Product.ProductId);
    }

    [Fact]
    public async Task UnsupportedSortFailsClosedAndPaginationPastTheEndIsEmpty()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "FILTERBOUNDARY", "Filter Boundary Store", withStock: false);
        db.SaveChanges();
        var service = new StorefrontService(db, new NoProductImages());

        var pastEnd = (await service.GetProductsAsync("FILTERBOUNDARY", 2, 1, new StorefrontProductQuery(), default))!;
        pastEnd.Items.Should().BeEmpty();
        pastEnd.TotalCount.Should().Be(1);
        pastEnd.HasMore.Should().BeFalse();
        await FluentActions.Awaiting(() => service.GetProductsAsync("FILTERBOUNDARY", 1, 24,
                new StorefrontProductQuery(SortBy: "UNSUPPORTED"), default))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PublicBrandingAndCategoryImagesAreResolvedOnlyForTheirTenant()
    {
        await using var db = CreateDb();
        var first = await AddCatalogAsync(db, "BRAND1", "Brand One", false);
        var second = await AddCatalogAsync(db, "BRAND2", "Brand Two", false);
        var logo = new StorefrontMedia { MediaId = Guid.NewGuid(), TenantId = first.Tenant.TenantId, ResourceType = "logo", StorageProvider = "DATABASE", ContentType = "image/webp", ImageData = [1], ThumbnailContentType = "image/webp", ThumbnailData = [8], ContentHash = "a" };
        var allCategory = new StorefrontMedia { MediaId = Guid.NewGuid(), TenantId = first.Tenant.TenantId, ResourceType = "category-all", StorageProvider = "DATABASE", ContentType = "image/webp", ImageData = [7], ContentHash = "all" };
        var category = new StorefrontMedia { MediaId = Guid.NewGuid(), TenantId = first.Tenant.TenantId, ResourceType = "category", StorageProvider = "DATABASE", ContentType = "image/webp", ImageData = [2], ContentHash = "b" };
        db.StorefrontMedia.AddRange(logo, allCategory, category);
        db.StorefrontConfigurations.Add(new() { TenantId = first.Tenant.TenantId, LogoMediaId = logo.MediaId, AllCategoryMediaId = allCategory.MediaId });
        db.StorefrontCategoryImages.Add(new() { TenantId = first.Tenant.TenantId, ProductCategoryId = first.Category.ProductCategoryId, MediaId = category.MediaId });
        await db.SaveChangesAsync();
        var media = new MemoryStorefrontImages(); var service = new StorefrontService(db, new NoProductImages(), media);

        (await service.GetStoreAsync("BRAND1", default))!.LogoUrl.Should().NotBeNull();
        (await service.GetStoreAsync("BRAND1", default))!.AllCategoryImageUrl.Should().EndWith("/presentation/categories/all");
        (await service.GetStoreAsync("BRAND2", default))!.AllCategoryImageUrl.Should().BeNull();
        (await service.GetStoreAsync("BRAND2", default))!.LogoUrl.Should().BeNull();
        (await service.GetCategoriesAsync("BRAND1", default))!.Single().ImageUrl.Should().NotBeNull();
        (await service.GetCategoriesAsync("BRAND2", default))!.Single().ImageUrl.Should().BeNull();
        (await service.GetPresentationImageAsync("BRAND1", "logo", null, default))!.Content.Should().Equal(1);
        (await service.GetPresentationImageAsync("BRAND1", "logo", null, true, default))!.Content.Should().Equal(8);
        (await service.GetPresentationImageAsync("BRAND1", "category-all", null, default))!.Content.Should().Equal(7);
        (await service.GetPresentationImageAsync("BRAND2", "category-all", null, default)).Should().BeNull();
        (await service.GetPresentationImageAsync("BRAND2", "logo", null, default)).Should().BeNull();
        (await service.GetPresentationImageAsync("BRAND1", "category", second.Category.ProductCategoryId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PresentationImageFallsBackToOriginalWhenStoredThumbnailIsUnavailable()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "PRESENTATIONFALLBACK", "Presentation Fallback", withStock: false);
        var media = new StorefrontMedia
        {
            MediaId = Guid.NewGuid(), TenantId = catalog.Tenant.TenantId, ResourceType = "logo", StorageProvider = "S3",
            ObjectKey = "storefront/original.webp", ThumbnailObjectKey = "storefront/missing-thumbnail.webp",
            ContentType = "image/webp", ThumbnailContentType = "image/webp", ContentHash = "presentation-fallback"
        };
        db.StorefrontMedia.Add(media);
        db.StorefrontConfigurations.Add(new() { TenantId = catalog.Tenant.TenantId, LogoMediaId = media.MediaId });
        await db.SaveChangesAsync();
        var storage = new MissingThumbnailStorefrontImages();
        var service = new StorefrontService(db, new NoProductImages(), storage);

        var result = await service.GetPresentationImageAsync("PRESENTATIONFALLBACK", "logo", null, thumbnail: true, default);

        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/webp");
        result.Content.Should().Equal(43);
        storage.Requests.Select(request => request.ObjectKey).Should().Equal("storefront/missing-thumbnail.webp", "storefront/original.webp");
        storage.Requests.Should().OnlyContain(request => request.TenantId == catalog.Tenant.TenantId);
    }

    [Fact]
    public async Task PublicBannerDtoHonorsServerScheduleAndBothSlots()
    {
        await using var db = CreateDb();
        var tenant = await AddCatalogAsync(db, "BANNER", "Banner Store", false);
        var disabledTenant = await AddCatalogAsync(db, "BNO", "Banner Disabled", false);
        var expiredTenant = await AddCatalogAsync(db, "BEX", "Banner Expired", false);
        var primary = new StorefrontMedia { MediaId = Guid.NewGuid(), TenantId = tenant.Tenant.TenantId, ResourceType = "banner-primary", StorageProvider = "DATABASE", ContentType = "image/webp", ImageData = [3], ThumbnailContentType = "image/webp", ThumbnailData = [9], ContentHash = "c" };
        var secondary = new StorefrontMedia { MediaId = Guid.NewGuid(), TenantId = tenant.Tenant.TenantId, ResourceType = "banner-secondary", StorageProvider = "DATABASE", ContentType = "image/webp", ImageData = [4], ContentHash = "d" };
        var disabled = new StorefrontMedia { MediaId = Guid.NewGuid(), TenantId = disabledTenant.Tenant.TenantId, ResourceType = "banner-primary", StorageProvider = "DATABASE", ContentType = "image/webp", ImageData = [5], ContentHash = "e" };
        var expired = new StorefrontMedia { MediaId = Guid.NewGuid(), TenantId = expiredTenant.Tenant.TenantId, ResourceType = "banner-secondary", StorageProvider = "DATABASE", ContentType = "image/webp", ImageData = [6], ContentHash = "f" };
        db.StorefrontMedia.AddRange(primary, secondary, disabled, expired);
        var now = DateTimeOffset.UtcNow;
        db.StorefrontBanners.AddRange(
            new StorefrontBanner { BannerId = Guid.NewGuid(), TenantId = tenant.Tenant.TenantId, Slot = StorefrontBannerSlots.Primary, MediaId = primary.MediaId, IsEnabled = true },
            new StorefrontBanner { BannerId = Guid.NewGuid(), TenantId = tenant.Tenant.TenantId, Slot = StorefrontBannerSlots.Secondary, MediaId = secondary.MediaId, IsEnabled = true, StartsAt = now.AddDays(1) },
            new StorefrontBanner { BannerId = Guid.NewGuid(), TenantId = disabledTenant.Tenant.TenantId, Slot = StorefrontBannerSlots.Primary, MediaId = disabled.MediaId, IsEnabled = false },
            new StorefrontBanner { BannerId = Guid.NewGuid(), TenantId = expiredTenant.Tenant.TenantId, Slot = StorefrontBannerSlots.Secondary, MediaId = expired.MediaId, IsEnabled = true, EndsAt = now.AddDays(-1) });
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new NoProductImages(), new MemoryStorefrontImages());
        var banners = (await service.GetStoreAsync("BANNER", default))!.Banners;
        banners.Should().ContainSingle().Which.Slot.Should().Be(StorefrontBannerSlots.Primary);
        (await service.GetPresentationImageAsync("BANNER", "banner-primary", null, default))!.Content.Should().Equal(3);
        (await service.GetPresentationImageAsync("BANNER", "banner-primary", null, true, default))!.Content.Should().Equal(9);
        (await service.GetPresentationImageAsync("BANNER", "banner-secondary", null, default)).Should().BeNull();
        (await service.GetStoreAsync("BNO", default))!.Banners.Should().BeEmpty();
        (await service.GetStoreAsync("BEX", default))!.Banners.Should().BeEmpty();
    }

    [Fact]
    public async Task GenericUnitIsOmittedAndMeasuredWeightUsesActualUnit()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "UNITS", "Unit Store", false);
        catalog.Product.Unit.ShortName = "Unit";
        catalog.Product.PackSize = "500 g";
        catalog.Unit.ShortName = "g";
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new NoProductImages());
        (await service.GetProductsAsync("UNITS", default))!.Single().PackSize.Should().Be("500 g");
        catalog.Product.PackSize = null;
        await db.SaveChangesAsync();
        (await service.GetProductsAsync("UNITS", default))!.Single().PackSize.Should().BeNull();
        catalog.Product.PackSize = null;
        await db.SaveChangesAsync();
        (await service.GetProductsAsync("UNITS", default))!.Single().PackSize.Should().BeNull();
    }

    [Fact]
    public async Task CustomerPaymentMethodsAreEffectiveAndIsolatedByStoreTenant()
    {
        await using var db = CreateDb();
        var first = await AddCatalogAsync(db, "PAYONE", "Payment One", false);
        var second = await AddCatalogAsync(db, "PAYTWO", "Payment Two", false);
        await db.SaveChangesAsync();
        var proxy = DispatchProxy.Create<ICommercePaymentService, EnabledMethodsProxy>();
        ((EnabledMethodsProxy)(object)proxy).Resolve = tenantId => tenantId == first.Tenant.TenantId
            ? [new("UPI", "RAZORPAY", "UPI", true), new("COD", "COD", "Cash on Delivery", false)]
            : [new("NET_BANKING", "RAZORPAY", "Net Banking", true)];
        var service = new StorefrontService(db, new NoProductImages(), payments: proxy);

        var firstMethods = (await service.GetStoreAsync("PAYONE", default))!.PaymentMethods;
        firstMethods.Select(x => x.Code).Should().Equal("UPI", "COD");
        firstMethods.Single(x => x.Code == "UPI").Label.Should().Be("UPI");
        var secondMethods = (await service.GetStoreAsync("PAYTWO", default))!.PaymentMethods;
        secondMethods.Select(x => x.Code).Should().Equal("NET_BANKING");
        secondMethods.Single().Label.Should().Be("Net Banking");
    }

    [Fact]
    public async Task PublicStoreExposesOnlyEffectiveFreeDeliveryAndConfiguredReturnWindow()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "BENEFITS", "Benefits Store", false);
        db.StorefrontConfigurations.Add(new() { TenantId = catalog.Tenant.TenantId, DeliveryEnabled = true, FreeDeliveryEnabled = true, FreeDeliveryThreshold = 499, DefaultReturnWindowDays = 14 });
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new NoProductImages());

        var store = (await service.GetStoreAsync("BENEFITS", default))!;
        store.FreeDeliveryEnabled.Should().BeTrue();
        store.FreeDeliveryThreshold.Should().Be(499);
        store.DefaultReturnWindowDays.Should().Be(14);

        db.StorefrontConfigurations.Single().FreeDeliveryEnabled = false;
        await db.SaveChangesAsync();
        (await service.GetStoreAsync("BENEFITS", default))!.FreeDeliveryEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ProductRatingsAreAggregatedServerSideAndHonorStoreSetting()
    {
        await using var db = CreateDb();
        var catalog = await AddCatalogAsync(db, "RATING", "Rating Store", false);
        db.StorefrontConfigurations.Add(new() { TenantId = catalog.Tenant.TenantId, ShowProductRatings = true, ShowProductReviews = true });
        db.StorefrontProductReviews.AddRange(
            new() { TenantId = catalog.Tenant.TenantId, ProductId = catalog.Product.ProductId, CustomerId = Guid.NewGuid(), Rating = 1, ReviewText = "Poor", Status = "PUBLISHED" },
            new() { TenantId = catalog.Tenant.TenantId, ProductId = catalog.Product.ProductId, CustomerId = Guid.NewGuid(), Rating = 5, ReviewText = "Excellent", Status = "PUBLISHED" },
            new() { TenantId = catalog.Tenant.TenantId, ProductId = catalog.Product.ProductId, CustomerId = Guid.NewGuid(), Rating = 5, ReviewText = "Hidden", Status = "HIDDEN" });
        await db.SaveChangesAsync();
        var service = new StorefrontService(db, new NoProductImages());

        var visible = (await service.GetProductsAsync("RATING", default))!.Single();
        visible.AverageRating.Should().Be(3m);
        visible.RatingCount.Should().Be(2);

        (await db.StorefrontConfigurations.SingleAsync()).ShowProductRatings = false;
        await db.SaveChangesAsync();
        var hidden = (await service.GetProductsAsync("RATING", default))!.Single();
        hidden.AverageRating.Should().BeNull();
        hidden.RatingCount.Should().Be(0);
    }

    private static async Task<CatalogFixture> AddCatalogAsync(ApplicationDbContext db, string key, string name, bool withStock)
    {
        var tenant = new Tenant { TenantId = Guid.NewGuid(), TenantKey = key, Name = name, IsActive = true };
        var category = new ProductCategory { ProductCategoryId = Guid.NewGuid(), CategoryCode = $"CAT-{key}", CategoryName = "Pantry", IsActive = true };
        var unit = new UnitOfMeasure { UnitId = Guid.NewGuid(), UnitCode = $"UNIT-{key}", UnitName = "Each", ShortName = "ea", IsActive = true };
        var brand = new Brand { BrandId = Guid.NewGuid(), BrandCode = $"BR-{key}", BrandName = "Local", IsActive = true };
        var product = new Product { ProductId = Guid.NewGuid(), TenantId = tenant.TenantId, ProductCode = $"P-{key}", ProductName = $"Product {key}", CategoryId = category.ProductCategoryId, Category = category, UnitId = unit.UnitId, Unit = unit, BrandId = brand.BrandId, Brand = brand, SellingPrice = 25, MRP = 30, IsActive = true, IsWhatsAppVisible = true };
        db.Tenants.Add(tenant);
        db.ProductCategories.Add(category);
        db.UnitsOfMeasure.Add(unit);
        db.Brands.Add(brand);
        db.Products.Add(product);
        AddTenantLink(db, "TenantProductCategory", "TenantId", tenant.TenantId, "ProductCategoryId", category.ProductCategoryId);
        AddTenantLink(db, "TenantUnitOfMeasure", "TenantId", tenant.TenantId, "UnitId", unit.UnitId);
        Warehouse? warehouse = null;
        if (withStock)
        {
            warehouse = new Warehouse { WarehouseId = Guid.NewGuid(), WarehouseCode = $"WH-{key}", WarehouseName = name, IsActive = true };
            db.Warehouses.Add(warehouse);
            db.Entry(warehouse).Property<Guid?>("TenantId").CurrentValue = tenant.TenantId;
        }
        return await Task.FromResult(new CatalogFixture(tenant, category, unit, brand, product, warehouse));
    }

    private static async Task<ProductFixture> AddProductAsync(ApplicationDbContext db, CatalogFixture catalog, string name, bool isVisible, bool isActive)
    {
        var product = new Product { ProductId = Guid.NewGuid(), TenantId = catalog.Tenant.TenantId, ProductCode = $"P-{Guid.NewGuid():N}", ProductName = name, CategoryId = catalog.Category.ProductCategoryId, Category = catalog.Category, UnitId = catalog.Unit.UnitId, Unit = catalog.Unit, BrandId = catalog.Brand.BrandId, Brand = catalog.Brand, SellingPrice = 8, MRP = 8, IsActive = isActive, IsWhatsAppVisible = isVisible };
        db.Products.Add(product);
        return await Task.FromResult(new ProductFixture(product));
    }

    private static async Task AddBalanceAsync(ApplicationDbContext db, Guid tenantId, Guid productId, Warehouse warehouse, decimal onHand, decimal reserved)
    {
        var product = db.Products.Local.SingleOrDefault(item => item.ProductId == productId) ?? await db.Products.SingleAsync(item => item.ProductId == productId);
        var balance = new InventoryBalance { InventoryBalanceId = Guid.NewGuid(), ProductId = productId, Product = product, WarehouseId = warehouse.WarehouseId, Warehouse = warehouse, QuantityOnHand = onHand, QuantityReserved = reserved };
        db.InventoryBalances.Add(balance);
        db.Entry(balance).Property<Guid?>("TenantId").CurrentValue = tenantId;
    }

    private static void AddTenantLink(ApplicationDbContext db, string typeName, string tenantProperty, Guid tenantId, string idProperty, Guid id)
    {
        var type = typeof(ApplicationDbContext).Assembly.GetType($"WhatsBiz.Infrastructure.Persistence.{typeName}", throwOnError: true)!;
        var row = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty(tenantProperty, BindingFlags.Public | BindingFlags.Instance)!.SetValue(row, tenantId);
        type.GetProperty(idProperty, BindingFlags.Public | BindingFlags.Instance)!.SetValue(row, id);
        db.Add(row);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed record CatalogFixture(Tenant Tenant, ProductCategory Category, UnitOfMeasure Unit, Brand Brand, Product Product, Warehouse? Warehouse);
    private sealed record ProductFixture(Product Product);

    private sealed class MemoryProductImages : IProductImageStorage
    {
        public string ActiveProvider => "DATABASE";
        public Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest request, CancellationToken cancellationToken) => Task.FromResult<ProductImageStorageContent?>(request.DatabaseContent.Length == 0 ? null : new(request.DatabaseContent, request.ContentType));
        public Task DeleteAsync(ProductImageStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class NoProductImages : IProductImageStorage
    {
        public string ActiveProvider => "DATABASE";
        public Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest request, CancellationToken cancellationToken) => Task.FromResult<ProductImageStorageContent?>(null);
        public Task DeleteAsync(ProductImageStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MissingThumbnailProductImages : IProductImageStorage
    {
        public string ActiveProvider => "S3";
        public List<ProductImageStorageReadRequest> Requests { get; } = [];
        public Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<ProductImageStorageContent?>(request.ObjectKey == "catalog/original.webp" ? new([42], request.ContentType) : null);
        }
        public Task DeleteAsync(ProductImageStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedProductImageOptimizer : IProductImageOptimizer
    {
        public byte[]? ReceivedContent { get; private set; }
        public int Calls { get; private set; }
        public bool Reject { get; init; }
        public Task<OptimizedProductImage> OptimizeAsync(string fileName, string? suppliedContentType, byte[] content, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedContent = content;
            if (Reject) throw new WhatsBiz.Application.Common.Exceptions.BusinessRuleException("Unsupported legacy image.");
            return Task.FromResult(new OptimizedProductImage("thumb.webp", "image/webp", [8], [9], 800, 600, 300, 225));
        }
    }

    private sealed class MemoryStorefrontImages : IStorefrontMediaStorage
    {
        public string ActiveProvider => "DATABASE";
        public Task<StoredStorefrontMedia> StoreStorefrontAsync(StorefrontMediaStorageWriteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadStorefrontAsync(StorefrontMediaStorageReadRequest request, CancellationToken cancellationToken) => Task.FromResult<ProductImageStorageContent?>(request.DatabaseContent.Length == 0 ? null : new(request.DatabaseContent, request.ContentType));
        public Task DeleteStorefrontAsync(StorefrontMediaStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MissingThumbnailStorefrontImages : IStorefrontMediaStorage
    {
        public string ActiveProvider => "S3";
        public List<StorefrontMediaStorageReadRequest> Requests { get; } = [];
        public Task<StoredStorefrontMedia> StoreStorefrontAsync(StorefrontMediaStorageWriteRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductImageStorageContent?> ReadStorefrontAsync(StorefrontMediaStorageReadRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<ProductImageStorageContent?>(request.ObjectKey == "storefront/original.webp" ? new([43], request.ContentType) : null);
        }
        public Task DeleteStorefrontAsync(StorefrontMediaStorageDeleteRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public class EnabledMethodsProxy : DispatchProxy
    {
        public Func<Guid, IReadOnlyCollection<WhatsBiz.Application.Features.Payments.EnabledPaymentMethod>> Resolve { get; set; } = _ => [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICommercePaymentService.GetEnabledMethodsForTenantAsync))
                return Task.FromResult(Resolve((Guid)args![0]!));
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
