using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Suppliers;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/products/shopify-import")]
public sealed class ShopifyRetailerProductImportController(
    ApplicationDbContext db, ICurrentUserService user, IProductRepository products, ISender sender) : ControllerBase
{
    // The Customer PWA / ERP posts selections with JSON.stringify, which emits
    // camelCase property names. Use the same web defaults at this form-JSON
    // boundary as ASP.NET's regular JSON input formatter; otherwise these
    // selections can be silently ignored and replaced with server defaults.
    private static readonly JsonSerializerOptions SelectionsJsonOptions = new(JsonSerializerDefaults.Web);

    private Guid Tenant => user.TenantId ?? throw new UnauthorizedAccessException("Tenant context is required.");

    [HttpPost("headers"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10 * 1024 * 1024), Consumes("multipart/form-data")]
    public async Task<IActionResult> Headers([FromForm] IFormFile file, CancellationToken token)
    {
        if (file is null || file.Length is <= 0 or > 10 * 1024 * 1024)
            return BadRequest(new { message = "Upload a CSV or XLSX file up to 10 MB." });
        var bytes = await ReadFile(file, token);
        try
        {
            var headers = ShopifyRetailerImportParser.ReadHeaders(bytes, file.FileName);
            var duplicateHeaders = headers.GroupBy(Normalize, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.First()).ToArray();
            return Ok(new { headers, duplicateHeaders });
        }
        catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("preview"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10 * 1024 * 1024), Consumes("multipart/form-data")]
    public async Task<IActionResult> Preview([FromForm] IFormFile file, [FromForm] string sourceStore,
        [FromForm] string? selectionsJson, CancellationToken token)
    {
        if (file is null || file.Length is <= 0 or > 10 * 1024 * 1024) return BadRequest(new { message = "Upload a CSV or XLSX file up to 10 MB." });
        if (!ShopifySourceIdentity.TryNormalize(sourceStore, out var store)) return BadRequest(new { message = "Enter a Shopify source store, supplier name, email address or domain." });
        if (!TrySelections(selectionsJson, out var selections, out var selectionError)) return BadRequest(new { message = selectionError });
        if (!TryResolveSupplierCostTreatment(store, selections, out selections, out selectionError)) return BadRequest(new { message = selectionError });
        var bytes = await ReadFile(file, token);
        IReadOnlyCollection<ShopifyRetailerProductRow> rows;
        try { rows = ShopifyRetailerImportParser.Parse(bytes, file.FileName, selections.FieldMappings); }
        catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
        return Ok(await BuildPreview(rows, store, ShopifyRetailerImportParser.Hash(bytes), selections, token));
    }

    [HttpPost("commit"), HasPermission(Permissions.Product.Create), RequestSizeLimit(10 * 1024 * 1024), Consumes("multipart/form-data")]
    public async Task<IActionResult> Commit([FromForm] IFormFile file, [FromForm] string sourceStore,
        [FromForm] string selectionsJson, [FromForm] string expectedPreviewHash, CancellationToken token)
    {
        if (file is null || file.Length is <= 0 or > 10 * 1024 * 1024) return BadRequest(new { message = "Upload a CSV or XLSX file up to 10 MB." });
        if (!ShopifySourceIdentity.TryNormalize(sourceStore, out var store)) return BadRequest(new { message = "Shopify source identifier is invalid." });
        if (!TrySelections(selectionsJson, out var selections, out var selectionError)) return BadRequest(new { message = selectionError });
        if (!TryResolveSupplierCostTreatment(store, selections, out selections, out selectionError)) return BadRequest(new { message = selectionError });

        var bytes = await ReadFile(file, token);
        IReadOnlyCollection<ShopifyRetailerProductRow> rows;
        try { rows = ShopifyRetailerImportParser.Parse(bytes, file.FileName, selections.FieldMappings); }
        catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
        var hash = ShopifyRetailerImportParser.Hash(bytes);
        var previewHash = SnapshotHash(store, hash, selections);
        if (!MatchesPreviewSnapshot(expectedPreviewHash, previewHash))
            return Conflict(new { message = "Import mappings or selections changed after preview. Preview the file again before importing." });
        var previous = await db.RetailerShopifyProductImportHistory
            .SingleOrDefaultAsync(x => x.TenantId == Tenant && x.SourceStore == store && x.SourceFileHash == hash, token);
        if (previous is { ErrorCount: 0 }) return Ok(new { alreadyImported = true, createdCount = previous.CreatedCount, updatedCount = previous.UpdatedCount, unchangedCount = previous.UnchangedCount, errorCount = previous.ErrorCount, importId = previous.ImportId });

        var categories = (await products.GetCategoriesAsync(token)).Where(x => x.IsActive && !x.IsDeleted).ToArray();
        var brands = (await products.GetBrandsAsync(token)).Where(x => x.IsActive && !x.IsDeleted).ToArray();
        var units = (await products.GetUnitsAsync(token)).Where(x => x.IsActive && !x.IsDeleted).ToArray();
        if (selections.DefaultBrandId.HasValue && !brands.Any(x => x.BrandId == selections.DefaultBrandId.Value))
            return BadRequest(new { message = "The selected default brand is not active in this retailer." });
        if (selections.UnitId.HasValue && !units.Any(x => x.UnitId == selections.UnitId)) return BadRequest(new { message = "The selected unit is not active in this retailer." });
        var tenant = Tenant;
        var existingSources = await db.RetailerShopifyProductSources.Where(x => x.TenantId == tenant && x.SourceStore == store)
            .ToDictionaryAsync(x => Identity(x.Handle, x.VariantSku), token);
        var productsById = await products.GetManyAsync(existingSources.Values.Select(x => x.ProductId).Distinct().ToArray(), true, token);
        var incomingSkus = rows.Where(x => !string.IsNullOrWhiteSpace(x.Sku)).Select(x => x.Sku!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var incomingSkuKeys = incomingSkus.Select(ShopifyImportIdentityRules.NormalizeSku).ToHashSet(StringComparer.Ordinal);
        var sharedSupplierSkus = ShopifyImportIdentityRules.FindSharedSupplierSkus(
            existingSources.Values.Select(x => (x.Handle, (string?)x.VariantSku)).Concat(rows.Select(x => (x.Handle, x.Sku))))
            .Where(incomingSkuKeys.Contains).ToHashSet(StringComparer.Ordinal);
        var linkedProductIds = (await db.RetailerShopifyProductSources.Where(x => x.TenantId == tenant).Select(x => x.ProductId).Distinct().ToArrayAsync(token)).ToHashSet();
        var reservedInternalSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<object>(); var warnings = new List<object>(); var created = 0; var updated = 0; var unchanged = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            foreach (var row in rows)
            {
                var rowErrors = row.Errors.ToList();
                if (string.IsNullOrWhiteSpace(row.Sku)) rowErrors.Add("Variant SKU is required for retailer product identity.");
                var categoryKey = Normalize(row.Type);
                selections.CategoryByType!.TryGetValue(categoryKey, out var categoryId);
                var manualCategory = categories.SingleOrDefault(x => x.ProductCategoryId == categoryId);
                var automaticCategory = ShopifyCategoryBinding.Match(row.Type, categories);
                var category = manualCategory ?? (automaticCategory.Status == "MATCHED"
                    ? categories.SingleOrDefault(x => x.ProductCategoryId == automaticCategory.ProductCategoryId)
                    : null);
                var vendorKey = Normalize(row.Vendor);
                selections.BrandByVendor!.TryGetValue(vendorKey, out var brandId);
                var brand = brands.SingleOrDefault(x => x.BrandId == brandId)
                    ?? brands.SingleOrDefault(x => Normalize(x.BrandName) == vendorKey);

                existingSources.TryGetValue(Identity(row.Handle, row.Sku ?? string.Empty), out var source);
                Product? product = source is null ? null : productsById.SingleOrDefault(x => x.ProductId == source.ProductId);
                if (source is not null && product is null)
                    rowErrors.Add("The saved Shopify identity points to a missing or unavailable tenant product; resolve it before importing.");
                var isSharedSku = row.Sku is not null && sharedSupplierSkus.Contains(ShopifyImportIdentityRules.NormalizeSku(row.Sku));
                var hasOtherSourceIdentity = source is null && row.Sku is not null && existingSources.Values
                    .Any(x => ShopifyImportIdentityRules.NormalizeSku(x.VariantSku) == ShopifyImportIdentityRules.NormalizeSku(row.Sku));
                var externalSkuMatches = source is null && row.Sku is not null && !isSharedSku && !hasOtherSourceIdentity
                    ? await db.Products.Where(x => x.TenantId == tenant && !x.IsDeleted && x.ProductCode == row.Sku.Trim() && !linkedProductIds.Contains(x.ProductId)).Take(2).ToArrayAsync(token)
                    : [];
                if (externalSkuMatches.Length == 1) product = await products.GetAsync(externalSkuMatches[0].ProductId, true, token);
                else if (externalSkuMatches.Length > 1) rowErrors.Add("SKU matches multiple products in this retailer and is ambiguous.");

                var isNew = product is null;
                if ((isNew || selections.UpdateCategory) && category is null) rowErrors.Add($"Map product category '{row.Type ?? "(blank)"}' to an active retailer category.");
                if ((isNew || selections.UpdateBrand) && brand is null && !selections.DefaultBrandId.HasValue) rowErrors.Add($"Map Shopify vendor '{row.Vendor ?? "(blank)"}' to an active retailer brand or choose a default brand.");
                if ((isNew || selections.UpdateUnit) && !selections.UnitId.HasValue) rowErrors.Add("Choose an active default product unit.");
                if (row.SellingPrice is null) rowErrors.Add("Selling price is required.");
                var selectedSelling = isNew || selections.UpdateSellingPrice ? row.SellingPrice : product!.SellingPrice;
                var selectedCost = SelectOptionalCost(row.ProductCost, product?.PurchasePrice, isNew, selections.UpdateProductCost);
                var selectedPackagingCost = SelectOptionalCost(row.PackagingCost, product?.PackagingCost, isNew, selections.UpdatePackagingCost);
                var selectedShippingCost = SelectOptionalCost(row.ShippingCost, product?.ShippingCost, isNew, selections.UpdateShippingCost);
                var costTreatment = selections.SupplierCostTreatment!;
                var combinedShippingPackagingCost = CombinedShippingPackagingCostForTreatment(costTreatment);
                var rowWarnings = SupplierPricingWarnings(selectedSelling, selectedCost, selectedPackagingCost, selectedShippingCost, costTreatment)
                    .Concat(row.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (row.GstPercentage.HasValue && (isNew || selections.UpdateGst) && (row.GstPercentage is < 0 or > 100)) rowErrors.Add("GST percentage must be between 0 and 100.");
                if (row.Weight.HasValue && row.WeightUnit is not null)
                {
                    try { _ = ShopifyProductWeightProjection.ToProductWeight(row.Weight.Value, row.WeightUnit); }
                    catch (ArgumentException) { rowErrors.Add("Weight unit must be g, kg, oz, or lb."); }
                }

                if (rowErrors.Count > 0) { errors.Add(new { row.RowNumber, row.Handle, row.Sku, errors = rowErrors, warnings = rowWarnings }); continue; }
                var selling = selectedSelling!.Value;
                var internalProductCode = product?.ProductCode ?? await ResolveInternalProductCode(row.Sku!, row.Handle, tenant, store, isSharedSku, reservedInternalSkus, token);
                var unitId = isNew || selections.UpdateUnit ? selections.UnitId!.Value : product!.UnitId;
                var finalCategoryId = isNew || selections.UpdateCategory ? category!.ProductCategoryId : product!.CategoryId;
                var finalBrandId = isNew || selections.UpdateBrand ? (brand?.BrandId ?? selections.DefaultBrandId!.Value) : product!.BrandId;
                var mappedWeight = row.Weight.HasValue && row.WeightUnit is not null
                    ? ShopifyProductWeightProjection.ToProductWeight(row.Weight.Value, row.WeightUnit)
                    : ((decimal Value, string Unit)?)null;
                var specifications = product?.Specifications.Where(x => !x.IsDeleted).Select(x => new ProductSpecificationInput(x.Name, x.Value, x.DisplayOrder)).ToList() ?? [];
                if (mappedWeight.HasValue && (isNew || selections.UpdateWeight))
                {
                    specifications.RemoveAll(x => x.Name == ShopifyProductWeightProjection.WeightUnitSpecificationName);
                    specifications.Add(new ProductSpecificationInput(ShopifyProductWeightProjection.WeightUnitSpecificationName, mappedWeight.Value.Unit, 0));
                }
                var input = new ProductInput(
                    ProductCode: internalProductCode, Barcode: row.Barcode ?? product?.Barcode, ProductName: isNew || selections.UpdateProductName ? row.Title! : product!.ProductName, ShortDescription: product?.ShortDescription,
                    LongDescription: isNew ? row.DescriptionHtml : selections.UpdateDescription && row.DescriptionHtml is not null ? row.DescriptionHtml : product!.LongDescription, CategoryId: finalCategoryId, BrandId: finalBrandId,
                    UnitId: unitId, HSNCode: null, SACCode: product?.SACCode, GSTPercentage: row.GstPercentage.HasValue && (isNew || selections.UpdateGst) ? row.GstPercentage.Value : isNew ? selections.DefaultGstPercentage ?? 0 : product!.GSTPercentage,
                    PurchasePrice: selectedCost ?? 0, SellingPrice: selling,
                    MRP: isNew || selections.UpdateMrp ? row.Mrp ?? product?.MRP ?? selling : product!.MRP,
                    MinimumStock: product?.MinimumStock ?? 0, MaximumStock: product?.MaximumStock ?? 0, ReorderLevel: product?.ReorderLevel ?? 0,
                    Weight: mappedWeight.HasValue && (isNew || selections.UpdateWeight) ? mappedWeight.Value.Value : product?.Weight, Length: product?.Length, Width: product?.Width, Height: product?.Height,
                    IsBatchManaged: product?.IsBatchManaged ?? false, IsSerialManaged: product?.IsSerialManaged ?? false,
                    IsActive: isNew ? selections.ActivateNewProducts : product!.IsActive,
                    BarcodeType: product?.BarcodeType ?? "CUSTOM", AdditionalBarcodes: product?.Barcodes.Where(x => !x.IsDeleted).Select(x => new ProductBarcodeInput(x.Barcode, x.BarcodeType)).ToArray(),
                    IsWhatsAppVisible: product?.IsWhatsAppVisible ?? true, PackSize: product?.PackSize,
                    ReturnPolicyMode: product?.ReturnPolicyMode ?? "INHERIT_DEFAULT", ReturnWindowDays: product?.ReturnWindowDays,
                    Specifications: specifications.ToArray(),
                    ReturnPolicyDescription: product?.ReturnPolicyDescription, GstPricingType: product?.GstPricingType ?? "EXCLUSIVE",
                    PackagingCost: selectedPackagingCost,
                    ShippingCost: selectedShippingCost,
                    CombinedShippingPackagingCost: combinedShippingPackagingCost);

                var before = product is null ? null : new { product.ProductCode, product.Barcode, product.ProductName, product.LongDescription, product.CategoryId, product.BrandId, product.UnitId, product.PurchasePrice, product.PackagingCost, product.ShippingCost, product.CombinedShippingPackagingCost, product.GSTPercentage, product.Weight, product.SellingPrice, product.MRP, product.IsActive };
                var saved = product is null
                    ? await sender.Send(new CreateProductCommand(input), token)
                    : await sender.Send(new UpdateProductCommand(product.ProductId, input), token);
                var entity = await products.GetAsync(saved.ProductId, true, token) ?? throw new InvalidOperationException("Saved product could not be reloaded.");
                var primaryImageUrl = row.Images.FirstOrDefault()?.Url;
                var imageChanged = false;
                if (isNew || selections.UpdateImages) foreach (var image in row.Images)
                {
                    var externalImage = await db.ProductExternalImages.SingleOrDefaultAsync(x => x.TenantId == tenant && x.ProductId == entity.ProductId && x.ImageUrl == image.Url, token);
                    if (externalImage is null) { imageChanged = true; db.ProductExternalImages.Add(new ProductExternalImage { TenantId = tenant, ProductId = entity.ProductId, ImageUrl = image.Url, DisplayOrder = Math.Max(0, image.Position - 1), IsPrimary = image.Url == primaryImageUrl }); }
                    else
                    {
                        var displayOrder = Math.Max(0, image.Position - 1);
                        var isPrimary = image.Url == primaryImageUrl;
                        imageChanged |= externalImage.DisplayOrder != displayOrder || externalImage.IsPrimary != isPrimary;
                        externalImage.DisplayOrder = displayOrder; externalImage.IsPrimary = isPrimary;
                    }
                }
                if (source is null)
                {
                    db.RetailerShopifyProductSources.Add(new RetailerShopifyProductSource { TenantId = tenant, SourceStore = store, Handle = row.Handle, VariantSku = row.Sku!, ProductId = entity.ProductId });
                    linkedProductIds.Add(entity.ProductId);
                }
                else source.ModifiedOn = DateTimeOffset.UtcNow;
                if (isNew) created++;
                else if (before!.ProductCode == entity.ProductCode && before.Barcode == entity.Barcode && before.ProductName == entity.ProductName && before.LongDescription == entity.LongDescription && before.CategoryId == entity.CategoryId && before.BrandId == entity.BrandId && before.UnitId == entity.UnitId && before.PurchasePrice == entity.PurchasePrice && before.PackagingCost == entity.PackagingCost && before.ShippingCost == entity.ShippingCost && before.CombinedShippingPackagingCost == entity.CombinedShippingPackagingCost && before.GSTPercentage == entity.GSTPercentage && before.Weight == entity.Weight && before.SellingPrice == entity.SellingPrice && before.MRP == entity.MRP && before.IsActive == entity.IsActive && !imageChanged) unchanged++;
                else updated++;
                if (rowWarnings.Length > 0) warnings.Add(new { row.RowNumber, row.Handle, row.Sku, warnings = rowWarnings });
            }
            if (previous is null) db.RetailerShopifyProductImportHistory.Add(new RetailerShopifyProductImportHistory { TenantId = tenant, SourceStore = store, SourceFileName = Path.GetFileName(file.FileName), SourceFileHash = hash, CreatedCount = created, UpdatedCount = updated, UnchangedCount = unchanged, ErrorCount = errors.Count, ImportedBy = user.Username });
            else { previous.CreatedCount += created; previous.UpdatedCount += updated; previous.UnchangedCount += unchanged; previous.ErrorCount = errors.Count; previous.ImportedBy = user.Username; previous.ImportedOn = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return Ok(new { alreadyImported = false, createdCount = created, updatedCount = updated, unchangedCount = unchanged, errorCount = errors.Count, errors, warnings });
        }
        catch { await transaction.RollbackAsync(token); throw; }
    }

    private async Task<string> ResolveInternalProductCode(string supplierSku, string handle, Guid tenant, string sourceStore, bool sharedSku,
        HashSet<string> reserved, CancellationToken token)
    {
        var preferred = supplierSku.Trim();
        if (!sharedSku && preferred.Length <= 50 && !reserved.Contains(preferred) &&
            !await db.Products.AnyAsync(x => x.TenantId == tenant && !x.IsDeleted && x.ProductCode == preferred, token))
        {
            reserved.Add(preferred);
            return preferred;
        }

        var generated = ShopifyImportIdentityRules.CreateInternalProductCode(tenant, sourceStore, handle, supplierSku);
        if (reserved.Contains(generated) || await db.Products.AnyAsync(x => x.TenantId == tenant && !x.IsDeleted && x.ProductCode == generated, token))
            throw new InvalidOperationException("A generated internal product SKU already exists; no product was imported. Contact support to resolve the conflicting code.");
        reserved.Add(generated);
        return generated;
    }

    private async Task<object> BuildPreview(IReadOnlyCollection<ShopifyRetailerProductRow> rows, string sourceStore, string hash, ImportSelections selections, CancellationToken token)
    {
        var tenant = Tenant;
        var sources = await db.RetailerShopifyProductSources.AsNoTracking().Where(x => x.TenantId == tenant && x.SourceStore == sourceStore).ToArrayAsync(token);
        var productsById = await products.GetManyAsync(sources.Select(x => x.ProductId).Distinct().ToArray(), false, token);
        var incomingSkus = rows.Where(x => !string.IsNullOrWhiteSpace(x.Sku)).Select(x => x.Sku!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var skuProducts = await db.Products.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted && incomingSkus.Contains(x.ProductCode))
            .Select(x => new { x.ProductId, x.ProductCode, x.Barcode, x.ProductName, x.LongDescription, x.CategoryId, x.BrandId, x.UnitId, x.PurchasePrice, x.PackagingCost, x.ShippingCost, x.CombinedShippingPackagingCost, x.GSTPercentage, x.Weight, x.SellingPrice, x.MRP }).ToArrayAsync(token);
        var linkedProductIds = (await db.RetailerShopifyProductSources.AsNoTracking().Where(x => x.TenantId == tenant)
            .Select(x => x.ProductId).Distinct().ToArrayAsync(token)).ToHashSet();
        var categories = (await products.GetCategoriesAsync(token)).Where(x => x.IsActive && !x.IsDeleted).ToArray();
        var brands = (await products.GetBrandsAsync(token)).Where(x => x.IsActive && !x.IsDeleted).ToArray();
        var units = (await products.GetUnitsAsync(token)).Where(x => x.IsActive && !x.IsDeleted).ToArray();
        var candidateProductIds = productsById.Select(x => x.ProductId).Concat(skuProducts.Select(x => x.ProductId)).Distinct().ToArray();
        var storedImages = await db.ProductExternalImages.AsNoTracking()
            .Where(x => x.TenantId == tenant && candidateProductIds.Contains(x.ProductId))
            .Select(x => new { x.ProductId, x.ImageUrl, x.DisplayOrder, x.IsPrimary }).ToArrayAsync(token);
        var effectiveSelections = selections.UnitId.HasValue || units.Length == 0 ? selections : selections with { UnitId = units[0].UnitId };
        var defaultBrandError = selections.DefaultBrandId.HasValue && !brands.Any(x => x.BrandId == selections.DefaultBrandId.Value)
            ? "The selected default brand is not active in this retailer." : null;
        var selectedUnitError = effectiveSelections.UnitId.HasValue && !units.Any(x => x.UnitId == effectiveSelections.UnitId.Value)
            ? "The selected unit is not active in this retailer." : null;
        var incomingSkuKeys = incomingSkus.Select(ShopifyImportIdentityRules.NormalizeSku).ToHashSet(StringComparer.Ordinal);
        var sharedSupplierSkus = ShopifyImportIdentityRules.FindSharedSupplierSkus(
            sources.Select(x => (x.Handle, (string?)x.VariantSku)).Concat(rows.Select(x => (x.Handle, x.Sku))))
            .Where(incomingSkuKeys.Contains).ToHashSet(StringComparer.Ordinal);
        var previous = await db.RetailerShopifyProductImportHistory.AsNoTracking().AnyAsync(x => x.TenantId == tenant && x.SourceStore == sourceStore && x.SourceFileHash == hash && x.ErrorCount == 0, token);
        var items = rows.Select(row =>
        {
            var source = sources.SingleOrDefault(x => Identity(x.Handle, x.VariantSku) == Identity(row.Handle, row.Sku ?? string.Empty));
            var sourceProduct = source is null ? null : productsById.SingleOrDefault(x => x.ProductId == source.ProductId);
            var isSharedSku = row.Sku is not null && sharedSupplierSkus.Contains(ShopifyImportIdentityRules.NormalizeSku(row.Sku));
            var skuUsedByAnotherSourceIdentity = row.Sku is not null && sources.Any(x => ShopifyImportIdentityRules.NormalizeSku(x.VariantSku) == ShopifyImportIdentityRules.NormalizeSku(row.Sku));
            var skuMatches = source is null && row.Sku is not null && !isSharedSku && !skuUsedByAnotherSourceIdentity
                ? skuProducts.Where(x => x.ProductCode.Equals(row.Sku.Trim(), StringComparison.OrdinalIgnoreCase) && !linkedProductIds.Contains(x.ProductId)).ToArray()
                : [];
            var matchedId = source is null ? skuMatches.Length == 1 ? skuMatches[0].ProductId : (Guid?)null : sourceProduct?.ProductId;
            var matchedName = source is null ? skuMatches.Length == 1 ? skuMatches[0].ProductName : null : sourceProduct?.ProductName;
            var matchedSelling = source is null ? skuMatches.Length == 1 ? skuMatches[0].SellingPrice : (decimal?)null : sourceProduct?.SellingPrice;
            var matchedMrp = source is null ? skuMatches.Length == 1 ? skuMatches[0].MRP : (decimal?)null : sourceProduct?.MRP;
            var matchedProductCode = source is null ? skuMatches.Length == 1 ? skuMatches[0].ProductCode : null : sourceProduct?.ProductCode;
            var matchedBarcode = source is null ? skuMatches.Length == 1 ? skuMatches[0].Barcode : null : sourceProduct?.Barcode;
            var categoryMatch = ShopifyCategoryBinding.Match(row.Type, categories);
            var categoryMatches = categoryMatch.Candidates;
            var vendorMatch = brands.FirstOrDefault(x => Normalize(x.BrandName) == Normalize(row.Vendor));
            var warnings = row.Warnings.ToList();
            if (source is not null && sourceProduct is null) warnings.Add("Saved Shopify identity refers to an unavailable product; resolve the source mapping.");
            if (skuMatches.Length > 1) warnings.Add("SKU matches multiple tenant products and is ambiguous.");
            if (isSharedSku) warnings.Add(ShopifyImportIdentityRules.SharedSupplierSkuWarning);
            var selectedCategoryId = selections.CategoryByType!.GetValueOrDefault(Normalize(row.Type));
            var selectedBrandId = selections.BrandByVendor!.GetValueOrDefault(Normalize(row.Vendor));
            var selectedCategory = categories.SingleOrDefault(x => x.ProductCategoryId == selectedCategoryId)
                ?? (categoryMatch.Status == "MATCHED" ? categories.SingleOrDefault(x => x.ProductCategoryId == categoryMatch.ProductCategoryId) : null);
            var categoryBindingStatus = selectedCategoryId != Guid.Empty && selectedCategory is not null ? "MANUAL"
                : categoryMatch.Status;
            var selectedBrand = brands.SingleOrDefault(x => x.BrandId == selectedBrandId) ?? vendorMatch ?? brands.SingleOrDefault(x => x.BrandId == selections.DefaultBrandId);
            var isNew = !matchedId.HasValue;
            var errors = row.Errors.ToList();
            if (defaultBrandError is not null) errors.Add(defaultBrandError);
            if (string.IsNullOrWhiteSpace(row.Sku)) errors.Add("Variant SKU is required for retailer product identity.");
            if (source is not null && sourceProduct is null) errors.Add("Saved Shopify identity points to an unavailable product.");
            if (skuMatches.Length > 1) errors.Add("SKU matches multiple products in this retailer and is ambiguous.");
            if (categoryMatch.Status == "AMBIGUOUS" && selectedCategory is null) warnings.Add($"Category '{row.Type}' matches multiple active retailer categories; choose one manually.");
            if ((isNew || selections.UpdateCategory) && selectedCategory is null) errors.Add($"Map product category '{row.Type ?? "(blank)"}' to an active retailer category.");
            if ((isNew || selections.UpdateBrand) && selectedBrand is null) errors.Add($"Map Shopify vendor '{row.Vendor ?? "(blank)"}' to an active retailer brand or choose a default brand.");
            if ((isNew || selections.UpdateUnit) && (!effectiveSelections.UnitId.HasValue || selectedUnitError is not null)) errors.Add(selectedUnitError ?? "Choose an active default product unit.");
            var effectiveSellingPrice = isNew || selections.UpdateSellingPrice ? row.SellingPrice : matchedSelling;
            var matchedProductCost = sourceProduct?.PurchasePrice ?? (skuMatches.Length == 1 ? skuMatches[0].PurchasePrice : (decimal?)null);
            var effectiveProductCost = SelectOptionalCost(row.ProductCost, matchedProductCost, isNew, selections.UpdateProductCost);
            var matchedPackagingCost = sourceProduct?.PackagingCost ?? (skuMatches.Length == 1 ? skuMatches[0].PackagingCost : (decimal?)null);
            var matchedShippingCost = sourceProduct?.ShippingCost ?? (skuMatches.Length == 1 ? skuMatches[0].ShippingCost : (decimal?)null);
            var matchedCombinedCost = sourceProduct?.CombinedShippingPackagingCost ?? (skuMatches.Length == 1 ? skuMatches[0].CombinedShippingPackagingCost : (decimal?)null);
            var effectivePackagingCost = SelectOptionalCost(row.PackagingCost, matchedPackagingCost, isNew, selections.UpdatePackagingCost);
            var effectiveShippingCost = SelectOptionalCost(row.ShippingCost, matchedShippingCost, isNew, selections.UpdateShippingCost);
            var effectiveMrp = isNew || selections.UpdateMrp ? row.Mrp ?? matchedMrp ?? effectiveSellingPrice : matchedMrp;
            var pricingPreview = ProjectPricingPreview(row, effectiveSellingPrice, effectiveMrp,
                effectiveProductCost, effectivePackagingCost, effectiveShippingCost, selections.SupplierCostTreatment!);
            warnings.AddRange(pricingPreview.EffectivePricingWarnings);
            var changes = new List<string>();
            if (matchedId.HasValue)
            {
                var currentName = sourceProduct?.ProductName ?? (skuMatches.Length == 1 ? skuMatches[0].ProductName : null);
                var currentDescription = sourceProduct?.LongDescription ?? (skuMatches.Length == 1 ? skuMatches[0].LongDescription : null);
                var currentCategoryId = sourceProduct?.CategoryId ?? (skuMatches.Length == 1 ? skuMatches[0].CategoryId : (Guid?)null);
                var currentBrandId = sourceProduct?.BrandId ?? (skuMatches.Length == 1 ? skuMatches[0].BrandId : (Guid?)null);
                var currentUnitId = sourceProduct?.UnitId ?? (skuMatches.Length == 1 ? skuMatches[0].UnitId : (Guid?)null);
                var currentPurchasePrice = matchedProductCost;
                var currentGst = sourceProduct?.GSTPercentage ?? (skuMatches.Length == 1 ? skuMatches[0].GSTPercentage : (decimal?)null);
                var currentWeight = sourceProduct?.Weight ?? (skuMatches.Length == 1 ? skuMatches[0].Weight : (decimal?)null);
                if (currentName is not null)
                {
                    if (selections.UpdateProductName && row.Title != currentName) changes.Add("Product name");
                    if (row.Barcode is not null && row.Barcode != matchedBarcode) changes.Add("Barcode");
                    if (selections.UpdateDescription && row.DescriptionHtml is not null && row.DescriptionHtml != currentDescription) changes.Add("Description");
                    if (selections.UpdateCategory && selectedCategory is not null && selectedCategory.ProductCategoryId != currentCategoryId) changes.Add("Category");
                    if (selections.UpdateBrand && selectedBrand is not null && selectedBrand.BrandId != currentBrandId) changes.Add("Brand");
                    if (selections.UpdateUnit && selections.UnitId.HasValue && currentUnitId != selections.UnitId.Value) changes.Add("Unit");
                    if (selections.UpdateSellingPrice && row.SellingPrice.HasValue && matchedSelling != row.SellingPrice.Value) changes.Add("Selling Price");
                    if (selections.UpdateMrp && row.Mrp.HasValue && matchedMrp != row.Mrp.Value) changes.Add("MRP");
                    if (selections.UpdateProductCost && row.ProductCost.HasValue && currentPurchasePrice != row.ProductCost.Value) changes.Add("Supplier product cost");
                    if (selections.UpdatePackagingCost && row.PackagingCost.HasValue && matchedPackagingCost != row.PackagingCost.Value) changes.Add("Packaging cost");
                    if (selections.UpdateShippingCost && row.ShippingCost.HasValue && matchedShippingCost != row.ShippingCost.Value) changes.Add("Shipping cost");
                    var expectedCombinedCost = CombinedShippingPackagingCostForTreatment(selections.SupplierCostTreatment!);
                    if (matchedCombinedCost != expectedCombinedCost) changes.Add("Supplier cost treatment");
                    if (selections.UpdateGst && row.GstPercentage.HasValue && currentGst != row.GstPercentage.Value) changes.Add("GST percentage");
                    if (selections.UpdateWeight && row.Weight.HasValue && currentWeight != row.Weight.Value) changes.Add("Weight");
                    if (selections.UpdateImages && row.Images.Count > 0)
                    {
                        var currentImages = storedImages.Where(x => x.ProductId == matchedId.Value).ToArray();
                        var needsImageUpdate = row.Images.Any(image =>
                        {
                            var current = currentImages.SingleOrDefault(x => x.ImageUrl == image.Url);
                            return current is null || current.DisplayOrder != Math.Max(0, image.Position - 1) || current.IsPrimary != (image.Url == row.Images.First().Url);
                        });
                        if (needsImageUpdate) changes.Add($"{row.Images.Count} image(s)");
                    }
                }
            }
            var status = errors.Count > 0 ? "INVALID"
                : matchedId is null ? "NEW" : changes.Count == 0 ? "UNCHANGED" : "UPDATE";
            var canUseSupplierSkuAsInternal = row.Sku is not null && row.Sku.Trim().Length <= 50 && !isSharedSku && !skuUsedByAnotherSourceIdentity &&
                !skuProducts.Any(x => x.ProductCode.Equals(row.Sku.Trim(), StringComparison.OrdinalIgnoreCase));
            var plannedInternalProductCode = matchedProductCode ?? (row.Sku is null ? null : canUseSupplierSkuAsInternal
                ? row.Sku.Trim()
                : ShopifyImportIdentityRules.CreateInternalProductCode(tenant, sourceStore, row.Handle, row.Sku));
            return new { row.RowNumber, row.Handle, row.Title, row.DescriptionHtml, row.Type, row.Vendor, row.Sku, internalProductCode = plannedInternalProductCode, row.Barcode,
                importedSellingPrice = pricingPreview.ImportedSellingPrice, row.SellingPrice, erpSellingPrice = row.SellingPrice,
                importedMrp = pricingPreview.ImportedMrp, row.Mrp, erpMrp = row.Mrp,
                productCost = row.ProductCost, supplierProductCost = row.ProductCost, packagingCost = row.PackagingCost, shippingCost = row.ShippingCost,
                effectiveSupplierProductCost = pricingPreview.EffectiveSupplierProductCost,
                effectivePackagingCost = pricingPreview.EffectivePackagingCost, effectiveShippingCost = pricingPreview.EffectiveShippingCost,
                importedTotalSupplierCost = pricingPreview.ImportedTotalSupplierCost,
                importedProfitLoss = pricingPreview.ImportedProfitLoss, importedMarginPercentage = pricingPreview.ImportedMarginPercentage,
                estimatedTotalSupplierCost = pricingPreview.EstimatedTotalSupplierCost,
                estimatedProfitLoss = pricingPreview.EstimatedProfitLoss, estimatedMarginPercentage = pricingPreview.EstimatedMarginPercentage,
                effectiveSellingPrice = pricingPreview.EffectiveSellingPrice, effectiveMrp = pricingPreview.EffectiveMrp,
                importedPricingWarnings = pricingPreview.ImportedPricingWarnings, effectivePricingWarnings = pricingPreview.EffectivePricingWarnings,
                shippingCostIsEstimate = effectiveShippingCost.HasValue, row.GstPercentage, row.Weight, row.WeightUnit, status,
                currentProductId = matchedId, currentProductName = matchedName, currentSellingPrice = matchedSelling, currentMrp = matchedMrp,
                categoryMatches, categoryBindingStatus, categorySourceColumn = row.CategorySourceColumn,
                productCostSourceColumn = row.ProductCostSourceColumn, packagingCostSourceColumn = row.PackagingCostSourceColumn,
                shippingCostSourceColumn = row.ShippingCostSourceColumn,
                matchedBrandId = selectedBrand?.BrandId, selectedCategoryId = selectedCategory?.ProductCategoryId, imageCount = row.Images.Count, images = row.Images,
                primaryImage = row.Images.FirstOrDefault()?.Url, additionalImageCount = Math.Max(0, row.Images.Count - 1), fieldChanges = changes,
                errors, warnings, storefrontPublication = "NOT READ OR MODIFIED" };
        }).ToArray();
        return new { sourceStore, sourceFileHash = hash, supplierCostTreatment = effectiveSelections.SupplierCostTreatment, mappingSnapshotHash = SnapshotHash(sourceStore, hash, effectiveSelections), effectiveUnitId = effectiveSelections.UnitId, fieldMappings = effectiveSelections.FieldMappings,
            alreadyImported = previous, categories = ShopifyCategoryBinding.Options(categories), brands = brands.Select(x => new { x.BrandId, x.BrandName }), units = units.Select(x => new { x.UnitId, x.UnitName, x.ShortName }), total = items.Length, newCount = items.Count(x => x.status == "NEW"), updateCount = items.Count(x => x.status == "UPDATE"), unchangedCount = items.Count(x => x.status == "UNCHANGED"), invalidCount = items.Count(x => x.status == "INVALID"), sharedSupplierSkuCount = sharedSupplierSkus.Count, duplicateSkuCount = sharedSupplierSkus.Count, items };
    }

    private static string Identity(string handle, string sku) => ShopifyImportIdentityRules.SourceIdentityKey(handle, sku);
    private static string Normalize(string? value) => string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
    internal static bool TrySelections(string? json, out ImportSelections selections, out string error)
    {
        error = "Import selections are invalid.";
        try
        {
            var parsed = string.IsNullOrWhiteSpace(json) ? new ImportSelections() : JsonSerializer.Deserialize<ImportSelections>(json, SelectionsJsonOptions) ?? throw new JsonException();
            var categoryEntries = (parsed.CategoryByType ?? []).Select(x => (Key: Normalize(x.Key), x.Value)).ToArray();
            var brandEntries = (parsed.BrandByVendor ?? []).Select(x => (Key: Normalize(x.Key), x.Value)).ToArray();
            if (categoryEntries.Any(x => x.Key.Length == 0) || categoryEntries.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != categoryEntries.Length || categoryEntries.Length > 200 || categoryEntries.Any(x => x.Value == Guid.Empty) ||
                brandEntries.Any(x => x.Key.Length == 0) || brandEntries.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != brandEntries.Length || brandEntries.Length > 200 || brandEntries.Any(x => x.Value == Guid.Empty))
                throw new JsonException();
            var mappings = parsed.FieldMappings is null ? ImportSelections.DefaultFieldMappings() : new Dictionary<string, string>(parsed.FieldMappings, StringComparer.Ordinal);
            var supported = ImportSelections.DefaultFieldMappings().Keys.ToHashSet(StringComparer.Ordinal);
            if (mappings.Count > supported.Count || mappings.Any(x => !supported.Contains(x.Key) || x.Value is null || x.Value.Length > 512 || x.Value.Any(char.IsControl))) throw new JsonException();
            mappings = ImportSelections.DefaultFieldMappings().Keys.ToDictionary(key => key, key => mappings.GetValueOrDefault(key, string.Empty), StringComparer.Ordinal);
            var selectedHeaders = mappings.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => Normalize(x.Value)).ToArray();
            if (selectedHeaders.Distinct(StringComparer.Ordinal).Count() != selectedHeaders.Length)
            { error = "Each import field must use a different source column."; throw new InvalidOperationException(); }
            if ((!string.IsNullOrWhiteSpace(mappings["weight"]) && string.IsNullOrWhiteSpace(mappings["weightUnit"])) ||
                (string.IsNullOrWhiteSpace(mappings["weight"]) && !string.IsNullOrWhiteSpace(mappings["weightUnit"])))
            { error = "Map both Weight and Weight Unit, or leave both unmapped."; throw new InvalidOperationException(); }
            if (parsed.DefaultGstPercentage is < 0 or > 100) { error = "Default GST percentage must be between 0 and 100."; throw new InvalidOperationException(); }
            var treatment = string.IsNullOrWhiteSpace(parsed.SupplierCostTreatment) ? null : parsed.SupplierCostTreatment.Trim().ToUpperInvariant();
            if (treatment is not null and not "INCLUSIVE" and not "EXCLUSIVE")
            { error = "Supplier cost treatment must be Inclusive or Exclusive."; throw new InvalidOperationException(); }
            selections = parsed with
            {
                CategoryByType = categoryEntries.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                BrandByVendor = brandEntries.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                FieldMappings = mappings,
                SupplierCostTreatment = treatment
            };
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            selections = new ImportSelections();
            return false;
        }
        catch (InvalidOperationException)
        {
            selections = new ImportSelections();
            return false;
        }
    }
    internal static bool TryResolveSupplierCostTreatment(string sourceStore, ImportSelections selections,
        out ImportSelections resolved, out string error)
    {
        error = string.Empty;
        var mappings = selections.FieldMappings ?? ImportSelections.DefaultFieldMappings();
        if (IsGuturGoSource(sourceStore))
        {
            var guturGoDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sellingPrice"] = "ERP Selling Price", ["mrp"] = "Variant Compare At Price",
                ["productCost"] = "Supplier Product Cost", ["packagingCost"] = "Packaging Cost", ["shippingCost"] = "Shipping Cost"
            };
            foreach (var key in guturGoDefaults.Keys)
                if (string.Equals(Normalize(mappings.GetValueOrDefault(key)), "VARIANTPRICE", StringComparison.Ordinal))
                    mappings[key] = guturGoDefaults[key];
        }
        resolved = selections with
        {
            FieldMappings = mappings,
            SupplierCostTreatment = selections.SupplierCostTreatment ?? (IsGuturGoSource(sourceStore) ? "INCLUSIVE" : "EXCLUSIVE")
        };
        return true;
    }

    internal static bool IsGuturGoSource(string sourceStore)
    {
        var normalized = Normalize(sourceStore);
        return normalized is "GUTURGO" or "GUTURGOMYSHOPIFYCOM";
    }

    internal static decimal? CombinedShippingPackagingCostForTreatment(string supplierCostTreatment) =>
        string.Equals(supplierCostTreatment, "INCLUSIVE", StringComparison.OrdinalIgnoreCase) ? 0m : null;

    internal static string SnapshotHash(string sourceStore, string fileHash, ImportSelections selection)
    {
        var snapshot = new
        {
            sourceStore,
            fileHash,
            selection.UnitId,
            Categories = selection.CategoryByType!.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Brands = selection.BrandByVendor!.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Fields = selection.FieldMappings!.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            selection.DefaultBrandId,
            selection.DefaultGstPercentage,
            selection.ActivateNewProducts,
            selection.UpdateProductName,
            selection.UpdateDescription,
            selection.UpdateImages,
            selection.UpdateCategory,
            selection.UpdateBrand,
            selection.UpdateUnit,
            selection.UpdateSellingPrice,
            selection.UpdateMrp,
            selection.UpdateProductCost,
            selection.UpdatePackagingCost,
            selection.UpdateShippingCost,
            selection.UpdateGst,
            selection.UpdateWeight,
            selection.SupplierCostTreatment
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot))));
    }
    internal static bool MatchesPreviewSnapshot(string? expectedPreviewHash, string currentPreviewHash) =>
        !string.IsNullOrWhiteSpace(expectedPreviewHash) && string.Equals(expectedPreviewHash, currentPreviewHash, StringComparison.OrdinalIgnoreCase);
    internal static decimal? EstimateSupplierTotal(decimal? productCost, decimal? packagingCost, decimal? shippingCost,
        string supplierCostTreatment = "EXCLUSIVE") => string.Equals(supplierCostTreatment, "INCLUSIVE", StringComparison.OrdinalIgnoreCase)
            ? productCost
            : productCost.HasValue && packagingCost.HasValue && shippingCost.HasValue
                ? productCost.Value + packagingCost.Value + shippingCost.Value
                : null;
    internal static decimal? EstimateProfitLoss(decimal? sellingPrice, decimal? totalSupplierCost) =>
        sellingPrice.HasValue && totalSupplierCost.HasValue ? sellingPrice.Value - totalSupplierCost.Value : null;
    internal static decimal? EstimateMarginPercentage(decimal? sellingPrice, decimal? estimatedProfitLoss) =>
        sellingPrice is > 0 && estimatedProfitLoss.HasValue
            ? decimal.Round(estimatedProfitLoss.Value / sellingPrice.Value * 100m, 2, MidpointRounding.AwayFromZero)
            : null;
    internal static IReadOnlyCollection<string> SupplierPricingWarnings(decimal? sellingPrice, decimal? productCost,
        decimal? packagingCost, decimal? shippingCost, string supplierCostTreatment = "EXCLUSIVE")
    {
        if (!sellingPrice.HasValue) return [];
        var total = EstimateSupplierTotal(productCost, packagingCost, shippingCost, supplierCostTreatment);
        var warnings = new List<string>();
        if (total.HasValue && total.Value > sellingPrice.Value)
            warnings.Add("Total supplier cost exceeds the ERP Selling Price.");
        return warnings;
    }

    internal static ShopifyPricingPreview ProjectPricingPreview(ShopifyRetailerProductRow row,
        decimal? effectiveSellingPrice, decimal? effectiveMrp, decimal? effectiveProductCost,
        decimal? effectivePackagingCost, decimal? effectiveShippingCost, string supplierCostTreatment)
    {
        var importedTotal = EstimateSupplierTotal(row.ProductCost, row.PackagingCost, row.ShippingCost, supplierCostTreatment);
        var effectiveTotal = EstimateSupplierTotal(effectiveProductCost, effectivePackagingCost, effectiveShippingCost, supplierCostTreatment);
        var importedProfit = EstimateProfitLoss(row.SellingPrice, importedTotal);
        var effectiveProfit = EstimateProfitLoss(effectiveSellingPrice, effectiveTotal);
        return new ShopifyPricingPreview(
            row.SellingPrice, row.Mrp, effectiveSellingPrice, effectiveMrp,
            row.ProductCost, row.PackagingCost, row.ShippingCost,
            effectiveProductCost, effectivePackagingCost, effectiveShippingCost,
            importedTotal, effectiveTotal, importedProfit, effectiveProfit,
            EstimateMarginPercentage(row.SellingPrice, importedProfit),
            EstimateMarginPercentage(effectiveSellingPrice, effectiveProfit),
            SupplierPricingWarnings(row.SellingPrice, row.ProductCost, row.PackagingCost, row.ShippingCost, supplierCostTreatment),
            SupplierPricingWarnings(effectiveSellingPrice, effectiveProductCost, effectivePackagingCost, effectiveShippingCost, supplierCostTreatment));
    }

    internal sealed record ShopifyPricingPreview(
        decimal? ImportedSellingPrice, decimal? ImportedMrp, decimal? EffectiveSellingPrice, decimal? EffectiveMrp,
        decimal? ImportedSupplierProductCost, decimal? ImportedPackagingCost, decimal? ImportedShippingCost,
        decimal? EffectiveSupplierProductCost, decimal? EffectivePackagingCost, decimal? EffectiveShippingCost,
        decimal? ImportedTotalSupplierCost, decimal? EstimatedTotalSupplierCost,
        decimal? ImportedProfitLoss, decimal? EstimatedProfitLoss,
        decimal? ImportedMarginPercentage, decimal? EstimatedMarginPercentage,
        IReadOnlyCollection<string> ImportedPricingWarnings, IReadOnlyCollection<string> EffectivePricingWarnings);
    internal static decimal? SelectOptionalCost(decimal? incoming, decimal? existing, bool isNew, bool updateExisting) =>
        incoming.HasValue && (isNew || updateExisting) ? incoming : existing;
    private static async Task<byte[]> ReadFile(IFormFile file, CancellationToken token) { await using var stream = new MemoryStream(); await file.CopyToAsync(stream, token); return stream.ToArray(); }
    public sealed record ImportSelections(
        Guid? UnitId = null,
        Dictionary<string, Guid>? CategoryByType = null,
        Dictionary<string, Guid>? BrandByVendor = null,
        bool ActivateNewProducts = false,
        Dictionary<string, string>? FieldMappings = null,
        Guid? DefaultBrandId = null,
        decimal? DefaultGstPercentage = null,
        bool UpdateProductName = true,
        bool UpdateDescription = true,
        bool UpdateImages = true,
        bool UpdateCategory = true,
        bool UpdateBrand = true,
        bool UpdateUnit = true,
        bool UpdateSellingPrice = true,
        bool UpdateMrp = true,
        bool UpdateProductCost = false,
        bool UpdatePackagingCost = false,
        bool UpdateShippingCost = false,
        bool UpdateGst = false,
        bool UpdateWeight = false,
        string? SupplierCostTreatment = null)
    {
        public static Dictionary<string, string> DefaultFieldMappings() => new(StringComparer.Ordinal)
        {
            ["handle"] = "Handle", ["title"] = "Title", ["sku"] = "Variant SKU", ["description"] = "Body (HTML)",
            ["category"] = "Product Category", ["brand"] = "Vendor", ["barcode"] = "Variant Barcode",
            ["sellingPrice"] = "Variant Price", ["mrp"] = "Variant Compare At Price",
            ["image"] = "Image Src", ["imagePosition"] = "Image Position",
            ["imageAlt"] = "Image Alt Text", ["productCost"] = "Supplier Product Cost", ["packagingCost"] = "Packaging Cost", ["shippingCost"] = "Shipping Cost",
            ["gstPercentage"] = string.Empty, ["weight"] = string.Empty, ["weightUnit"] = string.Empty
        };
    }
}
