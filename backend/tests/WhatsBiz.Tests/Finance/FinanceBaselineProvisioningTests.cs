using FluentAssertions;

namespace WhatsBiz.Tests.Finance;

public sealed class FinanceBaselineProvisioningTests
{
    [Fact]
    public void ProductionPostDeploymentIncludesIdempotentFinanceBaseline()
    {
        var postDeployment = Read("Scripts", "PostDeployment.sql");
        var baseline = Read("SeedData", "FinanceBaseline.sql");

        postDeployment.Should().Contain(":r ..\\SeedData\\FinanceBaseline.sql");
        foreach (var code in new[] { "CASH", "BANK", "CUSTOMER", "SUPPLIER", "INVENTORY", "INPUT_GST", "OUTPUT_GST", "SALES", "PURCHASE_RETURN", "SALES_RETURN", "STOCK_ADJUST" })
            baseline.Should().Contain($"N'{code}'");
        baseline.Should().Contain("MERGE finance.Accounts")
            .And.Contain("MERGE finance.AccountGroups")
            .And.Contain("MERGE finance.PaymentModes");
    }

    [Fact]
    public void ProductionPostDeploymentIncludesFinalReferenceDataValidation()
    {
        var postDeployment = Read("Scripts", "PostDeployment.sql");
        var validation = Read("Scripts", "RequiredReferenceDataValidation.sql");
        var qaBootstrap = Read("Scripts", "Bootstrap_QA.sql");

        postDeployment.Should().Contain(":r .\\RequiredReferenceDataValidation.sql");
        qaBootstrap.Should().NotContain("MERGE finance.Accounts")
            .And.NotContain("MERGE finance.AccountGroups")
            .And.NotContain("MERGE finance.PaymentModes");
        validation.Should().Contain("finance.AccountGroups")
            .And.Contain("finance.Accounts")
            .And.Contain("finance.PaymentModes")
            .And.Contain("sales.PaymentMethods")
            .And.Contain("purchase.SupplierPaymentTerms")
            .And.Contain("sales.CustomerPaymentTerms")
            .And.Contain("inventory.WarehouseTypes")
            .And.Contain("inventory.InventorySettings")
            .And.Contain("sales.InvoiceSeries")
            .And.Contain("purchase.PurchaseSeries")
            .And.Contain("core.Features")
            .And.Contain("core.Plans")
            .And.Contain("THROW 516");
        foreach (var code in new[] { "CASH", "BANK", "CUSTOMER", "SUPPLIER", "INVENTORY", "INPUT_GST", "OUTPUT_GST", "SALES", "PURCHASE_RETURN", "SALES_RETURN", "STOCK_ADJUST", "V1", "V2", "WHATSAPP_COMMERCE", "V1_DEFAULT", "V2_COMMERCE" })
            validation.Should().Contain($"N'{code}'");
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."), "database", "WhatsBiz.Database", .. parts]));
    }
}
