using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontAdministrationService(ApplicationDbContext db, ICurrentUserService currentUser,
    IProductImageOptimizer optimizer, IStorefrontMediaStorage storage) : IStorefrontAdministrationService
{
    private Guid Tenant => currentUser.TenantId ?? throw new Application.Common.Exceptions.UnauthorizedAccessException("A tenant context is required.");

    public async Task<StorefrontAdminDto> GetAsync(CancellationToken token)
    {
        var tenant = Tenant;
        var name = await db.Tenants.Where(x => x.TenantId == tenant).Select(x => x.Name).SingleAsync(token);
        var config = await db.StorefrontConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant, token);
        var rows = await db.StorefrontBanners.AsNoTracking().Where(x => x.TenantId == tenant).ToDictionaryAsync(x => x.Slot, token);
        var banners = StorefrontBannerSlots.All.Select(slot => rows.TryGetValue(slot, out var row)
            ? new StorefrontBannerAdminDto(slot, row.IsEnabled, row.StartsAt, row.EndsAt, row.Title, row.Subtitle, row.TargetUrl, row.DisplayOrder,
                row.MediaId is null ? null : $"/api/storefront-administration/banners/{slot}/image")
            : new StorefrontBannerAdminDto(slot, false, null, null, null, null, null, slot == StorefrontBannerSlots.Primary ? 1 : 2, null)).ToArray();
        return new(name, config?.LogoMediaId is null ? null : "/api/storefront-administration/logo", banners);
    }

    public async Task UpdateBannerAsync(string slot, UpdateStorefrontBannerInput input, CancellationToken token)
    {
        var normalized = NormalizeSlot(slot);
        if (input.StartsAt.HasValue && input.EndsAt.HasValue && input.StartsAt > input.EndsAt)
            throw new BusinessRuleException("Banner end time must be after its start time.");
        var target = NormalizeTarget(input.TargetUrl);
        var tenant = Tenant;
        var row = await db.StorefrontBanners.SingleOrDefaultAsync(x => x.TenantId == tenant && x.Slot == normalized, token);
        if (row is null)
        {
            row = new() { BannerId = Guid.NewGuid(), TenantId = tenant, Slot = normalized, CreatedAt = DateTime.UtcNow };
            db.StorefrontBanners.Add(row);
        }
        row.IsEnabled = input.IsEnabled; row.StartsAt = input.StartsAt; row.EndsAt = input.EndsAt;
        row.Title = Trim(input.Title, 150); row.Subtitle = Trim(input.Subtitle, 300); row.TargetUrl = target;
        row.DisplayOrder = Math.Clamp(input.DisplayOrder, 0, 100); row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
    }

    public async Task<StorefrontImage?> GetImageAsync(string resource, Guid? categoryId, CancellationToken token)
    {
        var tenant = Tenant; var mediaId = await ResolveMediaId(tenant, resource, categoryId, token);
        if (mediaId is null) return null;
        var media = await db.StorefrontMedia.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.MediaId == mediaId, token);
        if (media is null) return null;
        var content = await storage.ReadStorefrontAsync(new(tenant, media.StorageProvider, media.ObjectKey, media.ImageData ?? [], media.ContentType), token);
        return content is null ? null : new(content.ContentType, content.Content);
    }

    public async Task UploadImageAsync(string resource, Guid? categoryId, string fileName, Stream content, CancellationToken token)
    {
        var tenant = Tenant; var normalized = NormalizeResource(resource); await ValidateCategory(tenant, normalized, categoryId, token);
        await using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, token);
        var optimized = await optimizer.OptimizeAsync(fileName, null, buffer.ToArray(), token);
        var id = Guid.NewGuid();
        var stored = await storage.StoreStorefrontAsync(new(tenant, normalized, id, optimized.CatalogData, optimized.ThumbnailData, optimized.ContentType), token);
        var database = stored.Provider.Equals(ProductImageStorageProviders.Database, StringComparison.OrdinalIgnoreCase);
        var media = new StorefrontMedia { MediaId = id, TenantId = tenant, ResourceType = normalized, FileName = optimized.FileName,
            ContentType = optimized.ContentType, ThumbnailContentType = "image/webp", StorageProvider = stored.Provider,
            ObjectKey = stored.ObjectKey, ThumbnailObjectKey = stored.ThumbnailObjectKey,
            ImageData = database ? optimized.CatalogData : null, ThumbnailData = database ? optimized.ThumbnailData : null,
            ContentHash = stored.ContentHash, CreatedAt = DateTime.UtcNow };
        db.StorefrontMedia.Add(media);
        try
        {
            await db.SaveChangesAsync(token);
            var oldId = await Attach(tenant, normalized, categoryId, id, token);
            if (oldId.HasValue && oldId != id) await DeleteMedia(tenant, oldId.Value, token);
        }
        catch
        {
            await storage.DeleteStorefrontAsync(new(tenant, stored.Provider, stored.ObjectKey, stored.ThumbnailObjectKey), CancellationToken.None);
            throw;
        }
    }

    public async Task RemoveImageAsync(string resource, Guid? categoryId, CancellationToken token)
    {
        var tenant = Tenant; var normalized = NormalizeResource(resource); await ValidateCategory(tenant, normalized, categoryId, token);
        var old = await Attach(tenant, normalized, categoryId, null, token);
        if (old.HasValue) await DeleteMedia(tenant, old.Value, token);
    }

    private async Task<Guid?> Attach(Guid tenant, string resource, Guid? categoryId, Guid? mediaId, CancellationToken token)
    {
        Guid? old;
        if (resource == "logo")
        {
            var row = await db.StorefrontConfigurations.SingleOrDefaultAsync(x => x.TenantId == tenant, token);
            if (row is null) { row = new() { TenantId = tenant, CreatedAt = DateTime.UtcNow }; db.StorefrontConfigurations.Add(row); }
            old = row.LogoMediaId; row.LogoMediaId = mediaId; row.UpdatedAt = DateTime.UtcNow;
        }
        else if (resource == "category")
        {
            var row = await db.StorefrontCategoryImages.SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProductCategoryId == categoryId!.Value, token);
            old = row?.MediaId;
            if (mediaId is null && row is not null) db.StorefrontCategoryImages.Remove(row);
            else if (row is null && mediaId.HasValue) db.StorefrontCategoryImages.Add(new() { TenantId = tenant, ProductCategoryId = categoryId!.Value, MediaId = mediaId.Value, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            else if (row is not null && mediaId.HasValue) { row.MediaId = mediaId.Value; row.UpdatedAt = DateTime.UtcNow; }
        }
        else
        {
            var slot = resource == "banner-primary" ? StorefrontBannerSlots.Primary : StorefrontBannerSlots.Secondary;
            var row = await db.StorefrontBanners.SingleOrDefaultAsync(x => x.TenantId == tenant && x.Slot == slot, token);
            if (row is null) { row = new() { BannerId = Guid.NewGuid(), TenantId = tenant, Slot = slot, CreatedAt = DateTime.UtcNow, DisplayOrder = slot == StorefrontBannerSlots.Primary ? 1 : 2 }; db.StorefrontBanners.Add(row); }
            old = row.MediaId; row.MediaId = mediaId; row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(token); return old;
    }

    private async Task DeleteMedia(Guid tenant, Guid mediaId, CancellationToken token)
    {
        var row = await db.StorefrontMedia.SingleOrDefaultAsync(x => x.TenantId == tenant && x.MediaId == mediaId, token); if (row is null) return;
        db.StorefrontMedia.Remove(row); await db.SaveChangesAsync(token);
        await storage.DeleteStorefrontAsync(new(tenant, row.StorageProvider, row.ObjectKey, row.ThumbnailObjectKey), token);
    }
    private async Task<Guid?> ResolveMediaId(Guid tenant, string resource, Guid? categoryId, CancellationToken token)
    {
        var normalized = NormalizeResource(resource); await ValidateCategory(tenant, normalized, categoryId, token);
        if (normalized == "logo") return await db.StorefrontConfigurations.Where(x => x.TenantId == tenant).Select(x => x.LogoMediaId).SingleOrDefaultAsync(token);
        if (normalized == "category") return await db.StorefrontCategoryImages.Where(x => x.TenantId == tenant && x.ProductCategoryId == categoryId).Select(x => (Guid?)x.MediaId).SingleOrDefaultAsync(token);
        var slot = normalized == "banner-primary" ? StorefrontBannerSlots.Primary : StorefrontBannerSlots.Secondary;
        return await db.StorefrontBanners.Where(x => x.TenantId == tenant && x.Slot == slot).Select(x => x.MediaId).SingleOrDefaultAsync(token);
    }
    private async Task ValidateCategory(Guid tenant, string resource, Guid? categoryId, CancellationToken token)
    {
        if (resource != "category") return;
        if (!categoryId.HasValue || !await db.Set<TenantProductCategory>().AnyAsync(x => x.TenantId == tenant && x.ProductCategoryId == categoryId, token))
            throw new EntityNotFoundException("Category was not found.");
    }
    private static string NormalizeResource(string resource) { var value = resource.Trim().ToLowerInvariant(); if (value is not ("logo" or "category" or "banner-primary" or "banner-secondary")) throw new EntityNotFoundException("Storefront image was not found."); return value; }
    private static string NormalizeSlot(string slot) { var value = slot.Trim().ToUpperInvariant(); if (!StorefrontBannerSlots.All.Contains(value)) throw new EntityNotFoundException("Banner slot was not found."); return value; }
    private static string? Trim(string? value, int max) { var text = value?.Trim(); if (string.IsNullOrEmpty(text)) return null; if (text.Length > max) throw new BusinessRuleException($"Text cannot exceed {max} characters."); return text; }
    private static string? NormalizeTarget(string? value) { var text = Trim(value, 500); if (text is null) return null; if (text[0] == '/' && !text.StartsWith("//", StringComparison.Ordinal)) return text; if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return uri.AbsoluteUri; throw new BusinessRuleException("Banner target must be a relative store path or an HTTPS URL."); }
}
