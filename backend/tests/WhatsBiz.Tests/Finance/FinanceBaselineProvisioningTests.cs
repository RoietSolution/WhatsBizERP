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
        foreach (var code in new[] { "CASH", "BANK", "CUSTOMER", "SUPPLIER", "INVENTORY", "INPUT_GST", "OUTPUT_GST", "SALES", "PURCHASE_RETURN", "SALES_RETURN" })
            baseline.Should().Contain($"N'{code}'");
        baseline.Should().Contain("MERGE finance.Accounts")
            .And.Contain("MERGE finance.AccountGroups")
            .And.Contain("MERGE finance.PaymentModes");
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."), "database", "WhatsBiz.Database", .. parts]));
    }
}
