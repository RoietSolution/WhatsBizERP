using ClosedXML.Excel;

namespace WhatsBiz.Infrastructure.Suppliers;

public static class ShopifyCatalogImportTemplate
{
    private static readonly string[] ShopifyHeaders =
    [
        "Handle", "Title", "Body (HTML)", "Vendor", "Standardized Product Type", "Custom Product Type", "Tags", "Published",
        "Option1 Name", "Option1 Value", "Option2 Name", "Option2 Value", "Option3 Name", "Option3 Value", "Variant SKU",
        "Variant Grams", "Variant Inventory Tracker", "Variant Inventory Qty", "Variant Inventory Policy", "Variant Fulfillment Service",
        "Variant Price", "Variant Compare At Price", "Variant Requires Shipping", "Variant Taxable", "Variant Barcode", "Image Src",
        "Image Position", "Image Alt Text", "Gift Card", "SEO Title", "SEO Description", "Google Shopping / Google Product Category",
        "Google Shopping / Gender", "Google Shopping / Age Group", "Google Shopping / MPN", "Google Shopping / AdWords Grouping",
        "Google Shopping / AdWords Labels", "Google Shopping / Condition", "Google Shopping / Custom Product", "Google Shopping / Custom Label 0",
        "Google Shopping / Custom Label 1", "Google Shopping / Custom Label 2", "Google Shopping / Custom Label 3", "Google Shopping / Custom Label 4",
        "Variant Image", "Variant Weight Unit", "Variant Tax Code", "Cost per item", "Status"
    ];

    public static IReadOnlyList<string> Headers { get; } = [.. ShopifyHeaders, "ERP Selling Price", "ERP MRP"];

    public static byte[] Create()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Shopify Products");
        for (var index = 0; index < Headers.Count; index++)
            sheet.Cell(1, index + 1).Value = Headers[index];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, 1, Headers.Count).SetAutoFilter();
        sheet.Columns().AdjustToContents(1, 60);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
