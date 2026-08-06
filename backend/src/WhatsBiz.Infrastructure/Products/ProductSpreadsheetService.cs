using ClosedXML.Excel;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Products;

public sealed class ProductSpreadsheetService : IProductSpreadsheetService
{
    private static readonly string[] Headers = ["Product Code", "Barcode", "Product Name", "Category Code", "Brand Code", "Unit Code", "GST %", "Purchase Price", "Selling Price", "MRP", "Active"];
    public byte[] Export(IReadOnlyCollection<Product> products) { using var workbook = new XLWorkbook(); var sheet = workbook.Worksheets.Add("Products"); WriteHeaders(sheet); var row = 2; foreach (var product in products) { sheet.Cell(row, 1).Value = product.ProductCode; sheet.Cell(row, 2).Value = product.Barcode; sheet.Cell(row, 3).Value = product.ProductName; sheet.Cell(row, 4).Value = product.Category.CategoryCode; sheet.Cell(row, 5).Value = product.Brand.BrandCode; sheet.Cell(row, 6).Value = product.Unit.UnitCode; sheet.Cell(row, 7).Value = product.GSTPercentage; sheet.Cell(row, 8).Value = product.PurchasePrice; sheet.Cell(row, 9).Value = product.SellingPrice; sheet.Cell(row, 10).Value = product.MRP; sheet.Cell(row, 11).Value = product.IsActive; row++; } sheet.Columns().AdjustToContents(); return Save(workbook); }
    public byte[] CreateTemplate() { using var workbook = new XLWorkbook(); var sheet = workbook.Worksheets.Add("Products"); WriteHeaders(sheet); sheet.Cell(2, 1).Value = "PRD-001"; sheet.Cell(2, 3).Value = "Sample Product"; sheet.Cell(2, 4).Value = "CATEGORY-CODE"; sheet.Cell(2, 5).Value = "BRAND-CODE"; sheet.Cell(2, 6).Value = "UNIT-CODE"; sheet.Cell(2, 7).Value = 18; sheet.Cell(2, 8).Value = 100; sheet.Cell(2, 9).Value = 120; sheet.Cell(2, 10).Value = 125; sheet.Cell(2, 11).Value = true; sheet.Columns().AdjustToContents(); return Save(workbook); }
    public IReadOnlyCollection<ProductImportRow> Read(byte[] content) { using var stream = new MemoryStream(content); using var workbook = new XLWorkbook(stream); var sheet = workbook.Worksheet(1); var rows = new List<ProductImportRow>(); foreach (var row in sheet.RowsUsed().Skip(1)) { if (row.Cell(1).IsEmpty()) continue; rows.Add(new ProductImportRow(row.RowNumber(), row.Cell(1).GetString().Trim(), EmptyToNull(row.Cell(2).GetString()), row.Cell(3).GetString().Trim(), row.Cell(4).GetString().Trim(), row.Cell(5).GetString().Trim(), row.Cell(6).GetString().Trim(), GetDecimal(row.Cell(7)), GetDecimal(row.Cell(8)), GetDecimal(row.Cell(9)), GetDecimal(row.Cell(10)), !row.Cell(11).IsEmpty() && row.Cell(11).GetBoolean())); } return rows; }
    private static void WriteHeaders(IXLWorksheet sheet) { for (var column = 1; column <= Headers.Length; column++) sheet.Cell(1, column).Value = Headers[column - 1]; var range = sheet.Range(1, 1, 1, Headers.Length); range.Style.Font.Bold = true; range.Style.Fill.BackgroundColor = XLColor.LightBlue; sheet.SheetView.FreezeRows(1); }
    private static byte[] Save(XLWorkbook workbook) { using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray(); }
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal GetDecimal(IXLCell cell) => cell.TryGetValue<decimal>(out var value) ? value : 0;
}
