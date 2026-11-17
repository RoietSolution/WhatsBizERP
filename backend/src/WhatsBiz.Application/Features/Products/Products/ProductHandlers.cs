using AutoMapper;
using System.Collections.Concurrent;
using MediatR;
using WhatsBiz.Application.Common.Exceptions;
using UnauthorizedAccessException = System.UnauthorizedAccessException;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Domain.Products;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Application.Features.Products.Products;

public sealed class GetProductsQueryHandler(IProductRepository repository, IMapper mapper) : IRequestHandler<GetProductsQuery, PagedResult<ProductListItemDto>>
{
    public async Task<PagedResult<ProductListItemDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken) { var (items, count) = await repository.SearchAsync(request.Search, request.IsActive, request.CategoryId, request.BrandId, request.SortBy, request.Descending, request.PageNumber, request.PageSize, cancellationToken); return new PagedResult<ProductListItemDto>(mapper.Map<IReadOnlyCollection<ProductListItemDto>>(items), count, request.PageNumber, request.PageSize); }
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
    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
        var additional = ProductIdentifierPolicy.NormalizeAdditional(request.Input);
        await EnsureValidAsync(request.Input, additional, null, cancellationToken);
        var product = new Product { TenantId = tenantId };
        Apply(product, request.Input);
        SyncSpecifications(product, request.Input, currentUser.Username);
        product.CreatedBy = currentUser.Username;
        repository.Add(product);
        foreach (var specification in product.Specifications) repository.Add(specification);
        foreach (var code in additional)
            repository.Add(new ProductBarcode { TenantId = tenantId, ProductId = product.ProductId, Barcode = code.Barcode, BarcodeType = code.BarcodeType, CreatedBy = currentUser.Username });
        await repository.SaveChangesAsync(cancellationToken);
        return mapper.Map<ProductDto>(await repository.GetAsync(product.ProductId, false, cancellationToken));
    }
    private async Task EnsureValidAsync(ProductInput input, IReadOnlyCollection<ProductBarcodeInput> additional, Guid? id, CancellationToken token)
    {
        if (await repository.ProductCodeExistsAsync(input.ProductCode, id, token)) throw new BusinessRuleException("Product code already exists.");
        foreach (var identifier in additional.Select(x => x.Barcode).Prepend(input.Barcode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
        {
            var owner = await repository.IdentifierOwnerAsync(identifier!, id, token);
            if (owner is not null) throw new BusinessRuleException($"This barcode/QR is already assigned to {owner.ProductName}.");
        }
        if (!await repository.ReferencesExistAsync(input.CategoryId, input.BrandId, input.UnitId, token)) throw new BusinessRuleException("Category, brand, or unit is invalid or inactive.");
    }
    internal static void Apply(Product product, ProductInput input) { product.ProductCode = input.ProductCode.Trim(); product.Barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode; product.BarcodeType = input.BarcodeType.Trim().ToUpperInvariant(); product.ProductName = input.ProductName.Trim(); product.ShortDescription = ProductDescriptionSanitizer.Sanitize(input.ShortDescription, 10000); product.LongDescription = ProductDescriptionSanitizer.Sanitize(input.LongDescription, 100000); product.CategoryId = input.CategoryId; product.BrandId = input.BrandId; product.UnitId = input.UnitId; product.HSNCode = input.HSNCode?.Trim(); product.SACCode = input.SACCode?.Trim(); product.GSTPercentage = input.GSTPercentage; product.GstPricingType = input.GstPricingType.Trim().ToUpperInvariant(); product.PurchasePrice = input.PurchasePrice; product.PackagingCost = input.PackagingCost; product.ShippingCost = input.ShippingCost; product.CombinedShippingPackagingCost = input.CombinedShippingPackagingCost; product.SellingPrice = input.SellingPrice; product.MRP = input.MRP; product.MinimumStock = input.MinimumStock; product.MaximumStock = input.MaximumStock; product.ReorderLevel = input.ReorderLevel; product.Weight = input.Weight; product.PackSize = string.IsNullOrWhiteSpace(input.PackSize) ? null : input.PackSize.Trim(); product.Length = input.Length; product.Width = input.Width; product.Height = input.Height; product.IsBatchManaged = input.IsBatchManaged; product.IsSerialManaged = input.IsSerialManaged; product.IsActive = input.IsActive; product.IsWhatsAppVisible = input.IsWhatsAppVisible; product.ReturnPolicyMode = input.ReturnPolicyMode.Trim().ToUpperInvariant(); product.ReturnWindowDays = input.ReturnWindowDays; product.ReturnPolicyDescription = string.IsNullOrWhiteSpace(input.ReturnPolicyDescription) ? null : input.ReturnPolicyDescription.Trim(); }
    internal static void SyncSpecifications(Product product, ProductInput input, string? user, Action<ProductSpecification>? add = null)
    {
        var requested = (input.Specifications ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Value)).Select((x, index) => new { Name = x.Name.Trim(), Value = x.Value.Trim(), DisplayOrder = index }).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToArray();
        foreach (var existing in product.Specifications.Where(x => !x.IsDeleted))
        {
            if (existing.ProductId != product.ProductId || existing.TenantId != product.TenantId)
                throw new BusinessRuleException("The product specification does not belong to this product.");
            var match = requested.FirstOrDefault(x => string.Equals(x.Name, existing.Name, StringComparison.OrdinalIgnoreCase));
            if (match is null) { existing.IsDeleted = true; existing.IsActive = false; existing.ModifiedOn = DateTimeOffset.UtcNow; existing.ModifiedBy = user; continue; }
            existing.Name = match.Name; existing.Value = match.Value; existing.DisplayOrder = match.DisplayOrder; existing.ModifiedOn = DateTimeOffset.UtcNow; existing.ModifiedBy = user;
        }
        foreach (var item in requested.Where(x => !product.Specifications.Any(s => !s.IsDeleted && string.Equals(s.Name, x.Name, StringComparison.OrdinalIgnoreCase))))
        {
            var specification = new ProductSpecification { TenantId = product.TenantId, ProductId = product.ProductId, Name = item.Name, Value = item.Value, DisplayOrder = item.DisplayOrder, CreatedBy = user };
            product.Specifications.Add(specification);
            add?.Invoke(specification);
        }
    }
}

public sealed class UpdateProductCommandHandler(IProductRepository repository, ICurrentUserService currentUser, IMapper mapper) : IRequestHandler<UpdateProductCommand, ProductDto>
{
    public async Task<ProductDto> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await repository.GetAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Product was not found.");
        var additional = ProductIdentifierPolicy.NormalizeAdditional(request.Input);
        if (await repository.ProductCodeExistsAsync(request.Input.ProductCode, request.ProductId, cancellationToken)) throw new BusinessRuleException("Product code already exists.");
        foreach (var identifier in additional.Select(x => x.Barcode).Prepend(request.Input.Barcode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
        {
            var owner = await repository.IdentifierOwnerAsync(identifier!, request.ProductId, cancellationToken);
            if (owner is not null) throw new BusinessRuleException($"This barcode/QR is already assigned to {owner.ProductName}.");
        }
        if (!await repository.ReferencesExistAsync(request.Input.CategoryId, request.Input.BrandId, request.Input.UnitId, cancellationToken)) throw new BusinessRuleException("Category, brand, or unit is invalid or inactive.");

        CreateProductCommandHandler.Apply(product, request.Input);
        CreateProductCommandHandler.SyncSpecifications(product, request.Input, currentUser.Username, repository.Add);
        SyncAdditional(product, additional);
        product.ModifiedOn = DateTimeOffset.UtcNow;
        product.ModifiedBy = currentUser.Username;
        await repository.SaveChangesAsync(cancellationToken);
        return mapper.Map<ProductDto>(await repository.GetAsync(product.ProductId, false, cancellationToken));
    }

    private void SyncAdditional(Product product, IReadOnlyCollection<ProductBarcodeInput> desired)
    {
        var desiredByValue = desired.ToDictionary(x => x.Barcode, StringComparer.Ordinal);
        foreach (var existing in product.Barcodes.Where(x => !x.IsPrimary && !x.IsDeleted))
        {
            if (desiredByValue.Remove(existing.Barcode, out var requested))
            {
                existing.BarcodeType = requested.BarcodeType;
                existing.IsActive = true;
                existing.ModifiedOn = DateTimeOffset.UtcNow;
                existing.ModifiedBy = currentUser.Username;
            }
            else
            {
                existing.IsDeleted = true;
                existing.IsActive = false;
                existing.ModifiedOn = DateTimeOffset.UtcNow;
                existing.ModifiedBy = currentUser.Username;
            }
        }
        foreach (var requested in desiredByValue.Values)
        {
            var deleted = product.Barcodes.FirstOrDefault(x => !x.IsPrimary && x.IsDeleted && x.Barcode == requested.Barcode);
            if (deleted is not null)
            {
                deleted.BarcodeType = requested.BarcodeType;
                deleted.IsDeleted = false;
                deleted.IsActive = true;
                deleted.ModifiedOn = DateTimeOffset.UtcNow;
                deleted.ModifiedBy = currentUser.Username;
            }
            else
            {
                repository.Add(new ProductBarcode { TenantId = product.TenantId, ProductId = product.ProductId, Barcode = requested.Barcode, BarcodeType = requested.BarcodeType, CreatedBy = currentUser.Username });
            }
        }
    }
}

internal static class ProductIdentifierPolicy
{
    internal const int MaximumAdditionalCodes = 10;
    internal const int MaximumAdditionalCodeLength = 450;

    internal static IReadOnlyCollection<ProductBarcodeInput> NormalizeAdditional(ProductInput input)
    {
        var primary = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode;
        return (input.AdditionalBarcodes ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Barcode))
            .Select(x => new ProductBarcodeInput(x.Barcode, x.BarcodeType.Trim().ToUpperInvariant()))
            .Where(x => !string.Equals(x.Barcode, primary, StringComparison.Ordinal))
            .GroupBy(x => x.Barcode, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
    }
}

public sealed class DeleteProductCommandHandler(IProductRepository repository, IProductImageCleanupScheduler cleanup, ICurrentUserService currentUser) : IRequestHandler<DeleteProductCommand>
{
    public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await repository.GetAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Product was not found.");
        var modifiedOn = DateTimeOffset.UtcNow;
        product.IsActiveBeforeDelete = product.IsActive;
        product.DeletedOnUtc = modifiedOn;
        product.IsDeleted = true;
        product.IsActive = false;
        product.ModifiedOn = modifiedOn;
        product.ModifiedBy = currentUser.Username;
        await cleanup.ScheduleProductAsync(product.TenantId, product.ProductId, currentUser.Username ?? "unknown", modifiedOn, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public sealed class RestoreProductCommandHandler(IProductRepository repository, IProductImageCleanupScheduler cleanup, ICurrentUserService currentUser) : IRequestHandler<RestoreProductCommand>
{
    public async Task Handle(RestoreProductCommand request, CancellationToken cancellationToken)
    {
        var product = await repository.GetDeletedAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Deleted product was not found.");
        var now = DateTimeOffset.UtcNow;
        if (product.DeletedOnUtc is null)
            throw new BusinessRuleException("The product recovery period has expired.");
        if (!await cleanup.CancelProductAsync(product.TenantId, product.ProductId, cancellationToken))
            throw new BusinessRuleException("The product recovery period has expired or image cleanup has started; the product cannot be restored.");
        product.IsDeleted = false;
        product.IsActive = product.IsActiveBeforeDelete ?? false;
        product.IsActiveBeforeDelete = null;
        product.DeletedOnUtc = null;
        product.ModifiedOn = now;
        product.ModifiedBy = currentUser.Username;
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ExportProductsQueryHandler(IProductRepository repository, IProductSpreadsheetService spreadsheet) : IRequestHandler<ExportProductsQuery, byte[]>
{
    public async Task<byte[]> Handle(ExportProductsQuery request, CancellationToken cancellationToken) { var (items, _) = await repository.SearchAsync(request.Search, request.IsActive, null, null, "productName", false, 1, 10000, cancellationToken); return spreadsheet.Export(items); }
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
            if (row.GSTPercentage is < 0 or > 100 || row.SellingPrice < row.PurchasePrice || row.GstPricingType is not ("INCLUSIVE" or "EXCLUSIVE")) { errors.Add($"Row {row.RowNumber}: pricing, GST percentage, or GST pricing type is invalid."); continue; }
            if (!BarcodeTypes.All.Contains(row.BarcodeType)) { errors.Add($"Row {row.RowNumber}: barcode type is invalid."); continue; }
            if (!importedCodes.Add(row.ProductCode) || await repository.ProductCodeExistsAsync(row.ProductCode, null, cancellationToken)) { errors.Add($"Row {row.RowNumber}: product code already exists."); continue; }
            if (!string.IsNullOrWhiteSpace(row.Barcode) && (!importedBarcodes.Add(row.Barcode) || await repository.BarcodeExistsAsync(row.Barcode, null, cancellationToken))) { errors.Add($"Row {row.RowNumber}: barcode already exists."); continue; }
            repository.Add(new Product { TenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required."), ProductCode = row.ProductCode.Trim(), Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(), BarcodeType = row.BarcodeType.Trim().ToUpperInvariant(), ProductName = row.ProductName.Trim(), CategoryId = category.ProductCategoryId, BrandId = brand.BrandId, UnitId = unit.UnitId, GSTPercentage = row.GSTPercentage, GstPricingType = row.GstPricingType, PurchasePrice = row.PurchasePrice, SellingPrice = row.SellingPrice, MRP = row.MRP, IsActive = row.IsActive, CreatedBy = currentUser.Username });
            imported++;
        }
        if (imported > 0) await repository.SaveChangesAsync(cancellationToken);
        return new ImportProductsResult(imported, errors);
    }
}

public sealed class UploadProductImageCommandHandler(IProductRepository repository, ICurrentUserService currentUser, IProductImageOptimizer optimizer,IProductImageStorage storage, IStorefrontFeedbackAttachmentScanner scanner) : IRequestHandler<UploadProductImageCommand, ProductImageDto>
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProductGates = new(StringComparer.Ordinal);

    public async Task<ProductImageDto> Handle(UploadProductImageCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");
        var gate = ProductGates.GetOrAdd($"{tenantId:N}:{request.ProductId:N}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            _ = await repository.GetAsync(request.ProductId, false, cancellationToken) ?? throw new EntityNotFoundException("Product was not found.");
            var images = await repository.GetImagesAsync(request.ProductId, false, cancellationToken);
            if (images.Count >= 5) throw new BusinessRuleException("A product can have a maximum of 5 images.");
            StorefrontProductImageScanGuard.RequireClean(await ScanOriginalAsync(scanner, request.Content, cancellationToken));
            var optimized = await optimizer.OptimizeAsync(request.FileName, request.ContentType, request.Content, cancellationToken);
            var imageId = Guid.NewGuid();
            var stored = await storage.StoreAsync(new(tenantId, request.ProductId, imageId, optimized.CatalogData, optimized.ThumbnailData, optimized.ContentType), cancellationToken);
            var database = stored.Provider == ProductImageStorageProviders.Database;
            var image = new ProductImage { ProductImageId = imageId, TenantId = tenantId, ProductId = request.ProductId, CreatedBy = currentUser.Username, IsPrimary = images.Count == 0, FileName = optimized.FileName, ContentType = optimized.ContentType, ImageData = database ? optimized.CatalogData : [], ThumbnailData = database ? optimized.ThumbnailData : [], StorageProvider = stored.Provider, ObjectKey = stored.ObjectKey, ThumbnailObjectKey = stored.ThumbnailObjectKey, CatalogSizeBytes = stored.CatalogSizeBytes, ThumbnailSizeBytes = stored.ThumbnailSizeBytes, ContentHash = stored.ContentHash, ThumbnailContentType = "image/webp", Width = optimized.Width, Height = optimized.Height, ThumbnailWidth = optimized.ThumbnailWidth, ThumbnailHeight = optimized.ThumbnailHeight };
            repository.Add(image);
            try { await repository.SaveChangesAsync(cancellationToken); }
            catch { try { await storage.DeleteAsync(new(tenantId, request.ProductId, imageId, stored.Provider, stored.ObjectKey, stored.ThumbnailObjectKey), CancellationToken.None); } catch { /* preserve the original database error; storage logs the cleanup failure */ } throw; }
            return new ProductImageDto(image.ProductImageId, image.ProductId, image.FileName, image.ContentType, image.IsPrimary, $"/api/products/{request.ProductId}/images/{image.ProductImageId}");
        }
        finally { gate.Release(); }
    }
    private static async Task<StorefrontAttachmentScanResult> ScanOriginalAsync(IStorefrontFeedbackAttachmentScanner scanner, byte[] content, CancellationToken token)
    { try { return await scanner.ScanAsync(content, token); } catch (Exception exception) when (exception is not OperationCanceledException) { throw new BusinessRuleException("The image could not be security-scanned and was not published."); } }
}

public static class StorefrontProductImageScanGuard
{
    public static void RequireClean(StorefrontAttachmentScanResult result)
    {
        if (result != StorefrontAttachmentScanResult.Clean)
            throw new BusinessRuleException(result == StorefrontAttachmentScanResult.Infected
                ? "The image was rejected by the malware scanner."
                : "The image could not be security-scanned and was not published.");
    }
}

public sealed class GetProductImageQueryHandler(IProductRepository repository,IProductImageStorage storage) : IRequestHandler<GetProductImageQuery, ProductImageFile?> { public async Task<ProductImageFile?> Handle(GetProductImageQuery request, CancellationToken cancellationToken) { var image = await repository.GetImageAsync(request.ProductId, false, cancellationToken);if(image is null)return null;var content=await storage.ReadAsync(new(image.TenantId,image.StorageProvider,request.Thumbnail?image.ThumbnailObjectKey:image.ObjectKey,request.Thumbnail?image.ThumbnailData:image.ImageData,request.Thumbnail?image.ThumbnailContentType:image.ContentType),cancellationToken);return content is null?null:new(image.FileName,content.ContentType,content.Content); } }
public sealed class GetProductImagesQueryHandler(IProductRepository repository) : IRequestHandler<GetProductImagesQuery, IReadOnlyCollection<ProductImageDto>>
{
    public async Task<IReadOnlyCollection<ProductImageDto>> Handle(GetProductImagesQuery request, CancellationToken cancellationToken)
    {
        var product = await repository.GetAsync(request.ProductId, false, cancellationToken) ?? throw new EntityNotFoundException("Product was not found.");
        var managed = await repository.GetImagesAsync(product.ProductId, false, cancellationToken);
        var external = (await repository.GetExternalImagesAsync(product.ProductId, cancellationToken))
            .OrderBy(image => image.DisplayOrder == 1 ? 0 : 1).ThenBy(image => image.DisplayOrder).ThenBy(image => image.ImageUrl, StringComparer.Ordinal).ToArray();
        var externalPrimary = external.FirstOrDefault();
        var result = external.Select((image, index) => new ProductImageDto(
                image.ExternalImageId, image.ProductId, SafeExternalFileName(image.ImageUrl), "image/external-url",
                index == 0, image.ImageUrl, "EXTERNAL_URL", image.DisplayOrder))
            .Concat(managed.Select(image => new ProductImageDto(
                image.ProductImageId, image.ProductId, image.FileName, image.ContentType,
                externalPrimary is null && image.IsPrimary, $"/api/products/{image.ProductId}/images/{image.ProductImageId}", "MANAGED", int.MaxValue)))
            .ToArray();
        return result;
    }

    private static string SafeExternalFileName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(Path.GetFileName(uri.AbsolutePath))
            ? Path.GetFileName(uri.AbsolutePath)
            : "Shopify product image";
}
public sealed class GetProductImageByIdQueryHandler(IProductRepository repository,IProductImageStorage storage) : IRequestHandler<GetProductImageByIdQuery, ProductImageFile?> { public async Task<ProductImageFile?> Handle(GetProductImageByIdQuery request, CancellationToken cancellationToken) { var image = await repository.GetImageByIdAsync(request.ProductId, request.ImageId, false, cancellationToken);if(image is null)return null;var content=await storage.ReadAsync(new(image.TenantId,image.StorageProvider,request.Thumbnail?image.ThumbnailObjectKey:image.ObjectKey,request.Thumbnail?image.ThumbnailData:image.ImageData,request.Thumbnail?image.ThumbnailContentType:image.ContentType),cancellationToken);return content is null?null:new(image.FileName,content.ContentType,content.Content); } }

public sealed class DeleteProductImageCommandHandler(IProductRepository repository, IProductImageCleanupScheduler cleanup, ICurrentUserService currentUser) : IRequestHandler<DeleteProductImageCommand> { public async Task Handle(DeleteProductImageCommand request, CancellationToken cancellationToken) { var product = await repository.GetAsync(request.ProductId, true, cancellationToken) ?? throw new EntityNotFoundException("Product was not found."); var image = await repository.GetImageByIdAsync(request.ProductId, request.ImageId, true, cancellationToken) ?? throw new EntityNotFoundException("Product image was not found."); var now = DateTimeOffset.UtcNow; image.IsDeleted = true; image.IsActive = false; image.ModifiedOn = now; image.ModifiedBy = currentUser.Username; var remaining = (await repository.GetImagesAsync(request.ProductId, false, cancellationToken)).Where(x => x.ProductImageId != image.ProductImageId).ToArray(); if (image.IsPrimary && remaining.Length > 0) remaining.First().IsPrimary = true; product.ImageUrl = remaining.FirstOrDefault()?.ProductImageId is Guid primary ? $"/api/products/{request.ProductId}/images/{primary}" : null; await cleanup.ScheduleImageAsync(image, currentUser.Username ?? "unknown", now, cancellationToken); await repository.SaveChangesAsync(cancellationToken); } }

public sealed class SetProductStatusCommandHandler(IProductRepository repository, ICurrentUserService currentUser) : IRequestHandler<SetProductStatusCommand>
{
    public async Task Handle(SetProductStatusCommand request, CancellationToken cancellationToken)
    {
        var product = await repository.GetAsync(request.ProductId, true, cancellationToken)
            ?? throw new EntityNotFoundException("Product was not found.");
        product.IsActive = request.IsActive;
        product.ModifiedOn = DateTimeOffset.UtcNow;
        product.ModifiedBy = currentUser.Username;
        await repository.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SetProductsStatusCommandHandler(IProductRepository repository, ICurrentUserService currentUser) : IRequestHandler<SetProductsStatusCommand, ProductStatusBulkResult>
{
    public async Task<ProductStatusBulkResult> Handle(SetProductsStatusCommand request, CancellationToken cancellationToken)
    {
        var ids = request.ProductIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) throw new BusinessRuleException("Select at least one product.");
        if (ids.Length > 500) throw new BusinessRuleException("A maximum of 500 products can be updated at once.");
        var products = await repository.GetManyAsync(ids, true, cancellationToken);
        var found = products.Select(x => x.ProductId).ToHashSet();
        var missing = ids.Where(x => !found.Contains(x)).ToList();
        var updated = 0;
        var changedAt = DateTimeOffset.UtcNow;
        foreach (var product in products)
        {
            if (product.IsActive == request.IsActive) continue;
            product.IsActive = request.IsActive;
            product.ModifiedOn = changedAt;
            product.ModifiedBy = currentUser.Username;
            updated++;
        }
        if (updated > 0) await repository.SaveChangesAsync(cancellationToken);
        return new(ids.Length, updated, missing);
    }
}
