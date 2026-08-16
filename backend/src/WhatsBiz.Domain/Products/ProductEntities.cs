namespace WhatsBiz.Domain.Products;

public static class BarcodeTypes
{
    public const string Code128 = "CODE128";
    public const string Ean13 = "EAN13";
    public const string Ean8 = "EAN8";
    public const string Upc = "UPC";
    public const string Code39 = "CODE39";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Code128, Ean13, Ean8, Upc, Code39
    };
}

public abstract class ProductMasterEntity
{
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ProductCategory : ProductMasterEntity
{
    public Guid ProductCategoryId { get; set; } = Guid.NewGuid();
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public ProductCategory? ParentCategory { get; set; }
}

public sealed class Brand : ProductMasterEntity
{
    public Guid BrandId { get; set; } = Guid.NewGuid();
    public string BrandCode { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Logo { get; set; }
}

public sealed class UnitOfMeasure : ProductMasterEntity
{
    public Guid UnitId { get; set; } = Guid.NewGuid();
    public string UnitCode { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public byte DecimalPlaces { get; set; }
}

public sealed class Product : ProductMasterEntity
{
    public Guid ProductId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string BarcodeType { get; set; } = BarcodeTypes.Code128;
    public string ProductName { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? LongDescription { get; set; }
    public Guid CategoryId { get; set; }
    public Guid BrandId { get; set; }
    public Guid UnitId { get; set; }
    public string? HSNCode { get; set; }
    public string? SACCode { get; set; }
    public decimal GSTPercentage { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal MRP { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal MaximumStock { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal? Weight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsBatchManaged { get; set; }
    public bool IsSerialManaged { get; set; }
    public ProductCategory Category { get; set; } = null!;
    public Brand Brand { get; set; } = null!;
    public UnitOfMeasure Unit { get; set; } = null!;
}

public sealed class ProductImage : ProductMasterEntity
{
    public Guid ProductImageId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] ImageData { get; set; } = [];
    public byte[] ThumbnailData { get; set; } = [];
    public string ThumbnailContentType { get; set; } = "image/webp";
    public int Width { get; set; }
    public int Height { get; set; }
    public int ThumbnailWidth { get; set; }
    public int ThumbnailHeight { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class ProductBarcode : ProductMasterEntity
{
    public Guid ProductBarcodeId { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

public sealed class ProductPrice : ProductMasterEntity
{
    public Guid ProductPriceId { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string PriceType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
}

public sealed class ProductTaxMapping : ProductMasterEntity
{
    public Guid ProductTaxMappingId { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string TaxCode { get; set; } = string.Empty;
    public decimal TaxPercentage { get; set; }
}
