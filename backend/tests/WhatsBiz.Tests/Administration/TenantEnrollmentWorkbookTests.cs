using ClosedXML.Excel;
using FluentAssertions;
using WhatsBiz.Infrastructure.Features;

namespace WhatsBiz.Tests.Administration;

public sealed class TenantEnrollmentWorkbookTests
{
    [Fact]
    public void EnrollmentCellsAreResolvedByHeaderWhenColumnsAndRowsMove()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Administrator");
        sheet.Cell("B3").Value = "Email";
        sheet.Cell("D3").Value = "Temporary Password";
        sheet.Cell("F3").Value = "Username";
        sheet.Cell("B6").Value = "admin@example.com";
        sheet.Cell("D6").Value = "Temporary@123";
        sheet.Cell("F6").Value = "retailer.admin";

        var cells = TenantEnrollmentService.DataCells(sheet, ["Username", "Email", "Temporary Password"]);

        cells["Username"].GetString().Should().Be("retailer.admin");
        cells["Email"].GetString().Should().Be("admin@example.com");
        cells["Temporary Password"].GetString().Should().Be("Temporary@123");
    }

    [Fact]
    public void EnrollmentHeadersAllowNonBreakingAndRepeatedSpaces()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Administrator");
        sheet.Cell("A1").Value = "Username";
        sheet.Cell("B1").Value = "Email";
        sheet.Cell("C1").Value = "Temporary\u00a0  Password";
        sheet.Cell("A2").Value = "retailer.admin";
        sheet.Cell("B2").Value = "admin@example.com";
        sheet.Cell("C2").Value = "Temporary@123";

        var cells = TenantEnrollmentService.DataCells(sheet, ["Username", "Email", "Temporary Password"]);

        cells["Temporary Password"].GetString().Should().Be("Temporary@123");
    }
}
