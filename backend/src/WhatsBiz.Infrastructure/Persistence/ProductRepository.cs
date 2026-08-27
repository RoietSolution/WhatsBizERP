using System.Data;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class ProductRepository(ApplicationDbContext context, ICurrentUserService currentUser) : IProductRepository
{
    private IQueryable<Product> TenantProducts => currentUser.TenantId is Guid tenantId ? context.Products.Where(x => x.TenantId == tenantId) : context.Products.Where(_ => false);
    public async Task<(IReadOnlyCollection<Product> Items, int TotalCount)> SearchAsync(string? search, bool? isActive, string sortBy, bool descending, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = TenantProducts.AsNoTracking().Include(x => x.Category).Include(x => x.Brand).Include(x => x.Unit).Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(x => x.ProductCode.Contains(term) || x.ProductName.Contains(term) || (x.Barcode != null && x.Barcode.Contains(term))); }
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive);
        query = (sortBy.ToLowerInvariant(), descending) switch { ("productcode", false) => query.OrderBy(x => x.ProductCode), ("productcode", true) => query.OrderByDescending(x => x.ProductCode), ("sellingprice", false) => query.OrderBy(x => x.SellingPrice), ("sellingprice", true) => query.OrderByDescending(x => x.SellingPrice), ("categoryname", false) => query.OrderBy(x => x.Category.CategoryName), ("categoryname", true) => query.OrderByDescending(x => x.Category.CategoryName), (_, true) => query.OrderByDescending(x => x.ProductName), _ => query.OrderBy(x => x.ProductName) };
        var count = await query.CountAsync(cancellationToken);
        var items = await query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return (items, count);
    }

    public Task<Product?> GetAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = TenantProducts.Include(x => x.Category).Include(x => x.Brand).Include(x => x.Unit).Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.ProductId == id, cancellationToken); }
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
    public Task<bool> BarcodeExistsAsync(string barcode, Guid? excludingId, CancellationToken cancellationToken) => TenantProducts.AnyAsync(x => !x.IsDeleted && x.Barcode == barcode.Trim() && (!excludingId.HasValue || x.ProductId != excludingId), cancellationToken);
    public async Task<bool> ReferencesExistAsync(Guid categoryId, Guid brandId, Guid unitId, CancellationToken cancellationToken) => await context.ProductCategories.AnyAsync(x => x.ProductCategoryId == categoryId && x.IsActive && !x.IsDeleted, cancellationToken) && await context.Brands.AnyAsync(x => x.BrandId == brandId && x.IsActive && !x.IsDeleted, cancellationToken) && await context.UnitsOfMeasure.AnyAsync(x => x.UnitId == unitId && x.IsActive && !x.IsDeleted, cancellationToken);
    public void Add(Product product) => context.Products.Add(product);
    public async Task<IReadOnlyCollection<ProductCategory>> GetCategoriesAsync(CancellationToken cancellationToken) => await context.ProductCategories.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.DisplayOrder).ThenBy(x => x.CategoryName).ToArrayAsync(cancellationToken);
    public Task<ProductCategory?> GetCategoryAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = context.ProductCategories.Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.ProductCategoryId == id, cancellationToken); }
    public Task<bool> CategoryCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) => context.ProductCategories.AnyAsync(x => !x.IsDeleted && x.CategoryCode == code.Trim() && (!excludingId.HasValue || x.ProductCategoryId != excludingId), cancellationToken);
    public async Task<bool> CategoryHasChildrenOrProductsAsync(Guid id, CancellationToken cancellationToken) => await context.ProductCategories.AnyAsync(x => !x.IsDeleted && x.ParentCategoryId == id, cancellationToken) || await TenantProducts.AnyAsync(x => !x.IsDeleted && x.CategoryId == id, cancellationToken);
    public void Add(ProductCategory category) => context.ProductCategories.Add(category);
    public async Task<IReadOnlyCollection<Brand>> GetBrandsAsync(CancellationToken cancellationToken) => await context.Brands.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.BrandName).ToArrayAsync(cancellationToken);
    public Task<Brand?> GetBrandAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = context.Brands.Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.BrandId == id, cancellationToken); }
    public Task<bool> BrandCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) => context.Brands.AnyAsync(x => !x.IsDeleted && x.BrandCode == code.Trim() && (!excludingId.HasValue || x.BrandId != excludingId), cancellationToken);
    public Task<bool> BrandHasProductsAsync(Guid id, CancellationToken cancellationToken) => TenantProducts.AnyAsync(x => !x.IsDeleted && x.BrandId == id, cancellationToken);
    public void Add(Brand brand) => context.Brands.Add(brand);
    public async Task<IReadOnlyCollection<UnitOfMeasure>> GetUnitsAsync(CancellationToken cancellationToken) => await context.UnitsOfMeasure.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.UnitName).ToArrayAsync(cancellationToken);
    public Task<UnitOfMeasure?> GetUnitAsync(Guid id, bool tracking, CancellationToken cancellationToken) { var query = context.UnitsOfMeasure.Where(x => !x.IsDeleted); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(x => x.UnitId == id, cancellationToken); }
    public Task<bool> UnitCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) => context.UnitsOfMeasure.AnyAsync(x => !x.IsDeleted && x.UnitCode == code.Trim() && (!excludingId.HasValue || x.UnitId != excludingId), cancellationToken);
    public Task<bool> UnitHasProductsAsync(Guid id, CancellationToken cancellationToken) => TenantProducts.AnyAsync(x => !x.IsDeleted && x.UnitId == id, cancellationToken);
    public void Add(UnitOfMeasure unit) => context.UnitsOfMeasure.Add(unit);
    private IQueryable<ProductImage> TenantImages => currentUser.TenantId is Guid tenantId ? context.ProductImages.Where(x => x.TenantId == tenantId) : context.ProductImages.Where(_ => false);
    public Task<ProductImage?> GetImageAsync(Guid productId, bool tracking, CancellationToken cancellationToken) { var query = TenantImages.Where(x => x.ProductId == productId && !x.IsDeleted && x.IsActive); if (!tracking) query = query.AsNoTracking(); return query.OrderByDescending(x => x.IsPrimary).FirstOrDefaultAsync(cancellationToken); }
    public async Task<IReadOnlyCollection<ProductImage>> GetImagesAsync(Guid productId, bool tracking, CancellationToken cancellationToken) { IQueryable<ProductImage> query = TenantImages.Where(x => x.ProductId == productId && !x.IsDeleted && x.IsActive).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.CreatedOn); if (!tracking) query = query.AsNoTracking(); return await query.ToArrayAsync(cancellationToken); }
    public Task<ProductImage?> GetImageByIdAsync(Guid productId, Guid imageId, bool tracking, CancellationToken cancellationToken) { var query = TenantImages.Where(x => x.ProductId == productId && x.ProductImageId == imageId && !x.IsDeleted && x.IsActive); if (!tracking) query = query.AsNoTracking(); return query.SingleOrDefaultAsync(cancellationToken); }
    public void Add(ProductImage image) => context.ProductImages.Add(image);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
