using System.Text.RegularExpressions;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Products;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontService(ApplicationDbContext db, IProductImageStorage images,
    IStorefrontMediaStorage? presentationImages = null, ICommercePaymentService? payments = null, IFeatureService? features = null, IStorefrontAvailabilityService? availability = null) : IStorefrontService
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
                x.Title, x.Subtitle, x.PromotionId.HasValue ? null : x.TargetUrl, x.DisplayOrder, x.PromotionId)).ToArrayAsync(token);
        var enabled = payments is null ? [] : await payments.GetEnabledMethodsForTenantAsync(tenant.TenantId, token);
        var methods = enabled.Select(ToPaymentMethod).ToArray();
        var customerRewardsEnabled = features is not null && await features.IsEnabledAsync(tenant.TenantId, FeatureKeys.CustomerReferralRewards, token);
        string? orderContactNumber = null;
        if (features is not null && await features.IsEnabledAsync(tenant.TenantId, FeatureKeys.MultiChannelOrderIntake, token))
        {
            // A retailer may accept phone orders even when automated WhatsApp webhook
            // ingestion is disconnected. Expose only its explicitly configured LIVE
            // business number; webhook ingestion keeps its own connected/verified gate.
            var configuredNumber = await db.Database.SqlQuery<string>($"SELECT TOP (1) DisplayPhoneNumber AS [Value] FROM integration.WhatsAppConfigurations WHERE TenantId={tenant.TenantId} AND ProviderMode='LIVE' AND IsEnabled=1 AND DisplayPhoneNumber IS NOT NULL")
                .SingleOrDefaultAsync(token);
            if (!string.IsNullOrWhiteSpace(configuredNumber))
            {
                var normalized = configuredNumber.Trim();
                var hasInternationalPrefix = normalized.StartsWith('+') || normalized.StartsWith("00", StringComparison.Ordinal);
                var digits = Regex.Replace(normalized, "[^0-9]", string.Empty);
                if (hasInternationalPrefix && digits.Length is >= 8 and <= 15) orderContactNumber = "+" + (normalized.StartsWith("00", StringComparison.Ordinal) ? digits[2..] : digits);
            }
        }
        var featuredIds = new[] { configuration?.FeaturedCategory1Id, configuration?.FeaturedCategory2Id }
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var featuredRows = await db.ProductCategories.AsNoTracking()
            .Where(category => featuredIds.Contains(category.ProductCategoryId) && category.IsActive && !category.IsDeleted
                && db.Set<TenantProductCategory>().Any(link => link.TenantId == tenant.TenantId && link.ProductCategoryId == category.ProductCategoryId))
            .Select(category => new { category.ProductCategoryId, category.CategoryName, category.ParentCategoryId,
                HasImage = db.StorefrontCategoryImages.Any(x => x.TenantId == tenant.TenantId && x.ProductCategoryId == category.ProductCategoryId) })
            .ToArrayAsync(token);
        var featured = featuredIds.Select(id => featuredRows.FirstOrDefault(row => row.ProductCategoryId == id))
            .Where(row => row is not null)
            .Select(row => new StorefrontCategoryDto(row!.ProductCategoryId, row.CategoryName,
                row.HasImage ? $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/categories/{row.ProductCategoryId}" : null, row.ParentCategoryId))
            .ToArray();
        return new(tenant.TenantKey.ToLowerInvariant(), string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Name : tenant.DisplayName.Trim(),
            configuration?.Tagline ?? "Good things, close to home.", configuration?.LogoMediaId is null ? null : $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/logo?v={configuration.LogoMediaId.Value:N}",
            configuration?.AccentColor ?? "#145c43", configuration?.DeliveryMessage ?? "Fresh picks, close to home.", configuration?.FreeDeliveryThreshold,
            configuration?.ShowProductRatings ?? true, configuration?.ShowProductReviews ?? true, banners, methods,
            configuration?.AllCategoryMediaId is null ? null : $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/categories/all", customerRewardsEnabled, string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Name : tenant.DisplayName.Trim(), featured, orderContactNumber);
    }

    public async Task<IReadOnlyCollection<StorefrontCategoryDto>?> GetCategoriesAsync(string storeKey, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var categories = await db.ProductCategories.AsNoTracking()
            .Where(category => category.IsActive && !category.IsDeleted)
            .OrderBy(category => category.DisplayOrder).ThenBy(category => category.CategoryName)
            .Select(category => new { Id = category.ProductCategoryId, category.CategoryName, category.ParentCategoryId, category.DisplayOrder,
                HasImage = db.StorefrontCategoryImages.Any(x => x.TenantId == tenant.TenantId && x.ProductCategoryId == category.ProductCategoryId) })
            .ToArrayAsync(token);
        var productCategoryIds = await EligibleProducts(tenant.TenantId).Select(product => product.CategoryId).Distinct().ToArrayAsync(token);
        var byParent = categories.Where(x => x.ParentCategoryId.HasValue)
            .GroupBy(x => x.ParentCategoryId!.Value).ToDictionary(x => x.Key, x => x.ToArray());
        var direct = productCategoryIds.ToHashSet();
        var visible = new HashSet<Guid>();
        bool MarkVisible(Guid id)
        {
            if (!visible.Add(id)) return true;
            var hasProduct = direct.Contains(id);
            var hasChild = byParent.TryGetValue(id, out var children) && children.Any(child => MarkVisible(child.Id));
            if (hasProduct || hasChild) return true;
            visible.Remove(id);
            return false;
        }
        foreach (var category in categories) MarkVisible(category.Id);
        return categories.Where(x => visible.Contains(x.Id)).Select(category => new StorefrontCategoryDto(category.Id, category.CategoryName,
            category.HasImage ? $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/categories/{category.Id}" : null, category.ParentCategoryId))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<StorefrontProductDto>?> GetProductsAsync(string storeKey, CancellationToken token) => (await GetProductsAsync(storeKey, 1, int.MaxValue, null, token))?.Items;
    public Task<StorefrontProductPageDto?> GetProductsAsync(string storeKey, int page, int pageSize, CancellationToken token)
        => GetProductsAsync(storeKey, page, pageSize, null, token);

    public async Task<StorefrontProductPageDto?> GetProductsAsync(string storeKey, int page, int pageSize, Guid? categoryId, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var query = EligibleProducts(tenant.TenantId);
        if (categoryId.HasValue)
        {
            var categoryIds = await DescendantCategoryIdsAsync(categoryId.Value, token);
            query = query.Where(product => categoryIds.Contains(product.CategoryId));
        }
        var ordered = query.Include(product => product.Category).Include(product => product.Unit).Include(product => product.Specifications).OrderBy(product => product.ProductName); var total = await ordered.CountAsync(token); var products = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token); var items = await MapProductsAsync(tenant.TenantId, tenant.TenantKey, products, false, token); return new StorefrontProductPageDto(items, page, pageSize, total, page * pageSize < total);
    }

    private async Task<Guid[]> DescendantCategoryIdsAsync(Guid rootId, CancellationToken token)
    {
        var rows = await db.ProductCategories.AsNoTracking().Where(category => category.IsActive && !category.IsDeleted)
            .Select(category => new { category.ProductCategoryId, category.ParentCategoryId }).ToArrayAsync(token);
        var children = rows.Where(x => x.ParentCategoryId.HasValue)
            .GroupBy(x => x.ParentCategoryId!.Value).ToDictionary(x => x.Key, x => x.Select(y => y.ProductCategoryId).ToArray());
        var result = new HashSet<Guid>();
        void Visit(Guid id) { if (!result.Add(id)) return; if (children.TryGetValue(id, out var nested)) foreach (var child in nested) Visit(child); }
        Visit(rootId); return result.ToArray();
    }

    public async Task<StorefrontProductDto?> GetProductAsync(string storeKey, Guid productId, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var product = await EligibleProducts(tenant.TenantId).Include(item => item.Category).Include(item => item.Unit).Include(item => item.Specifications)
            .SingleOrDefaultAsync(item => item.ProductId == productId, token);
        if (product is null) return null;
        return (await MapProductsAsync(tenant.TenantId, tenant.TenantKey, [product], true, token)).Single();
    }

    public async Task<StorefrontOfferDto?> GetOfferAsync(string storeKey, Guid offerId, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null) return null;
        var offer = await db.StorefrontPromotions.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.PromotionId == offerId && !x.IsDeleted && x.IsActive, token);
        if (offer is null) return null;
        var now = DateTimeOffset.UtcNow;
        var bannerSlot = await db.StorefrontBanners.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.PromotionId == offerId && x.MediaId != null).OrderBy(x => x.DisplayOrder).Select(x => x.Slot).FirstOrDefaultAsync(token);
        var status = offer.StartsAt.HasValue && offer.StartsAt.Value > now ? "UPCOMING" : offer.EndsAt.HasValue && offer.EndsAt.Value < now ? "EXPIRED" : "ACTIVE";
        var benefit = offer.DiscountType == "PERCENTAGE" ? $"{offer.DiscountValue:0.##}% off" : $"₹{offer.DiscountValue:0.00} off";
        if (offer.MaximumDiscount.HasValue) benefit += $", up to ₹{offer.MaximumDiscount.Value:0.00}";
        return new(offer.PromotionId, offer.OfferName, bannerSlot is null ? null : $"/api/store/{Uri.EscapeDataString(tenant.TenantKey)}/presentation/banners/{bannerSlot.ToLowerInvariant()}", offer.ShortDescription, offer.DetailedDescription, offer.PromoCode, benefit, offer.StartsAt, offer.EndsAt, offer.MinimumPurchaseAmount, offer.EligibleItemsDescription, offer.MaximumDiscount, offer.UsageLimitPerCustomer, offer.TermsAndConditions, offer.CtaLabel, status);
    }
    public async Task<StorefrontImage?> GetProductImageAsync(string storeKey, Guid productId, Guid? imageId, bool thumbnail, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null || !await EligibleProducts(tenant.TenantId).AnyAsync(product => product.ProductId == productId, token)) return null;
        var image = await db.ProductImages.AsNoTracking()
            .Where(row => row.TenantId == tenant.TenantId && row.ProductId == productId && row.IsActive && !row.IsDeleted && (!imageId.HasValue || row.ProductImageId == imageId.Value))
            .OrderByDescending(row => row.IsPrimary).ThenBy(row => row.CreatedOn).FirstOrDefaultAsync(token);
        if (image is null) return null;
        var content = await images.ReadAsync(new(image.TenantId, image.StorageProvider, thumbnail ? image.ThumbnailObjectKey : image.ObjectKey, thumbnail ? image.ThumbnailData : image.ImageData, thumbnail ? image.ThumbnailContentType : image.ContentType), token);
        return content is null ? null : new(content.ContentType, content.Content);
    }

    public async Task<StorefrontImage?> GetPresentationImageAsync(string storeKey, string resource, Guid? categoryId, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        if (tenant is null || presentationImages is null) return null;
        Guid? mediaId = resource.ToLowerInvariant() switch
        {
            "logo" => await db.StorefrontConfigurations.AsNoTracking().Where(x => x.TenantId == tenant.TenantId).Select(x => x.LogoMediaId).SingleOrDefaultAsync(token),
            "category-all" => await db.StorefrontConfigurations.AsNoTracking().Where(x => x.TenantId == tenant.TenantId).Select(x => x.AllCategoryMediaId).SingleOrDefaultAsync(token),
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

    public async Task<StorefrontImage?> GetPwaIconAsync(string storeKey, int size, CancellationToken token)
    {
        if (size is not (192 or 512)) return null;
        var logo = await GetPresentationImageAsync(storeKey, "logo", null, token);
        if (logo is null) return null;
        if (logo.Content.Length == 0 || logo.Content.Length > ProductImageOptimizer.MaxUploadBytes) return null;
        ImageMagickSafety.EnsureInitialized();
        try
        {
            var info = new MagickImageInfo(logo.Content);
            if (info.Format is not (MagickFormat.Jpeg or MagickFormat.Png or MagickFormat.WebP)) return null;
            using var icon = new MagickImage(logo.Content);
            icon.AutoOrient();
            icon.Resize(new MagickGeometry((uint)size, (uint)size));
            icon.BackgroundColor = MagickColors.White;
            icon.Extent((uint)size, (uint)size, Gravity.Center, MagickColors.White);
            icon.Quality = 90;
            token.ThrowIfCancellationRequested();
            return new StorefrontImage("image/webp", icon.ToByteArray(MagickFormat.WebP));
        }
        catch (MagickException) { return null; }
    }
    private IQueryable<Guid?> EligibleBannerMedia(Guid tenantId, string slot)
    {
        var now = DateTimeOffset.UtcNow;
        return db.StorefrontBanners.AsNoTracking().Where(x => x.TenantId == tenantId && x.Slot == slot && x.IsEnabled && x.MediaId != null
            && (x.StartsAt == null || x.StartsAt <= now) && (x.EndsAt == null || x.EndsAt >= now)).Select(x => x.MediaId);
    }

    private IQueryable<Product> EligibleProducts(Guid tenantId) => db.Products.AsNoTracking()
        .Where(product => product.TenantId == tenantId && product.IsActive && !product.IsDeleted && product.IsWhatsAppVisible
            && ApplicationDbContext.IsProductPubliclyApproved(tenantId, product.ProductId)
            && (!db.PartnerProductListings.Any(x => x.OperatingTenantId == tenantId && x.ProductId == product.ProductId)
                || db.PartnerProductListings.Any(x => x.OperatingTenantId == tenantId && x.ProductId == product.ProductId && x.ListingStatus == "APPROVED"
                    && db.PartnerSellers.Any(s => s.OperatingTenantId == tenantId && s.PartnerSellerId == x.PartnerSellerId && s.SellerStatus == "ACTIVE" && s.VerificationStatus == "VERIFIED")))
            && db.Set<TenantProductCategory>().Any(link => link.TenantId == tenantId && link.ProductCategoryId == product.CategoryId)
            && db.ProductCategories.Any(category => category.ProductCategoryId == product.CategoryId && category.IsActive && !category.IsDeleted)
            && db.Set<TenantUnitOfMeasure>().Any(link => link.TenantId == tenantId && link.UnitId == product.UnitId)
            && db.UnitsOfMeasure.Any(unit => unit.UnitId == product.UnitId && unit.IsActive && !unit.IsDeleted));

    private async Task<IReadOnlyCollection<StorefrontProductDto>> MapProductsAsync(Guid tenantId, string storeKey, IReadOnlyCollection<Product> products, bool includeGallery, CancellationToken token)
    {
        if (products.Count == 0) return []; var defaultDays = await db.StorefrontConfigurations.AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => (int?)x.DefaultReturnWindowDays).SingleOrDefaultAsync(token) ?? 7;
        var ids = products.Select(product => product.ProductId).ToArray();
        var stock = await db.InventoryBalances.AsNoTracking()
            .Where(balance => EF.Property<Guid?>(balance, "TenantId") == tenantId
                && ids.Contains(balance.ProductId)
                && db.Warehouses.Any(warehouse => warehouse.WarehouseId == balance.WarehouseId
                    && EF.Property<Guid?>(warehouse, "TenantId") == tenantId && warehouse.IsActive && !warehouse.IsDeleted))
            .GroupBy(balance => balance.ProductId)
            .Select(group => new { ProductId = group.Key, Available = group.Sum(balance => balance.QuantityOnHand - balance.QuantityReserved) > 0 })
            .ToDictionaryAsync(row => row.ProductId, row => row.Available, token);
        var showRatings = await db.StorefrontConfigurations.AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => (bool?)x.ShowProductRatings).SingleOrDefaultAsync(token) ?? true;
        var ratings = await db.StorefrontProductReviews.AsNoTracking()
            .Where(x => showRatings && x.TenantId == tenantId && ids.Contains(x.ProductId) && x.Status == "PUBLISHED")
            .GroupBy(x => x.ProductId).Select(x => new { ProductId = x.Key, Average = x.Average(r => (decimal)r.Rating), Count = x.Count() })
            .ToDictionaryAsync(x => x.ProductId, token);
        var imageRows = await db.ProductImages.AsNoTracking()
            .Where(image => image.TenantId == tenantId && ids.Contains(image.ProductId) && image.IsActive && !image.IsDeleted)
            .OrderByDescending(image => image.IsPrimary).ThenBy(image => image.CreatedOn)
            .Select(image => new { image.ProductId, image.ProductImageId, image.IsPrimary }).ToArrayAsync(token);
        var imageIds = imageRows.GroupBy(image => image.ProductId).ToDictionary(group => group.Key, group => group.ToArray());
        var externalImages = await db.ProductExternalImages.AsNoTracking().Where(image => image.TenantId == tenantId && ids.Contains(image.ProductId))
            .Select(image => new { image.ExternalImageId, image.ProductId, image.ImageUrl, image.IsPrimary, image.DisplayOrder }).ToArrayAsync(token);
        var externalByProduct = externalImages.GroupBy(image => image.ProductId).ToDictionary(group => group.Key,
            group => group.Select(image => new ExternalStorefrontProductImage(image.ExternalImageId, image.ImageUrl, image.DisplayOrder, image.IsPrimary)).ToArray());
        var partnerRows = await (from listing in db.PartnerProductListings.AsNoTracking()
            join seller in db.PartnerSellers.AsNoTracking() on new { listing.OperatingTenantId, listing.PartnerSellerId } equals new { seller.OperatingTenantId, seller.PartnerSellerId }
            where listing.OperatingTenantId == tenantId && ids.Contains(listing.ProductId) && listing.ListingStatus == "APPROVED" && seller.SellerStatus == "ACTIVE" && seller.VerificationStatus == "VERIFIED"
            select new { listing.ProductId, seller.PartnerSellerId, seller.SellerName }).ToDictionaryAsync(x=>x.ProductId,token);
        var scheduled = availability is null ? new Dictionary<Guid, StorefrontProductAvailability>() : await availability.GetProductAvailabilityAsync(tenantId, ids, token);

        return products.Select(product => {
            imageIds.TryGetValue(product.ProductId, out var gallery);
            externalByProduct.TryGetValue(product.ProductId, out var externalGallery);
            var projectedGallery = StorefrontProductImageProjection.Project(storeKey, product.ProductId,
                externalGallery ?? [], (gallery ?? []).Select(image => new ManagedStorefrontProductImage(image.ProductImageId, image.IsPrimary)));
            var galleryDtos = includeGallery ? projectedGallery.Images : null;
            return new StorefrontProductDto(
            product.ProductId,
            product.CategoryId,
            product.ProductName,
            product.ShortDescription ?? string.Empty,
            product.LongDescription,
            product.Specifications.Where(x => x.IsActive && !x.IsDeleted).OrderBy(x => x.DisplayOrder).Select(x => new StorefrontProductSpecificationDto(x.Name, x.Value)).ToArray(),
            projectedGallery.PrimaryUrl,
            product.SellingPrice,
            product.MRP > product.SellingPrice ? product.MRP : null,
            scheduled.TryGetValue(product.ProductId, out var timing) ? (stock.GetValueOrDefault(product.ProductId) ? timing.Status : "OUT_OF_STOCK") : (stock.GetValueOrDefault(product.ProductId) ? "IN_STOCK" : "OUT_OF_STOCK"),
            product.PackSize, ratings.TryGetValue(product.ProductId, out var rating) ? decimal.Round(rating.Average, 1) : null,
            ratings.TryGetValue(product.ProductId, out rating) ? rating.Count : 0, product.ReturnPolicyMode, product.ReturnPolicyMode == "NON_RETURNABLE" ? null : product.ReturnPolicyMode == "CUSTOM" ? product.ReturnWindowDays : defaultDays, product.ReturnPolicyMode != "NON_RETURNABLE",
            galleryDtos, product.ReturnPolicyDescription, product.GSTPercentage, product.GstPricingType,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.CustomerMessage : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? (timing.OrderWindowLabel is null ? timing.DeliveryWindowLabel : $"Orders {timing.OrderWindowLabel} · Delivery {timing.DeliveryWindowLabel}") : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.DeliveryWindowLabel : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.NextTransitionAt : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.CanOrderNow : true,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.Reason : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.NextOrderOpenAtUtc : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.NextOrderCloseAtUtc : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.ServerNowUtc : null,
            scheduled.TryGetValue(product.ProductId, out timing) ? timing.TimeZoneId : null,
            !partnerRows.ContainsKey(product.ProductId),
            partnerRows.TryGetValue(product.ProductId,out var partner) ? partner.PartnerSellerId : null,
            partnerRows.TryGetValue(product.ProductId,out partner) ? partner.SellerName : null,
            partnerRows.ContainsKey(product.ProductId) ? "Partner product orders are not available yet." : null);
        }).ToArray();
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
        var weightUnit = product.Specifications.FirstOrDefault(specification => specification.IsActive && !specification.IsDeleted
            && string.Equals(specification.Name, ShopifyProductWeightProjection.WeightUnitSpecificationName, StringComparison.OrdinalIgnoreCase))?.Value;
        if (product.Weight is > 0 && !string.IsNullOrWhiteSpace(weightUnit))
            return $"{product.Weight.Value:0.####} {weightUnit.Trim()} · {unit.Trim()}";
        return product.Weight is > 0 ? $"{product.Weight.Value:0.####} {unit.Trim()}" : unit.Trim();
    }

    private static StorefrontPaymentMethodDto ToPaymentMethod(EnabledPaymentMethod method) => method.Provider switch
    {
        PaymentProviders.Razorpay => new(method.Code, method.Label, "Secure online payment", method.IsDefault, true, method.Provider),
        PaymentProviders.Cod => new(method.Code, method.Label, " (Pay when your order is delivered)", method.IsDefault, false, method.Provider),
        _ => throw new InvalidOperationException("Unsupported Storefront payment provider.")
    };
}
