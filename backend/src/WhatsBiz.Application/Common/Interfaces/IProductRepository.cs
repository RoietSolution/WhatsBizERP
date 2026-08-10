using WhatsBiz.Domain.Products;

namespace WhatsBiz.Application.Common.Interfaces;

public interface IProductRepository
{
    Task<(IReadOnlyCollection<Product> Items, int TotalCount)> SearchAsync(string? search, bool? isActive, string sortBy, bool descending, int pageNumber, int pageSize, CancellationToken cancellationToken);
    Task<Product?> GetAsync(Guid id, bool tracking, CancellationToken cancellationToken);
    Task<bool> ProductCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> BarcodeExistsAsync(string barcode, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> ReferencesExistAsync(Guid categoryId, Guid brandId, Guid unitId, CancellationToken cancellationToken);
    void Add(Product product);
    Task<IReadOnlyCollection<ProductCategory>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<ProductCategory?> GetCategoryAsync(Guid id, bool tracking, CancellationToken cancellationToken);
    Task<bool> CategoryCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> CategoryHasChildrenOrProductsAsync(Guid id, CancellationToken cancellationToken);
    void Add(ProductCategory category);
    Task<IReadOnlyCollection<Brand>> GetBrandsAsync(CancellationToken cancellationToken);
    Task<Brand?> GetBrandAsync(Guid id, bool tracking, CancellationToken cancellationToken);
    Task<bool> BrandCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> BrandHasProductsAsync(Guid id, CancellationToken cancellationToken);
    void Add(Brand brand);
    Task<IReadOnlyCollection<UnitOfMeasure>> GetUnitsAsync(CancellationToken cancellationToken);
    Task<UnitOfMeasure?> GetUnitAsync(Guid id, bool tracking, CancellationToken cancellationToken);
    Task<bool> UnitCodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> UnitHasProductsAsync(Guid id, CancellationToken cancellationToken);
    void Add(UnitOfMeasure unit);
    Task<ProductImage?> GetImageAsync(Guid productId, bool tracking, CancellationToken cancellationToken);
    void Add(ProductImage image);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IProductSpreadsheetService
{
    byte[] Export(IReadOnlyCollection<Product> products);
    byte[] CreateTemplate();
    IReadOnlyCollection<ProductImportRow> Read(byte[] content);
}

public sealed record ProductImportRow(int RowNumber, string ProductCode, string? Barcode, string ProductName, string CategoryCode, string BrandCode, string UnitCode, decimal GSTPercentage, decimal PurchasePrice, decimal SellingPrice, decimal MRP, bool IsActive, string BarcodeType);
