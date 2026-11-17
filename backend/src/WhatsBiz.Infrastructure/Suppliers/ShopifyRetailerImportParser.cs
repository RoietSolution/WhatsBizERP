using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using WhatsBiz.Application.Features.Products.Products;

namespace WhatsBiz.Infrastructure.Suppliers;

public sealed record ShopifyRetailerImage(string Url, int Position, string? AltText);
public sealed record ShopifyRetailerProductRow(int RowNumber, string Handle, string? Title, string? DescriptionHtml,
    string? Type, string? Vendor, string? Sku, string? Barcode, decimal? SellingPrice, decimal? Mrp,
    decimal? ProductCost, decimal? PackagingCost, decimal? ShippingCost, decimal? GstPercentage, decimal? Weight, string? WeightUnit, string? Status,
    IReadOnlyCollection<ShopifyRetailerImage> Images, IReadOnlyCollection<string> Errors,
    IReadOnlyCollection<string> Warnings, string? CategorySourceColumn = null, string? ProductCostSourceColumn = null,
    string? PackagingCostSourceColumn = null, string? ShippingCostSourceColumn = null);

/// <summary>Parser for retailer-owned products; deliberately separate from the supplier catalogue parser.</summary>
public static class ShopifyRetailerImportParser
{
    private const string UnreadableFormula = "\u001fSHOPIFY_UNREADABLE_FORMULA\u001f";
    private const int MaxBytes = 10 * 1024 * 1024;
    private const int MaxRows = 5000;
    private static readonly string[] NonPriceProductFieldKeys = ["title", "description", "category", "brand", "sku", "barcode", "status", "gstPercentage", "weight", "weightUnit"];
    private static readonly string[][] Aliases =
    [
        ["Handle", "Product handle", "External Product Key"],
        ["Title", "Product title", "Name"],
        ["Body (HTML)", "Body HTML", "Description", "Product description"],
        // Product Category is the supported automatic source default. Shopify Type
        // remains selectable explicitly in the UI but is never an automatic fallback.
        ["Product Category", "Category"],
        ["Vendor", "Brand"],
        ["Variant SKU", "SKU", "Variant sku"],
        ["Variant Barcode", "Barcode", "GTIN"],
        ["Variant Price", "Price", "Variant price"],
        ["Variant Compare At Price", "Compare-at price", "Compare At Price", "MRP"],
        ["Image Src", "Image URL", "Image source"],
        ["Image Position", "Image position"],
        ["Image Alt Text", "Alt Text", "Image alt text"],
        ["Status", "Product status"]
    ];
    private static readonly string[] KnownStatuses = ["active", "draft", "archived"];
    private static readonly (string Key, string[] Aliases, bool Required)[] Fields =
    [
        ("handle", Aliases[0], true), ("title", Aliases[1], true), ("description", Aliases[2], false),
        ("category", Aliases[3], false), ("brand", Aliases[4], false), ("sku", Aliases[5], true),
        ("barcode", Aliases[6], false), ("sellingPrice", Aliases[7], true), ("mrp", Aliases[8], false),
        ("image", Aliases[9], false), ("imagePosition", Aliases[10], false), ("imageAlt", Aliases[11], false),
        ("status", Aliases[12], false), ("productCost", ["Supplier Product Cost"], false),
        ("gstPercentage", ["GST Percentage", "GST %", "Tax rate"], false),
        ("weight", ["Variant Grams", "Weight"], false),
        ("weightUnit", ["Variant Weight Unit", "Weight Unit"], false),
        ("packagingCost", ["Packaging Cost"], false), ("shippingCost", ["Shipping Cost"], false)
    ];

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static IReadOnlyCollection<string> ReadHeaders(byte[] bytes, string fileName)
    {
        var records = ReadRecords(bytes, fileName);
        if (records.Count == 0) throw new InvalidDataException("The file has no header row.");
        return records[0].Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }

    public static IReadOnlyCollection<ShopifyRetailerProductRow> Parse(byte[] bytes, string fileName,
        IReadOnlyDictionary<string, string>? fieldMappings = null)
    {
        if (bytes.Length is <= 0 or > MaxBytes) throw new InvalidDataException("The file must be between 1 byte and 10 MB.");
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Upload a Shopify CSV or XLSX file.");
        var records = ReadRecords(bytes, fileName);
        if (records.Count == 0) throw new InvalidDataException("The file has no header row.");
        if (records.Count - 1 > MaxRows) throw new InvalidDataException($"The file may contain at most {MaxRows:N0} data rows.");
        if (fieldMappings is not null && fieldMappings.Keys.Any(key => Fields.All(f => f.Key != key)))
            throw new InvalidDataException("The import contains an unsupported field mapping.");
        if (fieldMappings is not null)
        {
            var duplicateMappings = fieldMappings.Values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize).ToArray();
            if (duplicateMappings.Distinct(StringComparer.Ordinal).Count() != duplicateMappings.Length)
                throw new InvalidDataException("Each import field must use a different source column.");
        }
        if (fieldMappings is not null)
        {
            var sellingColumn = fieldMappings.GetValueOrDefault("sellingPrice");
            foreach (var costKey in new[] { "productCost", "packagingCost", "shippingCost" })
            {
                var costColumn = fieldMappings.GetValueOrDefault(costKey);
                if (!string.IsNullOrWhiteSpace(costColumn) && !string.IsNullOrWhiteSpace(sellingColumn) && Normalize(costColumn) == Normalize(sellingColumn))
                    throw new InvalidDataException("Each import field must use a different source column; supplier costs cannot reuse the ERP Selling Price column.");
            }
        }
        var indexes = Fields.Select(field =>
        {
            var selected = fieldMappings?.GetValueOrDefault(field.Key);
            // Once a mapping snapshot is supplied, an empty optional mapping means
            // explicitly unmapped. This also keeps Shopify publication/status fields
            // out of the retailer import even when the export contains them.
            var explicitlyDisabled = fieldMappings is not null && !field.Required && string.IsNullOrWhiteSpace(selected);
            var matches = explicitlyDisabled ? [] : string.IsNullOrWhiteSpace(selected)
                ? Enumerable.Range(0, records[0].Length).Where(i => field.Aliases.Any(alias => Normalize(alias) == Normalize(records[0][i]))).ToArray()
                : Enumerable.Range(0, records[0].Length).Where(i => Normalize(selected!) == Normalize(records[0][i])).ToArray();
            // The visible default is the legacy Shopify header; keep V92's supported
            // current-export aliases working unless the retailer explicitly selected a
            // different source column.
            if (matches.Length == 0 && fieldMappings is not null && !explicitlyDisabled &&
                field.Aliases.Length > 0 && Normalize(selected!) == Normalize(field.Aliases[0]))
                matches = Enumerable.Range(0, records[0].Length).Where(i => field.Aliases.Any(alias => Normalize(alias) == Normalize(records[0][i]))).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException($"Source column for '{field.Key}' is ambiguous because the file contains duplicate matching headers.");
            var index = matches.Length == 1 ? matches[0] : -1;
            if (index < 0 && field.Required)
                throw new InvalidDataException($"Required Shopify mapping '{field.Key}' was not found. Check the selected source column.");
            return index;
        }).ToArray();

        string? Cell(string[] row, int index) => index >= 0 && index < row.Length && !string.IsNullOrWhiteSpace(row[index]) && row[index] != UnreadableFormula ? row[index].Trim() : null;
        string? RawCell(string[] row, int index) => index >= 0 && index < row.Length && !string.IsNullOrWhiteSpace(row[index]) && row[index] != UnreadableFormula ? row[index] : null;
        string? CostCell(string[] row, int index) => index >= 0 && index < row.Length && !string.IsNullOrWhiteSpace(row[index]) ? row[index] : null;
        var nonPriceProductFieldIndexes = NonPriceProductFieldKeys
            .Select(key => Array.FindIndex(Fields, field => field.Key == key))
            .Where(fieldIndex => fieldIndex >= 0)
            .Select(fieldIndex => indexes[fieldIndex])
            .Where(columnIndex => columnIndex >= 0)
            .ToArray();
        var variantIdentityIndexes = records[0].Select((header, index) => (header, index))
            .Where(x => IsVariantIdentityHeader(x.header))
            .Select(x => x.index)
            .ToArray();
        bool IsImageOnlyContinuation(string[] row, string? image) => image is not null
            && nonPriceProductFieldIndexes.All(index => Cell(row, index) is null)
            && !variantIdentityIndexes.Any(index => Cell(row, index) is not null);

        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 1;
        string? previousHandle = null;
        foreach (var row in records.Skip(1))
        {
            rowNumber++;
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            var handle = Cell(row, indexes[0]);
            var title = Cell(row, indexes[1]);
            var rawImage = Cell(row, indexes[9]);
            var imageOnlyContinuation = IsImageOnlyContinuation(row, rawImage);
            if (handle is null)
            {
                // Shopify exports can omit the Handle on an image continuation. Attach it
                // only to the immediately preceding group and never infer a product from price.
                if (imageOnlyContinuation && previousHandle is not null && groups.TryGetValue(previousHandle, out var preceding))
                    preceding.AddImage(row, rowNumber, indexes, Cell, rawImage);
                continue;
            }
            previousHandle = handle;
            if (groups.TryGetValue(handle, out var group) && imageOnlyContinuation)
            {
                // Some retailer exports repeat Handle on gallery-only rows. Those rows
                // may contain formula-filled price/cost columns (including zeroes); none
                // of those values define a variant or may conflict with the primary row.
                group.AddImage(row, rowNumber, indexes, Cell, rawImage);
                continue;
            }
            if (!groups.TryGetValue(handle, out group)) groups[handle] = group = new Group(rowNumber, handle);
            group.Add(row, rowNumber, indexes, Cell, RawCell, CostCell, rawImage);
        }
        string? SourceColumn(string key)
        {
            var fieldIndex = Array.FindIndex(Fields, field => field.Key == key);
            var sourceIndex = fieldIndex >= 0 ? indexes[fieldIndex] : -1;
            return sourceIndex >= 0 ? records[0][sourceIndex].Trim() : null;
        }
        var missingCostWarnings = new List<string>();
        foreach (var (key, label) in new[] { ("productCost", "Supplier Product Cost"), ("packagingCost", "Packaging Cost"), ("shippingCost", "Shipping Estimate") })
        {
            var selected = fieldMappings?.GetValueOrDefault(key);
            var fieldIndex = Array.FindIndex(Fields, field => field.Key == key);
            if (!string.IsNullOrWhiteSpace(selected) && fieldIndex >= 0 && indexes[fieldIndex] < 0)
                missingCostWarnings.Add($"Mapped source column '{selected}' for {label} was not found; no value was imported.");
        }
        return groups.Values.Select(group =>
        {
            var parsed = group.Build();
            return parsed with
            {
                CategorySourceColumn = SourceColumn("category"),
                ProductCostSourceColumn = SourceColumn("productCost"),
                PackagingCostSourceColumn = SourceColumn("packagingCost"),
                ShippingCostSourceColumn = SourceColumn("shippingCost"),
                Warnings = parsed.Warnings.Concat(missingCostWarnings).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }).ToArray();
    }

    private static bool IsVariantIdentityHeader(string header)
    {
        var normalized = Normalize(header);
        return normalized.StartsWith("OPTION", StringComparison.Ordinal)
            || normalized is "VARIANTID" or "VARIANTNAME" or "VARIANTTITLE" or "VARIANTIDENTIFIER"
            || normalized.StartsWith("VARIANTOPTION", StringComparison.Ordinal);
    }

    private sealed class Group(int rowNumber, string handle)
    {
        private readonly List<string> errors = [];
        private readonly List<string> warnings = [];
        private readonly List<ShopifyRetailerImage> images = [];
        private readonly HashSet<string> skus = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> barcodes = new(StringComparer.OrdinalIgnoreCase);
        public string? Title, Description, Type, Vendor, Sku, Barcode, Status, WeightUnit;
        public decimal? Selling, Mrp, ProductCost, PackagingCost, ShippingCost, GstPercentage, Weight;
        private int imageFallbackPosition;

        public void Add(string[] row, int sourceRow, int[] ix, Func<string[], int, string?> cell, Func<string[], int, string?> rawCell,
            Func<string[], int, string?> costCell, string? rawImage)
        {
            Keep(ref Title, cell(row, ix[1]), "Title");
            Keep(ref Description, cell(row, ix[2]), "Description");
            Keep(ref Type, cell(row, ix[3]), "Type / Product Category");
            Keep(ref Vendor, cell(row, ix[4]), "Vendor");
            Keep(ref Status, cell(row, ix[12]), "Status");
            // Keep the supplier-provided SKU byte-for-byte at the parsed text level in
            // the source identity. Matching rules normalize it separately.
            var sku = rawCell(row, ix[5]);
            if (sku is not null) { skus.Add(sku); if (sku.Length > 150) errors.Add($"Row {sourceRow}: SKU exceeds 150 characters."); }
            var barcode = cell(row, ix[6]); if (barcode is not null) barcodes.Add(barcode);
            SetDecimal(ref Selling, cell(row, ix[7]), "Variant Price", sourceRow, false);
            SetDecimal(ref Mrp, cell(row, ix[8]), "Variant Compare At Price", sourceRow, true);
            if (sourceRow == rowNumber)
            {
                SetCost(ref ProductCost, costCell(row, ix[13]), "Supplier product cost", sourceRow, ix[13] >= 0);
                SetCost(ref PackagingCost, costCell(row, ix[17]), "Packaging cost", sourceRow, ix[17] >= 0);
                SetCost(ref ShippingCost, costCell(row, ix[18]), "Shipping cost", sourceRow, ix[18] >= 0);
            }
            SetDecimal(ref GstPercentage, cell(row, ix[14]), "GST percentage", sourceRow, true);
            SetDecimal(ref Weight, cell(row, ix[15]), "Weight", sourceRow, true);
            Keep(ref WeightUnit, cell(row, ix[16]), "Weight unit");
            if (GstPercentage is < 0 or > 100) errors.Add($"Row {sourceRow}: GST percentage must be between 0 and 100.");
            AddImage(row, sourceRow, ix, cell, rawImage);
        }

        public void AddImage(string[] row, int sourceRow, int[] ix, Func<string[], int, string?> cell, string? rawImage)
        {
            if (rawImage is null) return;
            var safe = ShopifyCatalogImportParser.SafeHttps(rawImage);
            if (safe is null) errors.Add($"Row {sourceRow}: image URL must be a safe public HTTPS address.");
            else
            {
                var posText = cell(row, ix[10]);
                var position = ++imageFallbackPosition;
                if (posText is not null && (!int.TryParse(posText, NumberStyles.None, CultureInfo.InvariantCulture, out position) || position < 1))
                { errors.Add($"Row {sourceRow}: Image Position must be a positive integer."); position = imageFallbackPosition; }
                images.Add(new(safe, position, cell(row, ix[11])));
            }
        }

        private void Keep(ref string? current, string? next, string field)
        {
            if (next is null) return;
            if (current is not null && !string.Equals(current, next, StringComparison.Ordinal)) warnings.Add($"Conflicting {field} values occur under this handle; the first nonblank value is used.");
            else current ??= next;
        }
        private void SetDecimal(ref decimal? current, string? raw, string field, int row, bool optional, int? maxScale = null)
        {
            if (raw is null) return;
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
            { errors.Add($"Row {row}: {field} must be a valid non-negative amount."); return; }
            if (maxScale.HasValue && (decimal.Round(value, maxScale.Value) != value || value > 99999999999999.9999m))
            { errors.Add($"Row {row}: {field} must fit the supported 18,4 precision."); return; }
            if (current.HasValue && current.Value != value) errors.Add($"Row {row}: multiple variant {field} values cannot be represented by one ERP product; review this Shopify handle.");
            else current ??= value;
        }
        private void SetCost(ref decimal? current, string? raw, string field, int row, bool mapped)
        {
            if (raw == UnreadableFormula)
            {
                warnings.Add($"Row {row}: {field} formula could not be evaluated; no value was imported.");
                return;
            }
            if (raw is null)
            {
                if (mapped) warnings.Add($"Row {row}: {field} is blank on the primary product row; no value was imported.");
                return;
            }
            SetDecimal(ref current, raw, field, row, true, 4);
        }
        public ShopifyRetailerProductRow Build()
        {
            if (Title is null) errors.Add("Product title is missing.");
            if (handle.Length > 250) errors.Add("Shopify Handle exceeds 250 characters.");
            if (skus.Count > 1) errors.Add("This Shopify product has multiple variant SKUs; the ERP product model cannot represent those variants safely.");
            if (barcodes.Count > 1) errors.Add("This Shopify product has multiple variant barcodes; review before import.");
            if (Selling is null) errors.Add("Variant Price is required; blank prices never overwrite existing selling prices.");
            if (Weight.HasValue && string.IsNullOrWhiteSpace(WeightUnit)) errors.Add("A weight unit is required when importing product weight.");
            else if (Weight.HasValue)
            {
                try { _ = ShopifyProductWeightProjection.ToProductWeight(Weight.Value, WeightUnit!); }
                catch (ArgumentException) { errors.Add("Weight unit must be g, kg, oz, or lb."); }
            }
            if (Mrp.HasValue && Selling.HasValue && Mrp.Value < Selling.Value) warnings.Add("MRP is below the selling price; review this price pair.");
            if (Status is not null && !KnownStatuses.Contains(Status, StringComparer.OrdinalIgnoreCase)) warnings.Add("Shopify Status is unrecognized and will not be applied.");
            Sku = skus.Count == 1 ? skus.First() : null; Barcode = barcodes.Count == 1 ? barcodes.First() : null;
            return new(rowNumber, handle, Title, ProductDescriptionSanitizer.Sanitize(Description, 100000), Type, Vendor, Sku, Barcode,
                Selling, Mrp, ProductCost, PackagingCost, ShippingCost, GstPercentage, Weight, WeightUnit, Status,
                images.GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderBy(i => i.Position).First()).OrderBy(x => x.Position).ToArray(),
                errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private static List<string[]> ReadRecords(byte[] bytes, string fileName)
    {
        if (bytes.Length is <= 0 or > MaxBytes) throw new InvalidDataException("The file must be between 1 byte and 10 MB.");
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Upload a Shopify CSV or XLSX file.");
        try
        {
            var records = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ? Csv(bytes) : Workbook(bytes);
            if (records.Count > MaxRows + 1) throw new InvalidDataException($"The file may contain at most {MaxRows:N0} data rows.");
            return records;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        { throw new InvalidDataException("The file is not a valid UTF-8 CSV or XLSX workbook.", ex); }
    }

    private static string Normalize(string value) => string.Concat(value.Trim().Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static List<string[]> Workbook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var book = new XLWorkbook(stream); var sheet = book.Worksheet(1);
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastColumn == 0) return [];
        var rows = new List<string[]>();
        foreach (var row in sheet.RowsUsed())
        {
            var values = new string[lastColumn];
            for (var column = 1; column <= lastColumn; column++)
            {
                var cell = row.Cell(column);
                if (!cell.HasFormula)
                {
                    values[column - 1] = cell.GetFormattedString();
                    continue;
                }
                try
                {
                    // ClosedXML evaluates supported workbook formulas internally. Read the
                    // value (not the formula text) and use invariant numeric text so currency
                    // display formats cannot corrupt monetary parsing.
                    var value = cell.Value;
                    values[column - 1] = value.IsError ? UnreadableFormula
                        : value.IsBlank ? string.Empty
                        : value.IsNumber ? value.GetNumber().ToString(CultureInfo.InvariantCulture)
                        : value.ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    values[column - 1] = UnreadableFormula;
                }
            }
            rows.Add(values);
        }
        return rows;
    }
    private static List<string[]> Csv(byte[] bytes)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); var result = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++) { var c = text[i]; if (quoted) { if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else if (c == '"') quoted = false; else field.Append(c); } else if (c == '"' && field.Length == 0) quoted = true; else if (c == ',') { row.Add(field.ToString()); field.Clear(); } else if (c == '\n') { row.Add(field.ToString().TrimEnd('\r')); result.Add(row.ToArray()); row.Clear(); field.Clear(); } else field.Append(c); }
        if (quoted) throw new InvalidDataException("CSV contains an unterminated quoted field."); if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString().TrimEnd('\r')); result.Add(row.ToArray()); } return result;
    }
}
