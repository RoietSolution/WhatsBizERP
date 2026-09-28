using System.Data;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Infrastructure.Payments;
using WhatsBiz.Infrastructure.POS;
using WhatsBiz.Tests.Integration;
using Xunit.Abstractions;

namespace WhatsBiz.Tests.Payments;

[Collection("SQL phase 1 commerce payment")]
public sealed class CommercePaymentRuntimeIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task VerifiedWebhookAndReplayPostOneErpReceipt()
    {
        var connectionString = SqlIntegrationDatabase.ConnectionString;
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var warehouse = Guid.NewGuid();
        var product = Guid.NewGuid();
        var category = Guid.NewGuid();
        var brand = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var tag = $"P1C{Guid.NewGuid():N}"[..19];

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await SeedAsync(connection, tenant, customer, warehouse, product, category, brand, unit, tag);
        var invoice = await CreateHeldInvoiceAsync(connection, tenant, customer, warehouse, product, tag);

        var gateway = new SyntheticRazorpayGateway();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString
        }).Build();
        var service = new CommercePaymentService(configuration, new EphemeralDataProtectionProvider(),
            new SyntheticCurrentUser(tenant), new PaymentGatewayResolver([gateway]), new ErpPaymentPosting());
        await service.SaveRazorpayForTenantAsync(tenant,
            new SaveRazorpayConfiguration("phase1_test_key", "synthetic-only-key", "synthetic-only-webhook", true, true, true),
            tag, default);
        await service.SaveOptionsForTenantAsync(tenant, new SavePaymentOptions(true), tag, default);

        var attempt = await service.CreateAttemptForTenantAsync(tenant,
            new CreatePaymentAttemptInput(invoice, PaymentProviders.Razorpay), tag, default);
        attempt.Payment.Amount.Should().Be(990m);
        attempt.Payment.Status.Should().Be(CommercePaymentStatuses.Pending);
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { payload = new { payment_link = new { entity = new { id = gateway.ProviderReference, notes = new { payment_id = attempt.Payment.PaymentId.ToString("N") } } } } }));

        await service.ProcessRazorpayWebhookAsync(payload, "synthetic-signature", "phase1-event-1", default);
        var first = await SnapshotAsync(connection, invoice, attempt.Payment.PaymentId);
        AssertSingleReceipt(first);
        first.EventCount.Should().Be(1);
        output.WriteLine("FIRST {0}", first);

        // Identical event: provider-event uniqueness returns before settlement.
        await service.ProcessRazorpayWebhookAsync(payload, "synthetic-signature", "phase1-event-1", default);
        var sameEvent = await SnapshotAsync(connection, invoice, attempt.Payment.PaymentId);
        sameEvent.Should().Be(first);
        output.WriteLine("SAME_EVENT {0}", sameEvent);

        // A second event for the same provider payment reaches the PAID guard.
        await service.ProcessRazorpayWebhookAsync(payload, "synthetic-signature", "phase1-event-2", default);
        var laterEvent = await SnapshotAsync(connection, invoice, attempt.Payment.PaymentId);
        AssertSingleReceipt(laterEvent);
        laterEvent.EventCount.Should().Be(2);
        (laterEvent with { EventCount = 1 }).Should().Be(first);
        output.WriteLine("LATER_EVENT {0}", laterEvent);
    }

    private static void AssertSingleReceipt(Snapshot row)
    {
        row.Status.Should().Be("COMPLETED");
        row.CommerceStatus.Should().Be("PAID");
        row.GrandTotal.Should().Be(990m);
        row.PaidAmount.Should().Be(990m);
        row.Balance.Should().Be(0m);
        row.SalesPayments.Should().Be(1);
        row.SalesPaymentAmount.Should().Be(990m);
        row.Applications.Should().Be(1);
        row.ApplicationAmount.Should().Be(990m);
        row.PaymentJournals.Should().Be(1);
        row.SaleJournals.Should().Be(1);
        row.CustomerReceipts.Should().Be(1);
        row.CustomerReceiptAmount.Should().Be(990m);
        row.BankInflows.Should().Be(1);
        row.BankInflowAmount.Should().Be(990m);
        row.CashInflows.Should().Be(0);
        row.InventoryTransactions.Should().Be(1);
        row.MerchandiseSalesCredit.Should().Be(950m);
        row.DeliveryClearingCredit.Should().Be(40m);
        row.PaymentBankDebit.Should().Be(990m);
        row.PaymentCustomerCredit.Should().Be(990m);
    }

    private static async Task SeedAsync(SqlConnection connection, Guid tenant, Guid customer,
        Guid warehouse, Guid product, Guid category, Guid brand, Guid unit, string tag)
    {
        await using var command = new SqlCommand("""
            SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
            SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
            IF DB_NAME()<>N'WhatsBizERP_Phase1CommerceTest' THROW 51000,'Wrong disposable database',1;
            INSERT core.Tenants(TenantId,TenantKey,Name) VALUES(@tenant,@tag,@tag);
            INSERT master.ProductCategories(ProductCategoryId,CategoryCode,CategoryName,DisplayOrder) VALUES(@category,@tag,@tag,1);
            INSERT master.Brands(BrandId,BrandCode,BrandName) VALUES(@brand,@tag,@tag);
            INSERT master.UnitsOfMeasure(UnitId,UnitCode,UnitName,ShortName,DecimalPlaces) VALUES(@unit,@tag,@tag,N'ea',0);
            EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;
            INSERT sales.Customers(CustomerId,CustomerCode,CustomerName,CustomerType,TenantId) VALUES(@customer,@tag,@tag,N'RETAIL',@tenant);
            INSERT inventory.Warehouses(WarehouseId,TenantId,WarehouseCode,WarehouseName,WarehouseTypeId,IsDefault)
            SELECT @warehouse,@tenant,@tag,@tag,WarehouseTypeId,0 FROM inventory.WarehouseTypes WHERE TypeCode=N'GENERAL';
            INSERT master.Products(ProductId,ProductCode,ProductName,CategoryId,BrandId,UnitId,GSTPercentage,PurchasePrice,SellingPrice,MRP,
                MinimumStock,MaximumStock,ReorderLevel,IsBatchManaged,IsSerialManaged,TenantId)
            VALUES(@product,@tag,@tag,@category,@brand,@unit,0,100,1000,1000,0,1000,0,0,0,@tenant);
            INSERT inventory.InventoryBalances(ProductId,WarehouseId,TenantId,QuantityOnHand,AverageCost,LastPurchaseCost)
            VALUES(@product,@warehouse,@tenant,10,100,100);
            """, connection);
        Add(command, "@tenant", tenant); Add(command, "@customer", customer);
        Add(command, "@warehouse", warehouse); Add(command, "@product", product);
        Add(command, "@category", category); Add(command, "@brand", brand);
        Add(command, "@unit", unit); Add(command, "@tag", tag);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> CreateHeldInvoiceAsync(SqlConnection connection, Guid tenant,
        Guid customer, Guid warehouse, Guid product, string tag)
    {
        var items = $"[{{\"ProductId\":\"{product}\",\"Quantity\":1,\"UnitPrice\":1000,\"DiscountPercentage\":0,\"DiscountAmount\":0,\"TaxPercentage\":0}}]";
        await using var command = new SqlCommand("sales.POS_PostInvoice", connection) { CommandType = CommandType.StoredProcedure };
        Add(command, "@WarehouseId", warehouse); Add(command, "@CustomerId", customer);
        Add(command, "@ItemsJson", items); Add(command, "@PaymentsJson", "[]");
        Add(command, "@BillDiscount", 50m); Add(command, "@PromotionDiscountAmount", 50m);
        Add(command, "@AppliedPromotionName", "PHASE1 synthetic offer");
        Add(command, "@DeliveryCharge", 40m); Add(command, "@ServicePincode", "226001");
        Add(command, "@Status", "HELD"); Add(command, "@TenantId", tenant); Add(command, "@CreatedBy", tag);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetDecimal(reader.GetOrdinal("GrandTotal")).Should().Be(990m);
        return reader.GetGuid(reader.GetOrdinal("InvoiceId"));
    }

    private static async Task<Snapshot> SnapshotAsync(SqlConnection connection, Guid invoice, Guid commercePayment)
    {
        await using var command = new SqlCommand("""
            SELECT i.Status,p.Status,i.GrandTotal,i.PaidAmount,i.BalanceAmount,
              (SELECT COUNT(*) FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice),
              (SELECT ISNULL(SUM(s.Amount),0) FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice),
              (SELECT COUNT(*) FROM commerce.PaymentApplications a WHERE a.PaymentId=@payment),
              (SELECT ISNULL(SUM(a.Amount),0) FROM commerce.PaymentApplications a WHERE a.PaymentId=@payment),
              (SELECT COUNT(*) FROM finance.JournalEntries j WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice)),
              (SELECT COUNT(*) FROM finance.JournalEntries j WHERE j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice),
              (SELECT COUNT(*) FROM finance.CustomerLedger l JOIN finance.JournalEntries j ON j.JournalEntryId=l.JournalEntryId WHERE l.EntryType=N'RECEIPT' AND j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice)),
              (SELECT ISNULL(SUM(l.CreditAmount),0) FROM finance.CustomerLedger l JOIN finance.JournalEntries j ON j.JournalEntryId=l.JournalEntryId WHERE l.EntryType=N'RECEIPT' AND j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice)),
              (SELECT COUNT(*) FROM finance.BankBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice)),
              (SELECT ISNULL(SUM(b.AmountIn),0) FROM finance.BankBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice)),
              (SELECT COUNT(*) FROM finance.CashBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice)),
              (SELECT COUNT(*) FROM inventory.InventoryTransactions t WHERE t.ReferenceType=N'SALES_INVOICE' AND t.ReferenceId=@invoice),
              (SELECT ISNULL(SUM(d.CreditAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice AND a.AccountCode=N'SALES'),
              (SELECT ISNULL(SUM(d.CreditAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice AND a.AccountCode=N'DELIVERY_CLEARING'),
              (SELECT ISNULL(SUM(d.DebitAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice) AND a.AccountCode=N'BANK'),
              (SELECT ISNULL(SUM(d.CreditAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN(SELECT s.PaymentId FROM sales.SalesPayments s WHERE s.InvoiceId=@invoice) AND a.AccountCode=N'CUSTOMER'),
              (SELECT COUNT(*) FROM commerce.PaymentProviderEvents e WHERE e.PaymentId=@payment)
            FROM sales.SalesInvoices i JOIN commerce.CommercePayments p ON p.InvoiceId=i.InvoiceId AND p.PaymentId=@payment
            WHERE i.InvoiceId=@invoice;
            """, connection);
        Add(command, "@invoice", invoice); Add(command, "@payment", commercePayment);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
            reader.GetInt32(5), reader.GetDecimal(6), reader.GetInt32(7), reader.GetDecimal(8),
            reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetDecimal(12),
            reader.GetInt32(13), reader.GetDecimal(14), reader.GetInt32(15), reader.GetInt32(16),
            reader.GetDecimal(17), reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20), reader.GetInt32(21));
    }

    private static void Add(SqlCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);

    private sealed record Snapshot(string Status, string CommerceStatus, decimal GrandTotal, decimal PaidAmount,
        decimal Balance, int SalesPayments, decimal SalesPaymentAmount, int Applications, decimal ApplicationAmount,
        int PaymentJournals, int SaleJournals, int CustomerReceipts, decimal CustomerReceiptAmount,
        int BankInflows, decimal BankInflowAmount, int CashInflows, int InventoryTransactions,
        decimal MerchandiseSalesCredit, decimal DeliveryClearingCredit, decimal PaymentBankDebit,
        decimal PaymentCustomerCredit, int EventCount);

    private sealed class SyntheticCurrentUser(Guid tenant) : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid? TenantId => tenant;
        public string? Username => "phase1-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }

    private sealed class SyntheticRazorpayGateway : IPaymentGateway
    {
        private GatewayCreateRequest? request;
        public string Provider => PaymentProviders.Razorpay;
        public string? ProviderReference { get; private set; }

        public Task<GatewayCreateResult> CreatePaymentAsync(PaymentGatewayConfiguration configuration,
            GatewayCreateRequest input, CancellationToken token)
        {
            request = input;
            ProviderReference = $"phase1-link-{input.PaymentId:N}";
            return Task.FromResult(new GatewayCreateResult($"phase1-order-{input.PaymentId:N}", ProviderReference, null, null));
        }

        public Task<GatewayStatusResult> GetPaymentStatusAsync(PaymentGatewayConfiguration configuration,
            string providerReference, CancellationToken token) => throw new NotSupportedException();

        public GatewayWebhookResult VerifyWebhook(PaymentGatewayConfiguration configuration,
            ReadOnlyMemory<byte> rawBody, string signature, string? eventId)
        {
            request.Should().NotBeNull();
            return new(true, eventId, "payment_link.paid", $"phase1-order-{request!.PaymentId:N}",
                ProviderReference, $"phase1-provider-payment-{request.PaymentId:N}", request.Amount,
                request.Currency, true, false);
        }
    }
}

[CollectionDefinition("SQL phase 1 commerce payment", DisableParallelization = true)]
public sealed class Phase1CommercePaymentCollectionDefinition;
