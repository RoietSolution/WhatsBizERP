using ClosedXML.Excel;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Products;

namespace WhatsBiz.Infrastructure.Products;

public sealed class ProductMasterSpreadsheetService : IProductMasterSpreadsheetService
{
    private static readonly string[] CategoryHeaders = ["Category Code", "Category Name", "Description", "Display Order", "Parent Category Code", "Active"];
    private static readonly string[] BrandHeaders = ["Brand Code", "Brand Name", "Description", "Logo URL", "Active"];
    private static readonly string[] UnitHeaders = ["Unit Code", "Unit Name", "Short Name", "Decimal Places", "Active"];

    public byte[] ExportCategories(IReadOnlyCollection<ProductCategory> rows) => Workbook("Categories", CategoryHeaders, sheet =>
    {
        var byId = rows.ToDictionary(x => x.ProductCategoryId);
        var row = 2;
        foreach (var item in rows.OrderBy(x => x.DisplayOrder).ThenBy(x => x.CategoryName))
        {
            sheet.Cell(row, 1).Value = item.CategoryCode; sheet.Cell(row, 2).Value = item.CategoryName; sheet.Cell(row, 3).Value = item.Description;
            sheet.Cell(row, 4).Value = item.DisplayOrder; sheet.Cell(row, 5).Value = item.ParentCategoryId.HasValue && byId.TryGetValue(item.ParentCategoryId.Value, out var parent) ? parent.CategoryCode : null;
            sheet.Cell(row, 6).Value = item.IsActive; row++;
        }
    });

    public byte[] ExportBrands(IReadOnlyCollection<Brand> rows) => Workbook("Brands", BrandHeaders, sheet =>
    {
        var row = 2; foreach (var item in rows.OrderBy(x => x.BrandName)) { sheet.Cell(row, 1).Value = item.BrandCode; sheet.Cell(row, 2).Value = item.BrandName; sheet.Cell(row, 3).Value = item.Description; sheet.Cell(row, 4).Value = item.Logo; sheet.Cell(row, 5).Value = item.IsActive; row++; }
    });

    public byte[] ExportUnits(IReadOnlyCollection<UnitOfMeasure> rows) => Workbook("Units", UnitHeaders, sheet =>
    {
        var row = 2; foreach (var item in rows.OrderBy(x => x.UnitName)) { sheet.Cell(row, 1).Value = item.UnitCode; sheet.Cell(row, 2).Value = item.UnitName; sheet.Cell(row, 3).Value = item.ShortName; sheet.Cell(row, 4).Value = item.DecimalPlaces; sheet.Cell(row, 5).Value = item.IsActive; row++; }
    });

    public byte[] CategoryTemplate() => Workbook("Categories", CategoryHeaders, sheet => { sheet.Cell(2, 1).Value = "CAT-001"; sheet.Cell(2, 2).Value = "Sample Category"; sheet.Cell(2, 4).Value = 0; sheet.Cell(2, 6).Value = true; });
    public byte[] BrandTemplate() => Workbook("Brands", BrandHeaders, sheet => { sheet.Cell(2, 1).Value = "BRD-001"; sheet.Cell(2, 2).Value = "Sample Brand"; sheet.Cell(2, 5).Value = true; });
    public byte[] UnitTemplate() => Workbook("Units", UnitHeaders, sheet => { sheet.Cell(2, 1).Value = "PCS"; sheet.Cell(2, 2).Value = "Pieces"; sheet.Cell(2, 3).Value = "Pc"; sheet.Cell(2, 4).Value = 0; sheet.Cell(2, 5).Value = true; });

    public IReadOnlyCollection<CategoryImportRow> ReadCategories(byte[] content) => Read(content, row => new CategoryImportRow(row.RowNumber(), Text(row, 1), Text(row, 2), Optional(row, 3), Int(row, 4), Optional(row, 5), Bool(row, 6)));
    public IReadOnlyCollection<BrandImportRow> ReadBrands(byte[] content) => Read(content, row => new BrandImportRow(row.RowNumber(), Text(row, 1), Text(row, 2), Optional(row, 3), Optional(row, 4), Bool(row, 5)));
    public IReadOnlyCollection<UnitImportRow> ReadUnits(byte[] content) => Read(content, row => new UnitImportRow(row.RowNumber(), Text(row, 1), Text(row, 2), Text(row, 3), Int(row, 4), Bool(row, 5)));

    private static IReadOnlyCollection<T> Read<T>(byte[] content, Func<IXLRow, T> map) { using var stream = new MemoryStream(content); using var book = new XLWorkbook(stream); return book.Worksheet(1).RowsUsed().Skip(1).Where(x => !x.Cell(1).IsEmpty()).Select(map).ToArray(); }
    private static byte[] Workbook(string name, string[] headers, Action<IXLWorksheet> write) { using var book = new XLWorkbook(); var sheet = book.AddWorksheet(name); for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i]; var heading = sheet.Range(1, 1, 1, headers.Length); heading.Style.Font.Bold = true; heading.Style.Fill.BackgroundColor = XLColor.LightBlue; sheet.SheetView.FreezeRows(1); write(sheet); sheet.Columns().AdjustToContents(); using var stream = new MemoryStream(); book.SaveAs(stream); return stream.ToArray(); }
    private static string Text(IXLRow row, int column) => row.Cell(column).GetString().Trim();
    private static string? Optional(IXLRow row, int column) { var value = Text(row, column); return value.Length == 0 ? null : value; }
    private static int Int(IXLRow row, int column) => row.Cell(column).TryGetValue<int>(out var value) ? value : 0;
    private static bool Bool(IXLRow row, int column) => row.Cell(column).IsEmpty() || !row.Cell(column).TryGetValue<bool>(out var value) || value;
}
