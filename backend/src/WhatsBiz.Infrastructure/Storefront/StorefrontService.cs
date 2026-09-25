using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontService(ApplicationDbContext db, IProductImageStorage images,
    IStorefrontMediaStorage? presentationImages = null, ICommercePaymentService? payments = null) : IStorefrontService
{
    private static readonly Regex StoreKeyPattern = new("^[A-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<StorefrontStoreDto?> GetStoreAsync(string storeKey, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var configuration = await db.StorefrontConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, token);
        var now = DateTimeOffset.UtcNow;
        var banners = await db.StorefrontBanners.AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId && x.IsEnabled && x.MediaId != null
                && (x.StartsAt == null || x.StartsAt <= now) && (x.EndsAt == null || x.EndsAt >= now))
            .OrderBy(x => x.DisplayOrder).Select(x => new StorefrontBannerDto(x.Slot,
                $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/banners/{x.Slot.ToLowerInvariant()}",
                x.Title, x.Subtitle, x.TargetUrl, x.DisplayOrder)).ToArrayAsync(token);
        var enabled = payments is null ? [] : await payments.GetEnabledMethodsForTenantAsync(tenant.TenantId, token);
        var methods = enabled.Select(ToPaymentMethod).ToArray();
        return new(tenant.TenantKey.ToLowerInvariant(), tenant.Name,
            configuration?.Tagline ?? "Good things, close to home.", configuration?.LogoMediaId is null ? null : $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/logo",
            configuration?.AccentColor ?? "#145c43", configuration?.DeliveryMessage ?? "Fresh picks, close to home.", banners, methods);
    }

    public async Task<IReadOnlyCollection<StorefrontCategoryDto>?> GetCategoriesAsync(string storeKey, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var products = EligibleProducts(tenant.TenantId);
        return await db.ProductCategories.AsNoTracking()
            .Where(category => category.IsActive && !category.IsDeleted
                && db.Set<TenantProductCategory>().Any(link => link.TenantId == tenant.TenantId && link.ProductCategoryId == category.ProductCategoryId)
                && products.Any(product => product.CategoryId == category.ProductCategoryId))
            .OrderBy(category => category.DisplayOrder).ThenBy(category => category.CategoryName)
            .Select(category => new StorefrontCategoryDto(category.ProductCategoryId, category.CategoryName,
                db.StorefrontCategoryImages.Any(x => x.TenantId == tenant.TenantId && x.ProductCategoryId == category.ProductCategoryId)
                    ? $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/categories/{category.ProductCategoryId}" : null))
            .ToArrayAsync(token);
    }

    public async Task<IReadOnlyCollection<StorefrontProductDto>?> GetProductsAsync(string storeKey, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var products = await EligibleProducts(tenant.TenantId).Include(product => product.Category).Include(product => product.Unit)
            .OrderBy(product => product.ProductName).ToArrayAsync(token);
        return await MapProductsAsync(tenant.TenantId, tenant.TenantKey, products, token);
    }

    public async Task<StorefrontProductDto?> GetProductAsync(string storeKey, Guid productId, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var product = await EligibleProducts(tenant.TenantId).Include(item => item.Category).Include(item => item.Unit)
            .SingleOrDefaultAsync(item => item.ProductId == productId, token);
        if (product is null) return null;
        return (await MapProductsAsync(tenant.TenantId, tenant.TenantKey, [product], token)).Single();
    }

    public async Task<StorefrontImage?> GetProductImageAsync(string storeKey, Guid productId, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null || !await EligibleProducts(tenant.TenantId).AnyAsync(product => product.ProductId == productId, token)) return null;
        var image = await db.ProductImages.AsNoTracking()
            .Where(row => row.TenantId == tenant.TenantId && row.ProductId == productId && row.IsActive && !row.IsDeleted)
            .OrderByDescending(row => row.IsPrimary).ThenBy(row => row.CreatedOn).FirstOrDefaultAsync(token);
        if (image is null) return null;
        var content = await images.ReadAsync(new(image.TenantId, image.StorageProvider, image.ObjectKey, image.ImageData, image.ContentType), token);
        return content is null ? null : new(content.ContentType, content.Content);
    }

    public async Task<StorefrontImage?> GetPresentationImageAsync(string storeKey, string resource, Guid? categoryId, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null || presentationImages is null) return null;
        Guid? mediaId = resource.ToLowerInvariant() switch
        {
            "logo" => await db.StorefrontConfigurations.AsNoTracking().Where(x => x.TenantId == tenant.TenantId).Select(x => x.LogoMediaId).SingleOrDefaultAsync(token),
            "banner-primary" => await EligibleBannerMedia(tenant.TenantId, StorefrontBannerSlots.Primary).SingleOrDefaultAsync(token),
            "banner-secondary" => await EligibleBannerMedia(tenant.TenantId, StorefrontBannerSlots.Secondary).SingleOrDefaultAsync(token),
            "category" when categoryId.HasValue => await db.StorefrontCategoryImages.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.ProductCategoryId == categoryId).Select(x => (Guid?)x.MediaId).SingleOrDefaultAsync(token),
            _ => null
        };
        if (mediaId is null) return null;
        var media = await db.StorefrontMedia.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.MediaId == mediaId, token);
        if (media is null) return null;
        var content = await presentationImages.ReadStorefrontAsync(new(media.TenantId, media.StorageProvider, media.ObjectKey, media.ImageData ?? [], media.ContentType), token);
        return content is null ? null : new(content.ContentType, content.Content);
    }

    private IQueryable<Guid?> EligibleBannerMedia(Guid tenantId, string slot)
    {
        var now = DateTimeOffset.UtcNow;
        return db.StorefrontBanners.AsNoTracking().Where(x => x.TenantId == tenantId && x.Slot == slot && x.IsEnabled && x.MediaId != null
            && (x.StartsAt == null || x.StartsAt <= now) && (x.EndsAt == null || x.EndsAt >= now)).Select(x => x.MediaId);
    }

    private IQueryable<Product> EligibleProducts(Guid tenantId) => db.Products.AsNoTracking()
        .Where(product => product.TenantId == tenantId && product.IsActive && !product.IsDeleted && product.IsWhatsAppVisible
            && db.Set<TenantProductCategory>().Any(link => link.TenantId == tenantId && link.ProductCategoryId == product.CategoryId)
            && db.ProductCategories.Any(category => category.ProductCategoryId == product.CategoryId && category.IsActive && !category.IsDeleted)
            && db.Set<TenantUnitOfMeasure>().Any(link => link.TenantId == tenantId && link.UnitId == product.UnitId)
            && db.UnitsOfMeasure.Any(unit => unit.UnitId == product.UnitId && unit.IsActive && !unit.IsDeleted));

    private async Task<IReadOnlyCollection<StorefrontProductDto>> MapProductsAsync(Guid tenantId, string storeKey, IReadOnlyCollection<Product> products, CancellationToken token)
    {
        if (products.Count == 0) return [];
        var ids = products.Select(product => product.ProductId).ToArray();
        var stock = await db.InventoryBalances.AsNoTracking()
            .Where(balance => EF.Property<Guid?>(balance, "TenantId") == tenantId
                && ids.Contains(balance.ProductId)
                && db.Warehouses.Any(warehouse => warehouse.WarehouseId == balance.WarehouseId
                    && EF.Property<Guid?>(warehouse, "TenantId") == tenantId && warehouse.IsActive && !warehouse.IsDeleted))
            .GroupBy(balance => balance.ProductId)
            .Select(group => new { ProductId = group.Key, Available = group.Sum(balance => balance.QuantityOnHand - balance.QuantityReserved) > 0 })
            .ToDictionaryAsync(row => row.ProductId, row => row.Available, token);
        var imageRows = await db.ProductImages.AsNoTracking()
            .Where(image => image.TenantId == tenantId && ids.Contains(image.ProductId) && image.IsActive && !image.IsDeleted)
            .OrderByDescending(image => image.IsPrimary).ThenBy(image => image.CreatedOn)
            .Select(image => new { image.ProductId, image.ProductImageId }).ToArrayAsync(token);
        var imageIds = imageRows.GroupBy(image => image.ProductId).ToDictionary(group => group.Key, group => group.First().ProductImageId);

        return products.Select(product => new StorefrontProductDto(
            product.ProductId,
            product.CategoryId,
            product.ProductName,
            product.ShortDescription ?? string.Empty,
            imageIds.ContainsKey(product.ProductId) ? $"/api/store/{Uri.EscapeDataString(storeKey)}/products/{product.ProductId}/image" : null,
            product.SellingPrice,
            product.MRP > product.SellingPrice ? product.MRP : null,
            stock.GetValueOrDefault(product.ProductId) ? "IN_STOCK" : "OUT_OF_STOCK",
            UnitLabel(product))).ToArray();
    }

    private async Task<WhatsBiz.Domain.Tenants.Tenant?> ResolveTenantAsync(string storeKey, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(storeKey)) return null;
        var normalized = storeKey.Trim().ToUpperInvariant();
        if (!StoreKeyPattern.IsMatch(normalized)) return null;
        return await db.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.TenantKey == normalized && tenant.IsActive, token);
    }

    private static string? UnitLabel(Product product)
    {
        var unit = string.IsNullOrWhiteSpace(product.Unit.ShortName) ? product.Unit.UnitName : product.Unit.ShortName;
        if (string.IsNullOrWhiteSpace(unit) || unit.Trim().Equals("Unit", StringComparison.OrdinalIgnoreCase)) return null;
        return product.Weight is > 0 ? $"{product.Weight.Value:0.####} {unit.Trim()}" : unit.Trim();
    }

    private static StorefrontPaymentMethodDto ToPaymentMethod(EnabledPaymentMethod method) => method.Provider switch
    {
        PaymentProviders.Razorpay => new(method.Provider, "Pay Online", "UPI, Credit / Debit Card, Net Banking", method.IsDefault, true),
        PaymentProviders.DirectUpi => new(method.Provider, "UPI", "Payment is confirmed by the retailer after verification", method.IsDefault, false),
        PaymentProviders.Cod => new(method.Provider, "Cash on Delivery", "Pay when your order is delivered", method.IsDefault, false),
        _ => throw new InvalidOperationException("Unsupported storefront payment method.")
    };
}
