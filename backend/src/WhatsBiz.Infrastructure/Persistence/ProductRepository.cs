using System.Data;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class ProductRepository(ApplicationDbContext context, ICurrentUserService currentUser) : IProductRepository
{
    private Guid Tenant => currentUser.TenantId ?? throw new UnauthorizedAccessException("Tenant context is required.");
    private IQueryable<Product> TenantProducts => currentUser.TenantId is Guid tenantId ? context.Products.Where(x => x.TenantId == tenantId) : context.Products.Where(_ => false);
    private IQueryable<ProductCategory> TenantCategories => context.ProductCategories.Where(x => context.Set<TenantProductCategory>().Any(v => v.TenantId == Tenant && v.ProductCategoryId == x.ProductCategoryId));
    private IQueryable<Brand> TenantBrands => context.Brands.Where(x => context.Set<TenantBrand>().Any(v => v.TenantId == Tenant && v.BrandId == x.BrandId));
    private IQueryable<UnitOfMeasure> TenantUnits => context.UnitsOfMeasure.Where(x => context.Set<TenantUnitOfMeasure>().Any(v => v.TenantId == Tenant && v.UnitId == x.UnitId));
    public async Task<(IReadOnlyCollection<Product> Items, int TotalCount)> SearchAsync(string? search, bool? isActive, string sortBy, bool descending, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = TenantProducts.AsNoTracking().Include(x => x.Category).Include(x => x.Brand).Include(x => x.Unit).Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.ProductCode.Contains(term) || x.ProductName.Contains(term) || (x.Barcode != null && x.Barcode.Contains(term))); }
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive);
        query = (sortBy.ToLowerInvariant(), descending) switch { ("createdon", false) => query.OrderBy(x => x.CreatedOn), ("createdon", true) => query.OrderByDescending(x => x.CreatedOn), ("productcode", false) => query.OrderBy(x => x.ProductCode), ("productcode", true) => query.OrderByDescending(x => x.ProductCode), ("sellingprice", false) => query.OrderBy(x => x.SellingPrice), ("sellingprice", true) => query.OrderByDescending(x => x.SellingPrice), ("categoryname", false) => query.OrderBy(x => x.Category.CategoryName), ("categoryname", true) => query.OrderByDescending(x => x.Category.CategoryName), (_, true) => query.OrderByDescending(x => x.ProductName), _ => query.OrderBy(x => x.ProductName) };
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return (items, count);
    }

    public Task<Product?> GetAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = TenantProducts.Include(x => x.Category).Include(x => x.Brand).Include(x => x.Unit).Include(x => x.Barcodes).Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.ProductId == id, cancellationToken); }
    public async Task<IReadOnlyCollection<ProductHistoryDto>> GetHistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = new List<ProductHistoryDto>();
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT AuditLogId, UserName, Action, RequestPath, HttpMethod, Succeeded, OccurredOn
                FROM admin.AuditLogs
                WHERE RequestPath = @productPath OR RequestPath LIKE @assetPath
                ORDER BY OccurredOn DESC
                """;
            var productPath = command.CreateParameter();
            productPath.ParameterName = "@productPath";
            productPath.DbType = DbType.String;
            productPath.Value = $"/api/products/{id}";
            command.Parameters.Add(productPath);
            var assetPath = command.CreateParameter();
            assetPath.ParameterName = "@assetPath";
            assetPath.DbType = DbType.String;
            assetPath.Value = $"/api/products/{id}/%";
            command.Parameters.Add(assetPath);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var path = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var method = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                var (action, details) = DescribeHistory(method, path);
                rows.Add(new ProductHistoryDto(reader.GetInt64(0), action, details, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(5), reader.GetFieldValue<DateTimeOffset>(6)));
            }
        }
        finally
        {
            if (closeConnection) await connection.CloseAsync();
        }
        return rows;
    }

    private static (string Action, string Details) DescribeHistory(string method, string path)
    {
        var imageAction = path.Contains("/image", StringComparison.OrdinalIgnoreCase);
        return (method.ToUpperInvariant(), imageAction) switch
        {
            ("POST", true) => ("IMAGE_ADDED", "Product image added."),
            ("DELETE", true) => ("IMAGE_DELETED", "Product image deleted."),
            ("PUT" or "PATCH", _) => ("UPDATED", "Product details updated."),
            ("DELETE", _) => ("DELETED", "Product deleted."),
            _ => ("CHANGED", "Product changed.")
        };
    }
    public Task<bool> ProductCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) => TenantProducts.AnyAsync(x => !x.IsDeleted && x.ProductCode == code.Trim() && (!excludingId.HasValue || x.ProductId != excludingId), cancellationToken);
    public Task<bool> BarcodeExistsAsync(string barcode, Guid? excludingId, CancellationToken cancellationToken)
    {
        var value = barcode.Trim();
        return TenantProducts.AnyAsync(
            x => !x.IsDeleted
                && (!excludingId.HasValue || x.ProductId != excludingId)
                && (x.Barcode == value || context.ProductBarcodes.Any(b => b.TenantId == x.TenantId && b.ProductId == x.ProductId && b.Barcode == value && b.IsActive && !b.IsDeleted)),
            cancellationToken);
    }
    public Task<Product?> IdentifierOwnerAsync(string identifier, Guid? excludingProductId, CancellationToken cancellationToken)
    {
        var value = identifier;
        return TenantProducts.AsNoTracking().FirstOrDefaultAsync(
            x => !x.IsDeleted
                && (!excludingProductId.HasValue || x.ProductId != excludingProductId)
                && (x.Barcode == value || context.ProductBarcodes.Any(b => b.TenantId == x.TenantId && b.ProductId == x.ProductId && b.Barcode == value && b.IsActive && !b.IsDeleted)),
            cancellationToken);
    }
    public async Task<bool> ReferencesExistAsync(Guid categoryId, Guid brandId, Guid unitId, CancellationToken cancellationToken) => await TenantCategories.AnyAsync(x => x.ProductCategoryId == categoryId && x.IsActive && !x.IsDeleted, cancellationToken) && await TenantBrands.AnyAsync(x => x.BrandId == brandId && x.IsActive && !x.IsDeleted, cancellationToken) && await TenantUnits.AnyAsync(x => x.UnitId == unitId && x.IsActive && !x.IsDeleted, cancellationToken);
    public void Add(Product product) => context.Products.Add(product);
    public void Add(ProductBarcode barcode) => context.ProductBarcodes.Add(barcode);
    public async Task<IReadOnlyCollection<ProductCategory>> GetCategoriesAsync(CancellationToken cancellationToken) => await TenantCategories.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.DisplayOrder).ThenBy(x => x.CategoryName).ToArrayAsync(cancellationToken);
    public Task<ProductCategory?> GetCategoryAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = TenantCategories.Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.ProductCategoryId == id, cancellationToken); }
    public Task<bool> CategoryCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) => TenantCategories.AnyAsync(x => !x.IsDeleted && x.CategoryCode == code.Trim() && (!excludingId.HasValue || x.ProductCategoryId != excludingId), cancellationToken);
    public async Task<bool> CategoryHasChildrenOrProductsAsync(Guid id, CancellationToken cancellationToken) => await TenantCategories.AnyAsync(x => !x.IsDeleted && x.ParentCategoryId == id, cancellationToken) || await TenantProducts.AnyAsync(x => !x.IsDeleted && x.CategoryId == id, cancellationToken);
    public void Add(ProductCategory category) { var existing = context.ProductCategories.Local.FirstOrDefault(x => x.CategoryCode == category.CategoryCode) ?? context.ProductCategories.FirstOrDefault(x => x.CategoryCode == category.CategoryCode && !x.IsDeleted); if (existing is null) context.ProductCategories.Add(category); else { category.ProductCategoryId = existing.ProductCategoryId; category.CategoryName = existing.CategoryName; category.Description = existing.Description; category.DisplayOrder = existing.DisplayOrder; category.ParentCategoryId = existing.ParentCategoryId; category.IsActive = existing.IsActive; } context.Set<TenantProductCategory>().Add(new() { TenantId = Tenant, ProductCategoryId = category.ProductCategoryId }); }
    public async Task<IReadOnlyCollection<Brand>> GetBrandsAsync(CancellationToken cancellationToken) => await TenantBrands.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.BrandName).ToArrayAsync(cancellationToken);
    public Task<Brand?> GetBrandAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = TenantBrands.Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.BrandId == id, cancellationToken); }
    public Task<bool> BrandCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) => TenantBrands.AnyAsync(x => !x.IsDeleted && x.BrandCode == code.Trim() && (!excludingId.HasValue || x.BrandId != excludingId), cancellationToken);
    public Task<bool> BrandHasProductsAsync(Guid id, CancellationToken cancellationToken) => TenantProducts.AnyAsync(x => !x.IsDeleted && x.BrandId == id, cancellationToken);
    public void Add(Brand brand) { var existing = context.Brands.Local.FirstOrDefault(x => x.BrandCode == brand.BrandCode) ?? context.Brands.FirstOrDefault(x => x.BrandCode == brand.BrandCode && !x.IsDeleted); if (existing is null) context.Brands.Add(brand); else { brand.BrandId = existing.BrandId; brand.BrandName = existing.BrandName; brand.Description = existing.Description; brand.Logo = existing.Logo; brand.IsActive = existing.IsActive; } context.Set<TenantBrand>().Add(new() { TenantId = Tenant, BrandId = brand.BrandId }); }
    public async Task<IReadOnlyCollection<UnitOfMeasure>> GetUnitsAsync(CancellationToken cancellationToken) => await TenantUnits.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.UnitName).ToArrayAsync(cancellationToken);
    public Task<UnitOfMeasure?> GetUnitAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = TenantUnits.Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.UnitId == id, cancellationToken); }
    public Task<bool> UnitCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) => TenantUnits.AnyAsync(x => !x.IsDeleted && x.UnitCode == code.Trim() && (!excludingId.HasValue || x.UnitId != excludingId), cancellationToken);
    public Task<bool> UnitHasProductsAsync(Guid id, CancellationToken cancellationToken) => TenantProducts.AnyAsync(x => !x.IsDeleted && x.UnitId == id, cancellationToken);
    public void Add(UnitOfMeasure unit) { var existing = context.UnitsOfMeasure.Local.FirstOrDefault(x => x.UnitCode == unit.UnitCode) ?? context.UnitsOfMeasure.FirstOrDefault(x => x.UnitCode == unit.UnitCode && !x.IsDeleted); if (existing is null) context.UnitsOfMeasure.Add(unit); else { unit.UnitId = existing.UnitId; unit.UnitName = existing.UnitName; unit.ShortName = existing.ShortName; unit.DecimalPlaces = existing.DecimalPlaces; unit.IsActive = existing.IsActive; } context.Set<TenantUnitOfMeasure>().Add(new() { TenantId = Tenant, UnitId = unit.UnitId }); }
    private IQueryable<ProductImage> TenantImages => currentUser.TenantId is Guid tenantId ? context.ProductImages.Where(x => x.TenantId == tenantId) : context.ProductImages.Where(_ => false);
    public Task<ProductImage?> GetImageAsync(Guid productId, bool tracking, CancellationToken cancellationToken) { var query = TenantImages.Where(x => x.ProductId == productId && !x.IsDeleted && x.IsActive); if (!tracking) query = query.AsNoTracking(); return query.OrderByDescending(x => x.IsPrimary).FirstOrDefaultAsync(cancellationToken); }
    public async Task<IReadOnlyCollection<ProductImage>> GetImagesAsync(Guid productId, bool tracking, CancellationToken cancellationToken) { IQueryable<ProductImage> query = TenantImages.Where(x => x.ProductId == productId && !x.IsDeleted && x.IsActive).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.CreatedOn); if (!tracking) query = query.AsNoTracking(); return await query.ToArrayAsync(cancellationToken); }
    public Task<ProductImage?> GetImageByIdAsync(Guid productId, Guid imageId, bool tracking, CancellationToken cancellationToken) { var query = TenantImages.Where(x => x.ProductId == productId && x.ProductImageId == imageId && !x.IsDeleted && x.IsActive); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(cancellationToken); }
    public void Add(ProductImage image) => context.ProductImages.Add(image);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
