using AutoMapper;
using MediatR;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Application.Features.Products.Products;

public sealed class GetProductsQueryHandler(IProductRepository repository, IMapper mapper) : IRequestHandler<GetProductsQuery, PagedResult<ProductListItemDto>>
{
    public async Task<PagedResult<ProductListItemDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken) { var (items, count) = await repository.SearchAsync(request.Search, request.IsActive, request.SortBy, request.Descending, request.PageNumber, request.PageSize, cancellationToken); return new PagedResult<ProductListItemDto>(mapper.Map<IReadOnlyCollection<ProductListItemDto>>(items), count, request.PageNumber, request.PageSize); }
}

public sealed class GetProductByIdQueryHandler(IProductRepository repository, IMapper mapper) : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<ProductDto> Handle(GetProductByIdQuery request, CancellationToken cancellationToken) => mapper.Map<ProductDto>(await repository.GetAsync(request.ProductId, false, cancellationToken) ?? throw new EntityNotFoundException("Product was not found."));
}

public sealed class GetProductHistoryQueryHandler(IProductRepository repository) : IRequestHandler<GetProductHistoryQuery, IReadOnlyCollection<ProductHistoryDto>>
{
    public async Task<IReadOnlyCollection<ProductHistoryDto>> Handle(GetProductHistoryQuery request, CancellationToken cancellationToken)
    {
        var product = await repository.GetAsync(request.ProductId, false, cancellationToken) ?? throw new EntityNotFoundException("Product was not found.");
        var history = await repository.GetHistoryAsync(request.ProductId, cancellationToken);
        return history
            .Append(new ProductHistoryDto(0, "CREATED", "Product created.", product.CreatedBy, true, product.CreatedOn))
            .OrderByDescending(x => x.OccurredOn)
            .ToArray();
    }
}

public sealed class CreateProductCommandHandler(IProductRepository repository, ICurrentUserService currentUser, IMapper mapper) : IRequestHandler<CreateProductCommand, ProductDto>
{
    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken) { await EnsureValidAsync(request.Input, null, cancellationToken); var product = new Product { TenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.") }; Apply(product, request.Input); product.CreatedBy = currentUser.Username; repository.Add(product); await repository.SaveChangesAsync(cancellationToken); return mapper.Map<ProductDto>(await repository.GetAsync(product.ProductId, false, cancellationToken)); }
    private async Task EnsureValidAsync(ProductInput input, Guid? id, CancellationToken token) { if (await repository.ProductCodeExistsAsync(input.ProductCode, id, token)) throw new BusinessRuleException("Product code already exists."); if (!string.IsNullOrWhiteSpace(input.Barcode) && await repository.BarcodeExistsAsync(input.Barcode, id, token)) throw new BusinessRuleException("Barcode already exists."); if (!await repository.ReferencesExistAsync(input.CategoryId, input.BrandId, input.UnitId, token)) throw new BusinessRuleException("Category, brand, or unit is invalid or inactive."); }
    internal static void Apply(Product product, ProductInput input) { product.ProductCode = input.ProductCode.Trim(); product.Barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim(); product.BarcodeType = input.BarcodeType.Trim().ToUpperInvariant(); product.ProductName = input.ProductName.Trim(); product.ShortDescription = input.ShortDescription?.Trim(); product.LongDescription = input.LongDescription?.Trim(); product.CategoryId = input.CategoryId; product.BrandId = input.BrandId; product.UnitId = input.UnitId; product.HSNCode = input.HSNCode?.Trim(); product.SACCode = input.SACCode?.Trim(); product.GSTPercentage = input.GSTPercentage; product.PurchasePrice = input.PurchasePrice; product.SellingPrice = input.SellingPrice; product.MRP = input.MRP; product.MinimumStock = input.MinimumStock; product.MaximumStock = input.MaximumStock; product.ReorderLevel = input.ReorderLevel; product.Weight = input.Weight; product.Length = input.Length; product.Width = input.Width; product.Height = input.Height; product.IsBatchManaged = input.IsBatchManaged; product.IsSerialManaged = input.IsSerialManaged; product.IsActive = input.IsActive; }
}

public sealed class UpdateProductCommandHandler(IProductRepository repository, ICurrentUserService currentUser, IMapper mapper) : IRequestHandler<UpdateProductCommand, ProductDto>
{
    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken cancellationToken) { var product = await repository.GetAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Product was not found."); if (await repository.ProductCodeExistsAsync(request.Input.ProductCode, request.ProductId, cancellationToken)) throw new BusinessRuleException("Product code already exists."); if (!string.IsNullOrWhiteSpace(request.Input.Barcode) && await repository.BarcodeExistsAsync(request.Input.Barcode, request.ProductId, cancellationToken)) throw new BusinessRuleException("Barcode already exists."); if (!await repository.ReferencesExistAsync(request.Input.CategoryId, request.Input.BrandId, request.Input.UnitId, cancellationToken)) throw new BusinessRuleException("Category, brand, or unit is invalid or inactive."); CreateProductCommandHandler.Apply(product, request.Input); product.ModifiedOn = DateTimeOffset.UtcNow; product.ModifiedBy = currentUser.Username; await repository.SaveChangesAsync(cancellationToken); return mapper.Map<ProductDto>(await repository.GetAsync(product.ProductId, false, cancellationToken)); }
}

public sealed class DeleteProductCommandHandler(IProductRepository repository, ICurrentUserService currentUser) : IRequestHandler<DeleteProductCommand>
{
    public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken) { var product = await repository.GetAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Product was not found."); product.IsDeleted = true; product.IsActive = false; product.ModifiedOn = DateTimeOffset.UtcNow; product.ModifiedBy = currentUser.Username; await repository.SaveChangesAsync(cancellationToken); }
}

public sealed class ExportProductsQueryHandler(IProductRepository repository, IProductSpreadsheetService spreadsheet) : IRequestHandler<ExportProductsQuery, byte[]>
{
    public async Task<byte[]> Handle(ExportProductsQuery request, CancellationToken cancellationToken) { var (items, _) = await repository.SearchAsync(request.Search, request.IsActive, "productName", false, 1, 10000, cancellationToken); return spreadsheet.Export(items); }
}

public sealed class DownloadProductTemplateQueryHandler(IProductSpreadsheetService spreadsheet) : IRequestHandler<DownloadProductTemplateQuery, byte[]> { public Task<byte[]> Handle(DownloadProductTemplateQuery request, CancellationToken cancellationToken) => Task.FromResult(spreadsheet.CreateTemplate()); }

public sealed class ImportProductsCommandHandler(IProductRepository repository, IProductSpreadsheetService spreadsheet, ICurrentUserService currentUser) : IRequestHandler<ImportProductsCommand, ImportProductsResult>
{
    public async Task<ImportProductsResult> Handle(ImportProductsCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ProductImportRow> rows;
        try { rows = spreadsheet.Read(request.Content); }
        catch (Exception exception) when (exception is not OperationCanceledException) { throw new BusinessRuleException($"The workbook could not be read: {exception.Message}"); }
        var categories = (await repository.GetCategoriesAsync(cancellationToken)).ToDictionary(x => x.CategoryCode, StringComparer.OrdinalIgnoreCase);
        var brands = (await repository.GetBrandsAsync(cancellationToken)).ToDictionary(x => x.BrandCode, StringComparer.OrdinalIgnoreCase);
        var units = (await repository.GetUnitsAsync(cancellationToken)).ToDictionary(x => x.UnitCode, StringComparer.OrdinalIgnoreCase);
        var importedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importedBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var imported = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ProductCode) || string.IsNullOrWhiteSpace(row.ProductName)) { errors.Add($"Row {row.RowNumber}: product code and name are required."); continue; }
            if (!categories.TryGetValue(row.CategoryCode, out var category) || !brands.TryGetValue(row.BrandCode, out var brand) || !units.TryGetValue(row.UnitCode, out var unit)) { errors.Add($"Row {row.RowNumber}: category, brand, or unit code was not found."); continue; }
            if (row.GSTPercentage is < 0 or > 100 || row.SellingPrice < row.PurchasePrice) { errors.Add($"Row {row.RowNumber}: pricing or GST validation failed."); continue; }
            if (!BarcodeTypes.All.Contains(row.BarcodeType)) { errors.Add($"Row {row.RowNumber}: barcode type is invalid."); continue; }
            if (!importedCodes.Add(row.ProductCode) || await repository.ProductCodeExistsAsync(row.ProductCode, null, cancellationToken)) { errors.Add($"Row {row.RowNumber}: product code already exists."); continue; }
            if (!string.IsNullOrWhiteSpace(row.Barcode) && (!importedBarcodes.Add(row.Barcode) || await repository.BarcodeExistsAsync(row.Barcode, null, cancellationToken))) { errors.Add($"Row {row.RowNumber}: barcode already exists."); continue; }
            repository.Add(new Product { TenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), ProductCode = row.ProductCode.Trim(), Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(), BarcodeType = row.BarcodeType.Trim().ToUpperInvariant(), ProductName = row.ProductName.Trim(), CategoryId = category.ProductCategoryId, BrandId = brand.BrandId, UnitId = unit.UnitId, GSTPercentage = row.GSTPercentage, PurchasePrice = row.PurchasePrice, SellingPrice = row.SellingPrice, MRP = row.MRP, IsActive = row.IsActive, CreatedBy = currentUser.Username });
            imported++;
        }
        if (imported > 0) await repository.SaveChangesAsync(cancellationToken);
        return new ImportProductsResult(imported, errors);
    }
}

public sealed class UploadProductImageCommandHandler(IProductRepository repository, ICurrentUserService currentUser, IProductImageOptimizer optimizer,IProductImageStorage storage) : IRequestHandler<UploadProductImageCommand, ProductImageDto>
{
    public async Task<ProductImageDto> Handle(UploadProductImageCommand request, CancellationToken cancellationToken) { var tenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."); var product = await repository.GetAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Product was not found."); var images = await repository.GetImagesAsync(request.ProductId, false, cancellationToken); if (images.Count >= 5) throw new BusinessRuleException("A product can have a maximum of 5 images."); var optimized = await optimizer.OptimizeAsync(request.FileName, request.ContentType, request.Content, cancellationToken);var imageId=Guid.NewGuid();var stored=await storage.StoreAsync(new(tenantId,request.ProductId,imageId,optimized.CatalogData,optimized.ThumbnailData,optimized.ContentType),cancellationToken); var database=stored.Provider==ProductImageStorageProviders.Database;var image = new ProductImage { ProductImageId=imageId,TenantId = tenantId, ProductId = request.ProductId, CreatedBy = currentUser.Username, IsPrimary = images.Count == 0, FileName = optimized.FileName, ContentType = optimized.ContentType, ImageData = database?optimized.CatalogData:[], ThumbnailData = database?optimized.ThumbnailData:[],StorageProvider=stored.Provider,ObjectKey=stored.ObjectKey,ThumbnailObjectKey=stored.ThumbnailObjectKey,CatalogSizeBytes=stored.CatalogSizeBytes,ThumbnailSizeBytes=stored.ThumbnailSizeBytes,ContentHash=stored.ContentHash, ThumbnailContentType = "image/webp", Width = optimized.Width, Height = optimized.Height, ThumbnailWidth = optimized.ThumbnailWidth, ThumbnailHeight = optimized.ThumbnailHeight }; repository.Add(image); product.ImageUrl = $"/api/products/{request.ProductId}/image"; try{await repository.SaveChangesAsync(cancellationToken);}catch{await storage.DeleteAsync(new(tenantId,stored.Provider,stored.ObjectKey,stored.ThumbnailObjectKey),CancellationToken.None);throw;} return new ProductImageDto(image.ProductImageId, image.ProductId, image.FileName, image.ContentType, image.IsPrimary, $"/api/products/{request.ProductId}/images/{image.ProductImageId}"); }
}

public sealed class GetProductImageQueryHandler(IProductRepository repository,IProductImageStorage storage) : IRequestHandler<GetProductImageQuery, ProductImageFile?> { public async Task<ProductImageFile?> Handle(GetProductImageQuery request, CancellationToken cancellationToken) { var image = await repository.GetImageAsync(request.ProductId, false, cancellationToken);if(image is null)return null;var content=await storage.ReadAsync(new(image.TenantId,image.StorageProvider,request.Thumbnail?image.ThumbnailObjectKey:image.ObjectKey,request.Thumbnail?image.ThumbnailData:image.ImageData,request.Thumbnail?image.ThumbnailContentType:image.ContentType),cancellationToken);return content is null?null:new(image.FileName,content.ContentType,content.Content); } }
public sealed class GetProductImagesQueryHandler(IProductRepository repository) : IRequestHandler<GetProductImagesQuery, IReadOnlyCollection<ProductImageDto>> { public async Task<IReadOnlyCollection<ProductImageDto>> Handle(GetProductImagesQuery request, CancellationToken cancellationToken) { var product = await repository.GetAsync(request.ProductId, false, cancellationToken) ?? throw new EntityNotFoundException("Product was not found."); var images = await repository.GetImagesAsync(product.ProductId, false, cancellationToken); return images.Select(x => new ProductImageDto(x.ProductImageId, x.ProductId, x.FileName, x.ContentType, x.IsPrimary, $"/api/products/{x.ProductId}/images/{x.ProductImageId}")).ToArray(); } }
public sealed class GetProductImageByIdQueryHandler(IProductRepository repository,IProductImageStorage storage) : IRequestHandler<GetProductImageByIdQuery, ProductImageFile?> { public async Task<ProductImageFile?> Handle(GetProductImageByIdQuery request, CancellationToken cancellationToken) { var image = await repository.GetImageByIdAsync(request.ProductId, request.ImageId, false, cancellationToken);if(image is null)return null;var content=await storage.ReadAsync(new(image.TenantId,image.StorageProvider,request.Thumbnail?image.ThumbnailObjectKey:image.ObjectKey,request.Thumbnail?image.ThumbnailData:image.ImageData,request.Thumbnail?image.ThumbnailContentType:image.ContentType),cancellationToken);return content is null?null:new(image.FileName,content.ContentType,content.Content); } }

public sealed class DeleteProductImageCommandHandler(IProductRepository repository, ICurrentUserService currentUser,IProductImageStorage storage) : IRequestHandler<DeleteProductImageCommand> { public async Task Handle(DeleteProductImageCommand request, CancellationToken cancellationToken) { var product = await repository.GetAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Product was not found."); var image = await repository.GetImageByIdAsync(request.ProductId, request.ImageId, true, cancellationToken) ?? throw new EntityNotFoundException("Product image was not found.");var deletion=new ProductImageStorageDeleteRequest(image.TenantId,image.StorageProvider,image.ObjectKey,image.ThumbnailObjectKey); image.IsDeleted = true; image.IsActive = false; image.ImageData = []; image.ThumbnailData = []; image.ModifiedOn = DateTimeOffset.UtcNow; image.ModifiedBy = currentUser.Username; var remaining = (await repository.GetImagesAsync(request.ProductId, false, cancellationToken)).Where(x => x.ProductImageId != image.ProductImageId).ToArray(); if (image.IsPrimary && remaining.Length > 0) remaining.First().IsPrimary = true; product.ImageUrl = remaining.FirstOrDefault()?.ProductImageId is Guid primary ? $"/api/products/{request.ProductId}/images/{primary}" : null; await repository.SaveChangesAsync(cancellationToken);await storage.DeleteAsync(deletion,cancellationToken); } }
