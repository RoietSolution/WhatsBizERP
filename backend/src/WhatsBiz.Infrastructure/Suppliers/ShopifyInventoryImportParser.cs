using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;

namespace WhatsBiz.Infrastructure.Suppliers;

public sealed record ShopifyInventorySnapshotRow(int RowNumber, string? Handle, string? Sku, string? Location,
    decimal? OnHand, string? HsnCode, string? CountryOfOrigin, string? BinName, IReadOnlyCollection<string> Errors);

public static class ShopifyInventoryImportParser
{
    private const int MaxBytes = 10 * 1024 * 1024;
    // The supplied daily Shopify export contains 8,898 inventory rows. Keep a
    // finite bound while allowing that valid snapshot through the parser.
    private const int MaxRows = 10000;
    private const decimal MaxQuantity = 99999999999999.9999m;
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static IReadOnlyCollection<ShopifyInventorySnapshotRow> Parse(byte[] bytes, string fileName)
    {
        if (bytes.Length is <= 0 or > MaxBytes) throw new InvalidDataException("The inventory file must be between 1 byte and 10 MB.");
        var ext = Path.GetExtension(fileName);
        if (!ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Upload a Shopify inventory CSV or XLSX workbook.");
        List<string[]> records;
        try { records = ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) ? Csv(bytes) : Workbook(bytes); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException) { throw new InvalidDataException("The inventory file is not a valid UTF-8 CSV or XLSX workbook.", ex); }
        if (records.Count == 0) throw new InvalidDataException("The inventory file has no header row.");
        if (records.Count - 1 > MaxRows) throw new InvalidDataException($"The inventory file may contain at most {MaxRows:N0} data rows.");
        var headers = records[0].Select(Normalize).ToArray();
        int Find(params string[] names) => Array.FindIndex(headers, h => names.Select(Normalize).Contains(h, StringComparer.Ordinal));
        var handle = Find("Handle", "Product handle"); var sku = Find("SKU", "Variant SKU"); var location = Find("Location", "Shop Location"); var quantity = Find("On hand (current)", "On hand current");
        if (location < 0 || quantity < 0 || handle < 0 && sku < 0) throw new InvalidDataException("Required Shopify inventory columns are missing. Include Handle or SKU, Location and On hand (current).");
        var hsn = Find("HS Code", "HSN Code"); var coo = Find("COO", "Country of Origin"); var bin = Find("Bin name", "Bin");
        string? Cell(string[] row, int ix) => ix >= 0 && ix < row.Length && !string.IsNullOrWhiteSpace(row[ix]) ? row[ix].Trim() : null;
        string? RawCell(string[] row, int ix) => ix >= 0 && ix < row.Length && !string.IsNullOrWhiteSpace(row[ix]) ? row[ix] : null;
        var result = new List<ShopifyInventorySnapshotRow>(); var rowNumber = 1;
        foreach (var row in records.Skip(1))
        {
            rowNumber++; if (row.All(string.IsNullOrWhiteSpace)) continue;
            var errors = new List<string>(); var qtyText = Cell(row, quantity); decimal? qty = null;
            if (qtyText is null) errors.Add("On hand (current) is required.");
            else if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0 || parsed > MaxQuantity || decimal.Round(parsed, 4) != parsed) errors.Add("On hand (current) must be a non-negative quantity with at most four decimal places and within inventory limits.");
            else qty = parsed;
            var rowHandle = Cell(row, handle); var rowSku = RawCell(row, sku); var rowLocation = Cell(row, location);
            if (rowHandle is null && rowSku is null) errors.Add("Handle or SKU is required for product matching.");
            if (rowLocation is null) errors.Add("Shopify Location is required.");
            result.Add(new(rowNumber, rowHandle, rowSku, rowLocation, qty, Cell(row, hsn), Cell(row, coo), Cell(row, bin), errors));
        }
        return result;
    }

    private static string Normalize(string value) => string.Concat(value.Trim().Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static List<string[]> Workbook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var book = new XLWorkbook(stream); var sheet = book.Worksheet(1); var last = sheet.LastColumnUsed()?.ColumnNumber() ?? 0; if (last == 0) return [];
        var output = new List<string[]>(); foreach (var row in sheet.RowsUsed()) { var cells = new string[last]; for (var i = 1; i <= last; i++) cells[i - 1] = row.Cell(i).HasFormula ? string.Empty : row.Cell(i).GetFormattedString(); output.Add(cells); } return output;
    }
    private static List<string[]> Csv(byte[] bytes)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); var output = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++) { var c = text[i]; if (quoted) { if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else if (c == '"') quoted = false; else field.Append(c); } else if (c == '"' && field.Length == 0) quoted = true; else if (c == ',') { row.Add(field.ToString()); field.Clear(); } else if (c == '\n') { row.Add(field.ToString().TrimEnd('\r')); output.Add(row.ToArray()); row.Clear(); field.Clear(); } else field.Append(c); }
        if (quoted) throw new InvalidDataException("CSV contains an unterminated quoted field."); if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString().TrimEnd('\r')); output.Add(row.ToArray()); } return output;
    }
}
