using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontService(ApplicationDbContext db, IProductImageStorage images) : IStorefrontService
{
    private static readonly Regex StoreKeyPattern = new("^[A-Z0-9_-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<StorefrontStoreDto?> GetStoreAsync(string storeKey, CancellationToken token)
    {
        var tenant = await ResolveTenantAsync(storeKey, token);
        return tenant is null ? null : ToStore(tenant);
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
            .Select(category => new StorefrontCategoryDto(category.ProductCategoryId, category.CategoryName))
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
            string.IsNullOrWhiteSpace(product.Unit.ShortName) ? product.Unit.UnitName : product.Unit.ShortName)).ToArray();
    }

    private async Task<WhatsBiz.Domain.Tenants.Tenant?> ResolveTenantAsync(string storeKey, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(storeKey)) return null;
        var normalized = storeKey.Trim().ToUpperInvariant();
        if (!StoreKeyPattern.IsMatch(normalized)) return null;
        return await db.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.TenantKey == normalized && tenant.IsActive, token);
    }

    private static StorefrontStoreDto ToStore(WhatsBiz.Domain.Tenants.Tenant tenant) =>
        new(tenant.TenantKey.ToLowerInvariant(), tenant.Name, "Good things, close to home.", null, "#145c43", "Fresh picks, close to home.");
}
