using FluentAssertions;
#pragma warning disable CA1707

namespace WhatsBiz.Tests.POS;

public sealed class POSFinancePostingRegressionTests
{
    [Fact]
    public void Pos_posts_finance_only_for_completed_invoices()
    {
        var procedure = ReadScript("V18-POS-PostInvoice-TenantHardening.sql");

        procedure.Should().Contain("IF @Status='COMPLETED' EXEC finance.PostSource @TenantId=@TenantId,@SourceType='SALE'");
        procedure.Should().NotContain("EXEC finance.PostSource @SourceType='SALE',@SourceId=@InvoiceId,@CreatedBy=@CreatedBy; COMMIT");
    }

    [Fact]
    public void Held_invoice_transition_remains_the_single_later_finance_posting_path()
    {
        var transition = ReadScript("V2-WC-DEMO-002-ReadinessLifecycle.sql");

        transition.Should().Contain("UPDATE sales.SalesInvoices SET Status=N'COMPLETED'");
        transition.IndexOf("UPDATE sales.SalesInvoices SET Status=N'COMPLETED'", StringComparison.Ordinal)
            .Should().BeLessThan(transition.IndexOf("EXEC finance.PostSource @SourceType=N'SALE'", StringComparison.Ordinal));
    }

    [Fact]
    public void Finance_source_resolution_is_document_and_tenant_scoped()
    {
        var finance = ReadScript("V26-FinanceTenantIsolationAndPostingRepair.sql");

        finance.Should().Contain("FROM sales.SalesInvoices WHERE InvoiceId=@SourceId")
            .And.Contain("i.InvoiceId=@SourceId AND i.TenantId=@TenantId")
            .And.Contain("i.Status NOT IN(N'HELD',N'SUSPENDED',N'CANCELLED',N'VOID')")
            .And.Contain("@TenantId<>@SessionTenant");
    }

    [Fact]
    public void Deployment_reapplies_the_fix_idempotently()
    {
        var migration = ReadScript("V28-DeferredPOSFinancePosting.sql");
        var postDeployment = ReadScript("PostDeployment.sql");

        migration.Should().Contain(":r .\\V18-POS-PostInvoice-TenantHardening.sql");
        postDeployment.Should().Contain(":r .\\V28-DeferredPOSFinancePosting.sql");
        ReadScript("V18-POS-PostInvoice-TenantHardening.sql").Should().Contain("CREATE OR ALTER PROCEDURE [sales].[POS_PostInvoice]");
    }

    private static string ReadScript(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."),
            "database", "WhatsBiz.Database", "Scripts", name));
    }
}
