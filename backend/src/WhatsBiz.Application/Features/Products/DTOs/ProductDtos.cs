namespace WhatsBiz.Application.Features.Products.DTOs;

public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int TotalCount, int PageNumber, int PageSize);

public sealed record ProductListItemDto(Guid ProductId, string ProductCode, string? Barcode, string BarcodeType, string ProductName, string CategoryName, string BrandName, string UnitName, decimal PurchasePrice, decimal SellingPrice, decimal GSTPercentage, bool IsActive, string? ImageUrl);

public sealed record ProductDto(Guid ProductId, string ProductCode, string? Barcode, string BarcodeType, string ProductName, string? ShortDescription, string? LongDescription, Guid CategoryId, string CategoryName, Guid BrandId, string BrandName, Guid UnitId, string UnitName, string? HSNCode, string? SACCode, decimal GSTPercentage, decimal PurchasePrice, decimal SellingPrice, decimal MRP, decimal MinimumStock, decimal MaximumStock, decimal ReorderLevel, decimal? Weight, decimal? Length, decimal? Width, decimal? Height, string? ImageUrl, bool IsBatchManaged, bool IsSerialManaged, bool IsActive);

public sealed record ProductInput(string ProductCode, string? Barcode, string ProductName, string? ShortDescription, string? LongDescription, Guid CategoryId, Guid BrandId, Guid UnitId, string? HSNCode, string? SACCode, decimal GSTPercentage, decimal PurchasePrice, decimal SellingPrice, decimal MRP, decimal MinimumStock, decimal MaximumStock, decimal ReorderLevel, decimal? Weight, decimal? Length, decimal? Width, decimal? Height, bool IsBatchManaged, bool IsSerialManaged, bool IsActive, string BarcodeType = "CODE128");

public sealed record ProductCategoryDto(Guid ProductCategoryId, string CategoryCode, string CategoryName, string? Description, int DisplayOrder, Guid? ParentCategoryId, bool IsActive, IReadOnlyCollection<ProductCategoryDto> Children);
public sealed record ProductCategoryInput(string CategoryCode, string CategoryName, string? Description, int DisplayOrder, Guid? ParentCategoryId, bool IsActive);
public sealed record BrandDto(Guid BrandId, string BrandCode, string BrandName, string? Description, string? Logo, bool IsActive);
public sealed record BrandInput(string BrandCode, string BrandName, string? Description, string? Logo, bool IsActive);
public sealed record UnitOfMeasureDto(Guid UnitId, string UnitCode, string UnitName, string ShortName, byte DecimalPlaces, bool IsActive);
public sealed record UnitOfMeasureInput(string UnitCode, string UnitName, string ShortName, byte DecimalPlaces, bool IsActive);
public sealed record ProductImageDto(Guid ProductImageId, Guid ProductId, string FileName, string ContentType, bool IsPrimary, string Url);
public sealed record ImportProductsResult(int ImportedCount, IReadOnlyCollection<string> Errors);
public sealed record ImportProductMasterResult(int ImportedCount, IReadOnlyCollection<string> Errors);
