using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using WhatsBiz.Application.Features.Products.Products;

namespace WhatsBiz.Infrastructure.Suppliers;

public sealed record ShopifyCatalogRow(int RowNumber, string? Title, string? SupplierSku, string? Handle,
    string? Tags, IReadOnlyCollection<string> TagOptions, string? ProductType, string? DescriptionHtml,
    string? DescriptionText, decimal? ProductCost, decimal? CombinedShippingPackagingCost,
    decimal? SupplierAvailableQuantity, decimal? SellingPrice, decimal? GstPercentage, string? BrandName, string? UnitName, string? ImageUrl, IReadOnlyCollection<string> AdditionalImageUrls,
    IReadOnlyCollection<string> MissingFields, IReadOnlyCollection<string> Errors)
{
    public IReadOnlyCollection<ShopifyCatalogImage> Images { get; init; } = [];
    public bool PrimaryImageFallback { get; init; }
    public decimal? WeightGrams { get; init; }
    public string? WeightUnit { get; init; }
    public bool WeightUnitWasDefaulted { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public decimal? Mrp { get; init; }
    public string? ShopifyStatus { get; init; }
    public bool? ProposedIsActive { get; init; }
    public decimal? ErpCostPrice => ProductCost.HasValue && CombinedShippingPackagingCost.HasValue
        ? ProductCost.Value + CombinedShippingPackagingCost.Value
        : null;
}

public sealed record ShopifyCatalogImage(string Url, int Position, bool IsPrimary);

public static class ShopifyCatalogImportParser
{
    private static readonly string[] NonPublicHostSuffixes = [".localhost", ".local", ".internal", ".lan", ".home", ".corp", ".intranet", ".test", ".invalid", ".example"];
    public static readonly IReadOnlyDictionary<string,string> DefaultMappings = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["title"]="Title", ["sku"]="Variant SKU", ["handle"]="Handle", ["tags"]="Tags",
        ["subcategory"]="Custom Product Type", ["description"]="Body (HTML)", ["productCost"]="Cost per item",
        ["combinedCost"]="Variant Price", ["quantity"]="Variant Inventory Qty", ["image"]="Image Src",
        ["sellingPrice"]="ERP Selling Price", ["compareAtPrice"]="Variant Compare At Price", ["mrp"]="ERP MRP", ["gst"]="", ["brand"]="Vendor", ["unit"]="",
        ["imagePosition"]="Image Position", ["weightGrams"]="Variant Grams", ["weightUnit"]="Variant Weight Unit",
        ["status"]="Status"
    };

    public static IReadOnlyCollection<ShopifyCatalogRow> Parse(byte[] content, string fileName, IReadOnlyDictionary<string,string>? mappings = null)
    {
        if (content.Length == 0 || content.Length > 10 * 1024 * 1024) throw new InvalidDataException("The import file must be between 1 byte and 10 MB.");
        var effective = DefaultMappings.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (mappings is not null) foreach (var pair in mappings) effective[pair.Key] = pair.Value;
        // Migrate only the known legacy Shopify price mappings in memory. Other saved
        // supplier mappings remain untouched and are not rewritten by parsing.
        var combined = Normalize(effective.GetValueOrDefault("combinedCost"));
        var selling = Normalize(effective.GetValueOrDefault("sellingPrice"));
        if (string.Equals(combined, "Variant Price", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(combined) && string.Equals(selling, "Variant Price", StringComparison.OrdinalIgnoreCase))
        {
            effective["combinedCost"] = "Variant Price";
            if (string.IsNullOrEmpty(selling) || string.Equals(selling, "Variant Price", StringComparison.OrdinalIgnoreCase))
                effective["sellingPrice"] = "ERP Selling Price";
        }
        if (string.IsNullOrWhiteSpace(combined)) effective["combinedCost"] = "Variant Price";
        if (string.IsNullOrWhiteSpace(selling)) effective["sellingPrice"] = "ERP Selling Price";
        if (string.IsNullOrWhiteSpace(Normalize(effective.GetValueOrDefault("mrp")))) effective["mrp"] = "ERP MRP";
        if (string.IsNullOrWhiteSpace(Normalize(effective.GetValueOrDefault("brand")))) effective["brand"] = "Vendor";
        if (string.Equals(Normalize(effective.GetValueOrDefault("mrp")), "Variant Compare At Price", StringComparison.OrdinalIgnoreCase))
            effective["mrp"] = "ERP MRP";
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only Shopify CSV and XLSX files are supported.");
        List<string[]> records;
        try { records = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ? ReadCsv(content) : ReadWorkbook(content); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        { throw new InvalidDataException("The supplier file is not a valid UTF-8 CSV or XLSX workbook.", ex); }
        if (records.Count > 5001) throw new InvalidDataException("A supplier import may contain at most 5,000 data rows.");
        if (records.Count == 0) return [];
        var headers = records[0];
        var indexes = effective.ToDictionary(x => x.Key, x => string.IsNullOrWhiteSpace(x.Value) ? -1 : Array.FindIndex(headers, h => string.Equals(Normalize(h), Normalize(x.Value), StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        string? Cell(string key, string[] row) => indexes.TryGetValue(key, out var index) && index >= 0 && index < row.Length ? NullIfBlank(row[index]) : null;
        string? RawCell(string key, string[] row) => indexes.TryGetValue(key, out var index) && index >= 0 && index < row.Length && !string.IsNullOrWhiteSpace(row[index]) ? row[index] : null;
        var products = new List<RowBuilder>();
        var productsByIdentity = new Dictionary<string, RowBuilder>(StringComparer.OrdinalIgnoreCase);
        var productsByHandle = new Dictionary<string, List<RowBuilder>>(StringComparer.OrdinalIgnoreCase);
        var imagesByHandle = new Dictionary<string, List<ShopifyCatalogImage>>(StringComparer.OrdinalIgnoreCase);
        var imagesBySku = new Dictionary<string, List<ShopifyCatalogImage>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (row, offset) in records.Skip(1).Select((r,i)=>(r,i)))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var title = StripLeadingSupplierCode(Cell("title",row));
            var sku = Cell("sku", row);
            var normalizedSku = Normalize(sku);
            var handle = Cell("handle", row);
            var tags = RawCell("tags",row);
            var tagOptions = ShopifyCategoryMappingPolicy.ParseTagsAsCategory(tags);
            var shopifyStatus = Cell("status", row);
            var proposedIsActive = ShopifyCatalogStatusPolicy.Parse(shopifyStatus, out var statusError);
            var html = ProductDescriptionSanitizer.Sanitize(Cell("description",row),100000);
            var text = html is null ? null : HtmlToText(html);
            var rawImage = Cell("image",row);
            var image = SafeHttps(rawImage);
            var errors = new List<string>();
            if (statusError is not null) errors.Add(statusError);
            if (rawImage is not null && image is null) errors.Add("Image URL must be a valid public HTTPS URL without credentials or a nonstandard port.");
            if (title?.Length > 250) errors.Add("Product name exceeds 250 characters.");
            if (sku?.Length > 150) errors.Add("Supplier SKU exceeds 150 characters.");
            if (handle?.Length > 250) errors.Add("Source handle exceeds 250 characters.");
            if (tagOptions.Any(x=>x.Length>200)) errors.Add("A category tag exceeds 200 characters.");
            if (Cell("subcategory",row)?.Length > 200) errors.Add("Subcategory exceeds 200 characters.");
            var imagePositionText = Cell("imagePosition", row);
            var imagePosition = offset + 1;
            if (imagePositionText is not null && (!int.TryParse(imagePositionText, NumberStyles.None, CultureInfo.InvariantCulture, out imagePosition) || imagePosition < 1))
            {
                errors.Add("Image Position must be a positive whole number.");
                imagePosition = offset + 1;
            }
            var weightGrams = Decimal(Cell("weightGrams", row), "Variant Grams", errors);
            var weightUnit = Cell("weightUnit", row)?.Trim().ToLowerInvariant();
            var weightUnitWasDefaulted = weightGrams.HasValue && weightUnit is null;
            if (weightUnitWasDefaulted) weightUnit = "kg"; // Shopify's documented display-unit default.
            if (weightUnit is not null && weightUnit is not ("g" or "kg" or "oz" or "lb"))
                errors.Add("Variant Weight Unit must be g, kg, oz, or lb.");
            if (weightUnit is not null && !weightGrams.HasValue)
                errors.Add("Variant Weight Unit is present without Variant Grams.");
            var productCost = Decimal(Cell("productCost", row), "Product Cost", errors);
            var combinedCost = Decimal(Cell("combinedCost", row), "Combined Shipping + Packaging Cost", errors);
            var sellingPrice = Decimal(Cell("sellingPrice", row), "ERP Selling Price", errors);
            var mrp = Decimal(Cell("mrp", row), "ERP MRP", errors);
            var missing = new List<string>();
            if (title is null) missing.Add("Product name");
            if (tagOptions.Count == 0) missing.Add("Shopify Tags (ERP category)");
            if (productCost is null) missing.Add("Cost per item");
            if (combinedCost is null) missing.Add("Variant Price (shipping + packaging)");
            if (sellingPrice is null) missing.Add("ERP Selling Price");
            if (mrp is null) missing.Add("ERP MRP");

            var parsedImage = image is null ? null : new ShopifyCatalogImage(image, imagePosition, imagePosition == 1);
            if (parsedImage is not null)
            {
                if (!string.IsNullOrWhiteSpace(handle)) AddImage(imagesByHandle, Normalize(handle), parsedImage);
                if (!string.IsNullOrWhiteSpace(sku)) AddImage(imagesBySku, normalizedSku, parsedImage);
            }

            // Shopify emits image-only continuation rows. They contribute images to the
            // handle/SKU group, never a standalone product row.
            if (title is null && image is not null) continue;
            if (title is null && sku is null && handle is null && tags is null && image is null) continue;

            var parsed = new ShopifyCatalogRow(offset + 2,title,sku,handle,tags,tagOptions,Cell("subcategory",row),html,text,
                productCost,combinedCost,
                Decimal(Cell("quantity",row),"Supplier Available Quantity",errors),sellingPrice,
                Decimal(Cell("gst",row),"GST percentage",errors),Cell("brand",row),Cell("unit",row),null,[],missing,errors)
            {
                WeightGrams = weightGrams,
                WeightUnit = weightUnit,
                WeightUnitWasDefaulted = weightUnitWasDefaulted,
                CompareAtPrice = Decimal(Cell("compareAtPrice", row), "Variant Compare At Price", errors),
                Mrp = mrp,
                ShopifyStatus = shopifyStatus,
                ProposedIsActive = proposedIsActive
            };
            var identity = !string.IsNullOrWhiteSpace(sku) ? "sku:" + normalizedSku
                : !string.IsNullOrWhiteSpace(handle) ? "handle:" + Normalize(handle)
                : "row:" + (offset + 2).ToString(CultureInfo.InvariantCulture);
            if (productsByIdentity.TryGetValue(identity, out var existing))
            {
                existing.Merge(parsed);
                if (parsedImage is not null) existing.AddImage(parsedImage);
                continue;
            }

            var builder = new RowBuilder(parsed);
            if (parsedImage is not null) builder.AddImage(parsedImage);
            products.Add(builder);
            productsByIdentity.Add(identity, builder);
            if (!string.IsNullOrWhiteSpace(handle))
            {
                var normalizedHandle = Normalize(handle);
                if (!productsByHandle.TryGetValue(normalizedHandle, out var siblings)) productsByHandle[normalizedHandle] = siblings = [];
                siblings.Add(builder);
            }
        }

        foreach (var (handle, images) in imagesByHandle)
            if (productsByHandle.TryGetValue(handle, out var siblings))
                foreach (var sibling in siblings) foreach (var image in images) sibling.AddImage(image);
        foreach (var (sku, images) in imagesBySku)
            if (productsByIdentity.TryGetValue("sku:" + sku, out var product))
                foreach (var image in images) product.AddImage(image);

        return products.Select(product => product.Build()).ToArray();
    }

    private static void AddImage(Dictionary<string, List<ShopifyCatalogImage>> groups, string key, ShopifyCatalogImage image)
    {
        if (!groups.TryGetValue(key, out var images)) groups[key] = images = [];
        images.Add(image);
    }

    private sealed class RowBuilder(ShopifyCatalogRow row)
    {
        private readonly List<ShopifyCatalogImage> images = [];
        private ShopifyCatalogRow current = row;
        public void AddImage(ShopifyCatalogImage image) => images.Add(image);
        public void Merge(ShopifyCatalogRow other)
        {
            var mergeErrors = current.Errors.Concat(other.Errors).ToList();
            if (current.WeightGrams.HasValue && other.WeightGrams.HasValue && current.WeightGrams != other.WeightGrams)
                mergeErrors.Add("Rows with the same product SKU contain conflicting Variant Grams values.");
            if (current.WeightUnit is not null && other.WeightUnit is not null && !string.Equals(current.WeightUnit, other.WeightUnit, StringComparison.OrdinalIgnoreCase))
                mergeErrors.Add("Rows with the same product SKU contain conflicting Variant Weight Unit values.");
            if (current.ProposedIsActive.HasValue && other.ProposedIsActive.HasValue && current.ProposedIsActive != other.ProposedIsActive)
                mergeErrors.Add("Rows with the same product SKU contain conflicting Shopify Status values.");
            var weightUnitWasDefaulted = current.WeightGrams.HasValue && other.WeightGrams.HasValue
                ? current.WeightUnitWasDefaulted && other.WeightUnitWasDefaulted
                : current.WeightGrams.HasValue ? current.WeightUnitWasDefaulted : other.WeightUnitWasDefaulted;
            current = current with
            {
                Title = current.Title ?? other.Title,
                Tags = current.Tags ?? other.Tags,
                TagOptions = current.TagOptions.Count == 0 ? other.TagOptions : current.TagOptions,
                ProductType = current.ProductType ?? other.ProductType,
                DescriptionHtml = current.DescriptionHtml ?? other.DescriptionHtml,
                DescriptionText = current.DescriptionText ?? other.DescriptionText,
                ProductCost = current.ProductCost ?? other.ProductCost,
                CombinedShippingPackagingCost = current.CombinedShippingPackagingCost ?? other.CombinedShippingPackagingCost,
                SupplierAvailableQuantity = current.SupplierAvailableQuantity ?? other.SupplierAvailableQuantity,
                SellingPrice = current.SellingPrice ?? other.SellingPrice,
                GstPercentage = current.GstPercentage ?? other.GstPercentage,
                BrandName = current.BrandName ?? other.BrandName,
                UnitName = current.UnitName ?? other.UnitName,
                WeightGrams = current.WeightGrams ?? other.WeightGrams,
                WeightUnit = current.WeightUnit ?? other.WeightUnit,
                WeightUnitWasDefaulted = weightUnitWasDefaulted,
                CompareAtPrice = current.CompareAtPrice ?? other.CompareAtPrice,
                Mrp = current.Mrp ?? other.Mrp,
                ShopifyStatus = current.ShopifyStatus ?? other.ShopifyStatus,
                ProposedIsActive = current.ProposedIsActive ?? other.ProposedIsActive,
                // A field is still missing only when every merged variant row is missing it.
                MissingFields = current.MissingFields.Intersect(other.MissingFields, StringComparer.OrdinalIgnoreCase).ToArray(),
                Errors = mergeErrors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
        public ShopifyCatalogRow Build()
        {
            var ordered = images
                .GroupBy(image => image.Url, StringComparer.Ordinal)
                .Select(group => group.OrderBy(image => image.Position).First())
                .OrderBy(image => image.Position)
                .ThenBy(image => image.Url, StringComparer.Ordinal)
                .ToArray();
            var primary = ordered.FirstOrDefault(image => image.Position == 1) ?? ordered.FirstOrDefault();
            var projected = ordered.Select(image => image with { IsPrimary = primary is not null && string.Equals(image.Url, primary.Url, StringComparison.Ordinal) }).ToArray();
            return current with
            {
                ImageUrl = primary?.Url,
                AdditionalImageUrls = projected.Where(image => !image.IsPrimary).Select(image => image.Url).ToArray(),
                Images = projected,
                PrimaryImageFallback = primary is not null && primary.Position != 1
            };
        }
    }

    public static string Normalize(string? value) => Regex.Replace((value ?? string.Empty).Trim().TrimStart('\uFEFF'), @"\s+", " ");
    public static IReadOnlyCollection<string> ParseTags(string? tags) => ShopifyCategoryMappingPolicy.ParseTagsAsCategory(tags);
    public static string? StripLeadingSupplierCode(string? title) => string.IsNullOrWhiteSpace(title) ? null : Regex.Replace(title.Trim(), @"^\s*\d{3,}\s*(?:[-|:]\s+|\s+)", string.Empty).Trim();
    public static string? SafeHttps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            return null;

        var host = uri.IdnHost.TrimEnd('.');
        if (host.Length == 0 || host.Contains('%') || host.IndexOf('.') < 1
            || NonPublicHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return null;

        if (IPAddress.TryParse(host, out var address) && !IsPublicAddress(address)) return null;
        var safeUrl = uri.AbsoluteUri;
        return safeUrl.Length <= 2048 ? safeUrl : null;
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return !address.Equals(IPAddress.IPv6Any) && !address.Equals(IPAddress.IPv6Loopback)
                && !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast
                && (bytes[0] & 0xfe) != 0xfc;

        if (bytes.Length != 4) return false;
        var a = bytes[0]; var b = bytes[1]; var c = bytes[2];
        return a is not (0 or 10 or 127) && a < 224
            && !(a == 100 && b is >= 64 and <= 127)
            && !(a == 169 && b == 254)
            && !(a == 172 && b is >= 16 and <= 31)
            && !(a == 192 && b == 168)
            && !(a == 192 && b == 0 && c is 0 or 2)
            && !(a == 198 && b is 18 or 19)
            && !(a == 198 && b == 51 && c == 100)
            && !(a == 203 && b == 0 && c == 113);
    }
    public static string HtmlToText(string html) => System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")).Replace('\u00a0',' ').Trim();
    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal? Decimal(string? value,string name,List<string> errors)
    {
        if (value is null) return null;
        if (decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var result) && result>=0) return result;
        errors.Add($"{name} is not a valid non-negative number."); return null;
    }
    private static List<string[]> ReadWorkbook(byte[] content)
    {
        using var stream=new MemoryStream(content); using var book=new XLWorkbook(stream); var sheet=book.Worksheet(1);
        return sheet.RowsUsed().Select(row=>row.Cells(1,sheet.LastColumnUsed()?.ColumnNumber()??1).Select(c=>c.GetFormattedString()).ToArray()).ToList();
    }
    private static List<string[]> ReadCsv(byte[] content)
    {
        var text=new UTF8Encoding(false,true).GetString(content); var records=new List<string[]>(); var row=new List<string>(); var field=new StringBuilder(); var quoted=false;
        for(var i=0;i<text.Length;i++) { var c=text[i]; if(quoted) { if(c=='"' && i+1<text.Length && text[i+1]=='"'){field.Append('"');i++;} else if(c=='"') quoted=false; else field.Append(c); } else if(c=='"' && field.Length==0) quoted=true; else if(c==','){row.Add(field.ToString());field.Clear();} else if(c=='\n'){row.Add(field.ToString().TrimEnd('\r'));records.Add(row.ToArray());row.Clear();field.Clear();} else field.Append(c); }
        if(quoted) throw new InvalidDataException("CSV contains an unterminated quoted field."); if(field.Length>0||row.Count>0){row.Add(field.ToString().TrimEnd('\r'));records.Add(row.ToArray());} return records;
    }
}
