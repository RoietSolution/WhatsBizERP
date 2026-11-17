using System.Data;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Inventory;
using WhatsBiz.Application.Features.Products.DTOs;
using WhatsBiz.Application.Features.Products.Products;
using WhatsBiz.Domain.Inventory;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Suppliers;
using WhatsBiz.SharedKernel;
using WhatsBiz.Infrastructure.Identity;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/inventory/shopify-import")]
public sealed class ShopifyInventoryImportController(ApplicationDbContext db, ICurrentUserService user, ISender sender, IProductRepository products) : ControllerBase
{
    private Guid Tenant => user.TenantId ?? throw new UnauthorizedAccessException("Tenant context is required.");

    [HttpGet("history"), HasPermission(Permissions.Inventory.View)]
    public async Task<IActionResult> History(CancellationToken token) => Ok(await db.ShopifyInventoryImportHistory.AsNoTracking()
        .Where(x => x.TenantId == Tenant).OrderByDescending(x => x.ImportedOn).Take(30)
        .Select(x => new { x.ImportId, x.SourceStore, x.SourceFileName, x.SourceFileHash, x.WarehouseMappingsJson, x.ResultsJson, x.ImportedRows, x.SkippedRows, x.FailedRows, x.ImportedOn, x.ImportedBy }).ToArrayAsync(token));

    [HttpPost("preview"), HasPermission(Permissions.Inventory.Adjust), RequestSizeLimit(10 * 1024 * 1024), Consumes("multipart/form-data")]
    public async Task<IActionResult> Preview([FromForm] IFormFile file, [FromForm] string sourceStore, [FromForm] string warehouseMappingsJson, CancellationToken token)
    {
        if (!ValidUpload(file) || !ShopifySourceIdentity.TryNormalize(sourceStore, out var store)) return BadRequest(new { message = "Upload a CSV/XLSX inventory file up to 10 MB and enter a valid Shopify source identifier." });
        if (!TryMappings(warehouseMappingsJson, out var mappings)) return BadRequest(new { message = "Warehouse location mappings are invalid." });
        var bytes = await Read(file, token); IReadOnlyCollection<ShopifyInventorySnapshotRow> parsed;
        try { parsed = ShopifyInventoryImportParser.Parse(bytes, file.FileName); } catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
        return Ok(await BuildPreview(parsed, store, mappings, ShopifyInventoryImportParser.Hash(bytes), token));
    }

    [HttpPost("commit"), HasPermission(Permissions.Inventory.Adjust), RequestSizeLimit(10 * 1024 * 1024), Consumes("multipart/form-data")]
    public async Task<IActionResult> Commit([FromForm] IFormFile file, [FromForm] string sourceStore, [FromForm] string warehouseMappingsJson, [FromForm] string expectedRowsJson, [FromForm] bool applyHsnMetadata, CancellationToken token)
    {
        if (!ValidUpload(file) || !ShopifySourceIdentity.TryNormalize(sourceStore, out var store)) return BadRequest(new { message = "Upload a CSV/XLSX inventory file up to 10 MB and enter a valid Shopify source identifier." });
        if (!TryMappings(warehouseMappingsJson, out var mappings)) return BadRequest(new { message = "Warehouse location mappings are invalid." });
        ExpectedRow[] expected;
        try { expected = JsonSerializer.Deserialize<ExpectedRow[]>(expectedRowsJson) ?? []; } catch (JsonException) { return BadRequest(new { message = "Preview snapshot is invalid. Refresh the preview." }); }
        var bytes = await Read(file, token); IReadOnlyCollection<ShopifyInventorySnapshotRow> rows;
        try { rows = ShopifyInventoryImportParser.Parse(bytes, file.FileName); } catch (InvalidDataException ex) { return BadRequest(new { message = ex.Message }); }
        var hash = ShopifyInventoryImportParser.Hash(bytes); var tenant = Tenant;
        var previous = await db.ShopifyInventoryImportHistory.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.SourceStore == store && x.SourceFileHash == hash, token);
        if (previous is not null) return Ok(new { alreadyImported = true, previous.ImportId, previous.ImportedRows, previous.SkippedRows, previous.FailedRows });
        var preview = await ResolveRows(rows, store, mappings, token);
        var errors = preview.Where(x => x.Errors.Count > 0).ToArray();
        if (errors.Length > 0) return Conflict(new { message = "Resolve all inventory preview errors before committing.", rows = errors });
        if (applyHsnMetadata && preview.Any(x => !string.IsNullOrWhiteSpace(x.HsnCode)) && !User.HasClaim(CustomClaimTypes.Permission, Permissions.Product.Edit))
            return Forbid();
        if (expected.Length != preview.Count || expected.Any(e => !preview.Any(p => p.Key == e.Key && p.Current == e.Current))) return Conflict(new { message = "Stock or row mapping changed since preview. Refresh the preview before committing." });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            // The SQL procedure locks and rechecks every exact balance before applying the absolute snapshot.
            var snapshots = preview.Where(x => x.Difference != 0).Select(x => new Snapshot(x.WarehouseId, x.ProductId, x.ZoneId, x.BinId, x.Current, x.Target!.Value, x.UnitCost)).ToArray();
            if (snapshots.Length > 0)
            {
                var connection = (SqlConnection)db.Database.GetDbConnection();
                await using (var context = connection.CreateCommand())
                {
                    context.Transaction = (SqlTransaction)tx.GetDbTransaction();
                    context.CommandText = "EXEC sys.sp_set_session_context @key=N'TenantId', @value=@TenantId";
                    context.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.UniqueIdentifier) { Value = tenant });
                    await context.ExecuteNonQueryAsync(token);
                }
                await using var command = connection.CreateCommand(); command.Transaction = (SqlTransaction)tx.GetDbTransaction();
                command.CommandType = CommandType.StoredProcedure; command.CommandText = "inventory.ShopifySnapshot_Post";
                command.Parameters.Add(new SqlParameter("@SnapshotsJson", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(snapshots) });
                command.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.UniqueIdentifier) { Value = tenant });
                command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 256) { Value = (object?)user.Username ?? DBNull.Value });
                await command.ExecuteNonQueryAsync(token);
            }
            if (applyHsnMetadata)
            {
                foreach (var row in preview.Where(x => x.ProductId != Guid.Empty && !string.IsNullOrWhiteSpace(x.HsnCode)))
                {
                    var product = await products.GetAsync(row.ProductId, true, token) ?? throw new InvalidOperationException("A matched tenant product is no longer available.");
                    if (!string.Equals(product.HSNCode, row.HsnCode, StringComparison.Ordinal))
                        await sender.Send(new UpdateProductCommand(product.ProductId, ProductInputFrom(product, row.HsnCode!)), token);
                }
            }
            var resultJson = JsonSerializer.Serialize(preview.Select(x => new { x.RowNumber, x.Sku, x.ProductName, x.Location, x.HsnCode, x.CountryOfOrigin, x.Current, target = x.Target, adjustment = x.Difference, status = x.Difference == 0 ? "UNCHANGED" : "APPLIED" }));
            db.ShopifyInventoryImportHistory.Add(new ShopifyInventoryImportHistory { TenantId = tenant, SourceStore = store, SourceFileName = Path.GetFileName(file.FileName), SourceFileHash = hash, WarehouseMappingsJson = JsonSerializer.Serialize(mappings), ResultsJson = resultJson, ImportedRows = preview.Count(x => x.Difference != 0), SkippedRows = preview.Count(x => x.Difference == 0), FailedRows = 0, ImportedBy = user.Username });
            await db.SaveChangesAsync(token); await tx.CommitAsync(token);
            return Ok(new { alreadyImported = false, importedRows = preview.Count(x => x.Difference != 0), skippedRows = preview.Count(x => x.Difference == 0), failedRows = 0 });
        }
        catch { await tx.RollbackAsync(token); throw; }
    }

    private async Task<object> BuildPreview(IReadOnlyCollection<ShopifyInventorySnapshotRow> rows, string store, Dictionary<string, Guid> mappings, string hash, CancellationToken token)
    {
        var resolved = await ResolveRows(rows, store, mappings, token);
        var prior = await db.ShopifyInventoryImportHistory.AsNoTracking().AnyAsync(x => x.TenantId == Tenant && x.SourceStore == store && x.SourceFileHash == hash, token);
        return new { sourceStore = store, sourceFileHash = hash, alreadyImported = prior, warehouseOptions = await db.Warehouses.AsNoTracking().Where(x => EF.Property<Guid>(x, "TenantId") == Tenant && x.IsActive && !x.IsDeleted).Select(x => new { x.WarehouseId, x.WarehouseName, x.WarehouseCode }).ToArrayAsync(token), rows = resolved.Select(x => new { x.RowNumber, x.Handle, x.Sku, x.ProductName, x.Location, x.HsnCode, x.CountryOfOrigin, x.WarehouseId, warehouseName = x.WarehouseName, x.Current, target = x.Target, adjustment = x.Difference, status = x.Errors.Count > 0 ? "ERROR" : x.Difference == 0 ? "UNCHANGED" : "READY", errors = x.Errors, warnings = x.Warnings, key = x.Key }) };
    }

    private async Task<List<ResolvedRow>> ResolveRows(IReadOnlyCollection<ShopifyInventorySnapshotRow> rows, string store, Dictionary<string, Guid> mappings, CancellationToken token)
    {
        var tenant = Tenant; var products = await db.Products.AsNoTracking().Where(x => x.TenantId == tenant && !x.IsDeleted).Select(x => new { x.ProductId, x.ProductCode, x.ProductName, x.IsActive }).ToArrayAsync(token);
        var source = await db.RetailerShopifyProductSources.AsNoTracking().Where(x => x.TenantId == tenant && x.SourceStore == store).ToArrayAsync(token);
        var warehouses = await db.Warehouses.AsNoTracking().Where(x => EF.Property<Guid>(x, "TenantId") == tenant && x.IsActive && !x.IsDeleted).ToDictionaryAsync(x => x.WarehouseId, token);
        var balances = await db.InventoryBalances.AsNoTracking().Where(x => EF.Property<Guid>(x, "TenantId") == tenant).Select(x => new { x.ProductId, x.WarehouseId, x.ZoneId, x.BinId, x.BatchNo, x.SerialNo, x.QuantityOnHand, x.QuantityReserved, x.AverageCost, x.RowVersion }).ToArrayAsync(token);
        var warehouseIds = warehouses.Keys.ToArray();
        var bins = await db.WarehouseBins.AsNoTracking().Where(x => x.IsActive && warehouseIds.Contains(x.WarehouseId)).Select(x => new { x.BinId, x.BinName, x.ZoneId, x.WarehouseId }).ToArrayAsync(token);
        var duplicateRows = rows.GroupBy(x => $"{x.Handle}|{x.Sku}|{x.Location}|{x.BinName}", StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ResolvedRow>();
        foreach (var row in rows)
        {
            var errors = row.Errors.ToList(); var warnings = new List<string>(); var locationKey = Normalize(row.Location); mappings.TryGetValue(locationKey, out var warehouseId);
            if (row.Location is not null && (!mappings.ContainsKey(locationKey) || !warehouses.ContainsKey(warehouseId))) errors.Add("Map this Shopify location to an active warehouse owned by this retailer.");
            var match = ShopifyInventoryProductMatcher.Resolve(row.Handle, row.Sku,
                source.Select(x => new ShopifyInventorySourceCandidate(x.Handle, x.VariantSku, x.ProductId)),
                products.Select(x => new ShopifyInventoryProductCandidate(x.ProductId, x.ProductCode)));
            if (match.Status == "NOT_FOUND") errors.Add("No tenant product matches the Shopify handle and SKU.");
            else if (match.Status == "AMBIGUOUS") errors.Add("Product identity is ambiguous; no stock will be changed.");
            else if (match.Status == "UNAVAILABLE") errors.Add("The Shopify product identity is no longer available in this retailer.");
            var product = match.IsResolved ? products.SingleOrDefault(x => x.ProductId == match.ProductId) : null;
            Guid? zoneId = null, binId = null;
            if (row.BinName is not null && warehouseId != Guid.Empty)
            {
                var found = bins.Where(x => x.WarehouseId == warehouseId && x.BinName.Equals(row.BinName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (found.Length != 1) errors.Add("Bin name is missing or ambiguous in the mapped warehouse."); else { binId = found[0].BinId; zoneId = found[0].ZoneId; }
            }
            var stock = product is null ? [] : balances.Where(x => x.ProductId == product.ProductId && x.WarehouseId == warehouseId).ToArray();
            var matching = stock.Where(x => x.ZoneId == zoneId && x.BinId == binId && x.BatchNo is null && x.SerialNo is null).ToArray();
            if (stock.Any(x => x.BatchNo is not null || x.SerialNo is not null)) errors.Add("Batch/serial-controlled inventory cannot be reconciled from this Shopify snapshot.");
            if (stock.Any(x => x.ZoneId != zoneId || x.BinId != binId)) errors.Add("Inventory is split across bins/zones; include an unambiguous Bin name for each snapshot row.");
            if (matching.Length > 1) errors.Add("Multiple balance rows match this product and location; manual reconciliation is required.");
            if (row.Location is not null && product is not null && warehouseId != Guid.Empty && stock.Length == 0 && zoneId is not null) warnings.Add("No current balance exists for this bin; a positive target will create it through the adjustment service.");
            if (row.Location is not null && product is not null && warehouseId != Guid.Empty && row.BinName is null && stock.Any(x => x.ZoneId is not null || x.BinId is not null)) errors.Add("This product has bin-level stock; the Shopify row must identify its bin.");
            var current = matching.SingleOrDefault()?.QuantityOnHand ?? 0m; var cost = matching.SingleOrDefault()?.AverageCost ?? 0m; var target = row.OnHand;
            if (row.Location is not null && duplicateRows.Contains($"{row.Handle}|{row.Sku}|{row.Location}|{row.BinName}")) errors.Add("Duplicate inventory snapshot row; remove duplicates before commit.");
            var key = $"{row.Handle}|{row.Sku}|{locationKey}|{row.BinName}|{product?.ProductId}|{warehouseId}";
            result.Add(new(row.RowNumber,row.Handle,row.Sku,product?.ProductId ?? Guid.Empty,product?.ProductName,row.Location,row.HsnCode,row.CountryOfOrigin,warehouseId,warehouses.TryGetValue(warehouseId,out var wh)?wh.WarehouseName:null,current,target,current-(target??current),cost,zoneId,binId,key,errors,warnings));
        }
        foreach (var collision in result.Where(x => x.ProductId != Guid.Empty && x.WarehouseId != Guid.Empty).GroupBy(x => (x.WarehouseId, x.ProductId, x.ZoneId, x.BinId)).Where(x => x.Count() > 1))
            foreach (var item in collision) item.Errors.Add("Multiple Shopify rows resolve to the same ERP product and warehouse/bin. Map locations differently or remove the duplicate snapshot rows.");
        return result;
    }

    private sealed record ResolvedRow(int RowNumber,string? Handle,string? Sku,Guid ProductId,string? ProductName,string? Location,string? HsnCode,string? CountryOfOrigin,Guid WarehouseId,string? WarehouseName,decimal Current,decimal? Target,decimal Difference,decimal UnitCost,Guid? ZoneId,Guid? BinId,string Key,List<string> Errors,List<string> Warnings);
    private sealed record Snapshot(Guid WarehouseId,Guid ProductId,Guid? ZoneId,Guid? BinId,decimal ExpectedQuantity,decimal TargetQuantity,decimal UnitCost);
    private sealed record ExpectedRow(string Key, decimal Current);
    private static bool ValidUpload(IFormFile? f) => f is { Length: > 0 and <= 10 * 1024 * 1024 };
    private static string Normalize(string? s) => string.Concat((s??string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static bool TryMappings(string? json,out Dictionary<string,Guid> maps) { try { var raw=JsonSerializer.Deserialize<Dictionary<string,Guid>>(json??"")??[]; var normalized=raw.Select(x=>(Key:Normalize(x.Key),x.Value)).ToArray(); if(normalized.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=normalized.Length){maps=[];return false;} maps=normalized.ToDictionary(x=>x.Key,x=>x.Value); return maps.Count<=100 && maps.Values.All(x=>x!=Guid.Empty); } catch(JsonException){maps=[];return false;} }
    private static async Task<byte[]> Read(IFormFile file,CancellationToken t){await using var s=new MemoryStream();await file.CopyToAsync(s,t);return s.ToArray();}
    private static ProductInput ProductInputFrom(WhatsBiz.Domain.Products.Product p, string hsn) => new(p.ProductCode,p.Barcode,p.ProductName,p.ShortDescription,p.LongDescription,p.CategoryId,p.BrandId,p.UnitId,hsn,p.SACCode,p.GSTPercentage,p.PurchasePrice,p.SellingPrice,p.MRP,p.MinimumStock,p.MaximumStock,p.ReorderLevel,p.Weight,p.Length,p.Width,p.Height,p.IsBatchManaged,p.IsSerialManaged,p.IsActive,p.BarcodeType,p.Barcodes.Where(x=>!x.IsPrimary&&!x.IsDeleted).Select(x=>new ProductBarcodeInput(x.Barcode,x.BarcodeType)).ToArray(),p.IsWhatsAppVisible,p.PackSize,p.ReturnPolicyMode,p.ReturnWindowDays,p.Specifications.Where(x=>!x.IsDeleted).Select(x=>new ProductSpecificationInput(x.Name,x.Value,x.DisplayOrder)).ToArray(),p.ReturnPolicyDescription,p.GstPricingType,p.PackagingCost,p.ShippingCost,p.CombinedShippingPackagingCost);
}
