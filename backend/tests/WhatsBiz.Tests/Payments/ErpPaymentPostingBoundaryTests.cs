using FluentAssertions;
using System.Text.RegularExpressions;

namespace WhatsBiz.Tests.Payments;

// Repository-contract tests; database posting cardinality still requires a
// disposable migrated database in the later integration gate.
public sealed class ErpPaymentPostingBoundaryTests
{
    [Fact]
    public void SalePostingSelectsOnlyPaymentsWithoutAnErpReceiptJournal()
    {
        var sale = SaleBranch();
        sale.Should().Contain("p.InvoiceId=@SourceId AND p.Status=N'COMPLETED'");
        sale.Should().Contain("paymentJournal.TenantId=@TenantId");
        sale.Should().Contain("paymentJournal.ReferenceType=N'SALE_PAYMENT'");
        sale.Should().Contain("paymentJournal.ReferenceId=p.PaymentId");
        sale.Should().Contain("paymentJournal.TransactionType=N'SALE_PAYMENT'");
        sale.Should().NotContain("STOREFRONT");
        sale.Should().NotContain("SourceChannel");
    }

    [Fact]
    public void AllSaleReceiptEffectsUseTheSameUnpostedPaymentSet()
    {
        var sale = SaleBranch();
        Regex.Matches(sale, @"JOIN @InlineReceipts receipt ON receipt.PaymentId=p.PaymentId").Count.Should().Be(4);
        sale.Should().Contain("SELECT @Paid=ISNULL(SUM(p.Amount),0) FROM sales.SalesPayments p JOIN @InlineReceipts");
        sale.Should().Contain("IF @Paid>0 INSERT finance.JournalEntryDetails");
        sale.Should().Contain("IF @Paid>0 INSERT finance.CustomerLedger");
        sale.Should().Contain("INSERT finance.CashBook");
        sale.Should().Contain("INSERT finance.BankBook");
    }

    [Theory]
    [InlineData(990, 950, 40)]
    [InlineData(440, 400, 40)]
    [InlineData(500, 500, 0)]
    public void SaleAndReceiptRemainSeparateCommittedComponents(decimal grand, decimal merchandise, decimal delivery)
    {
        // The SQL contract uses GrandTotal for receivable, subtracts committed
        // delivery for Sales, and posts that delivery to its existing clearing.
        var sale = SaleBranch();
        grand.Should().Be(merchandise + delivery);
        sale.Should().Contain("@Net=i.GrandTotal-i.TaxAmount-i.DeliveryCharge");
        sale.Should().Contain("@DeliveryClearing,0,@Delivery");
        Read("database/WhatsBiz.Database/StoredProcedures/Finance_PostPayment.sql")
            .Should().Contain("ELSE INSERT finance.JournalEntryDetails(JournalEntryId,AccountId,DebitAmount,CreditAmount) VALUES(@J,CASE WHEN @Book=N'CASH' THEN @Cash ELSE @Bank END,@Amount,0),(@J,@PartyAccount,0,@Amount)");
    }

    [Fact]
    public void ImmediatePosInlinePaymentStillHasItsSaleReceiptPath()
    {
        var pos = Read("database/WhatsBiz.Database/StoredProcedures/POS_PostInvoice.sql");
        pos.Should().Contain("IF ISJSON(@PaymentsJson)=1 INSERT sales.SalesPayments");
        pos.Should().Contain("IF @Status='COMPLETED' EXEC finance.PostSource");
        pos.Should().NotContain("EXEC finance.PostPayment");
        SaleBranch().Should().Contain("INSERT @InlineReceipts(PaymentId)");
    }

    [Fact]
    public void CommerceAndCollectedCodDelegateToTheSameErpPostingContract()
    {
        var commerce = Read("backend/src/WhatsBiz.Infrastructure/Payments/CommercePaymentService.cs");
        var delivery = Read("backend/src/WhatsBiz.Infrastructure/Delivery/DeliveryService.cs");
        var erp = Read("backend/src/WhatsBiz.Infrastructure/POS/ErpPaymentPosting.cs");
        commerce.Should().Contain("erpPayments.ApplyAsync(c,tx,new(tenant,invoice,method,amount");
        delivery.Should().Contain("erpPayments.ApplyAsync(c,tx,new(tenantId,row.OrderId,method,input.Amount");
        commerce.Should().NotContain("new SqlCommand(\"sales.POS_AddPayment\"");
        delivery.Should().NotContain("new SqlCommand(\"sales.POS_AddPayment\"");
        erp.Should().Contain("new SqlCommand(\"sales.POS_AddPayment\"");
    }

    [Fact]
    public void ErpProcedureChecksInvoiceTenantAndReturnsReceiptIdentity()
    {
        var procedure = Read("database/WhatsBiz.Database/StoredProcedures/POS_AddPayment.sql");
        procedure.Should().Contain("@SessionTenant<>@TenantId");
        procedure.Should().Contain("WHERE InvoiceId=@InvoiceId AND TenantId=@TenantId AND Status NOT IN");
        procedure.Should().Contain("EXEC finance.PostPayment @Domain='SALE',@PaymentId=@PaymentId");
        procedure.Should().Contain("@PaymentId PaymentId");
    }

    [Fact]
    public void CommerceDuplicateAndPaidCancellationGuardsRemain()
    {
        var commerce = Read("backend/src/WhatsBiz.Infrastructure/Payments/CommercePaymentService.cs");
        commerce.Should().Contain("if(status==CommercePaymentStatuses.Paid)return;");
        commerce.Should().Contain("INSERT commerce.PaymentApplications");
        var cancellation = Read("backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCancellationService.cs");
        cancellation.Should().Contain("Paid-order cancellation requires a verified finance reversal");
    }

    private static string SaleBranch()
    {
        var source = Read("database/WhatsBiz.Database/StoredProcedures/Finance_PostSource.sql");
        var start = source.IndexOf("ELSE IF @SourceType=N'SALE'", StringComparison.Ordinal);
        var end = source.IndexOf("ELSE IF @SourceType=N'PURCHASE_RETURN'", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "frontend")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root not found."), path));
    }
}