using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Infrastructure.Payments;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.POS;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WhatsBiz.Tests.Integration;

/// <summary>
/// Service-owned commit tests. They retain uniquely tagged synthetic rows in the explicitly named,
/// operator-provisioned V91 test database. Disposal/replacement remains a separate operator action.
/// </summary>
[Collection(GuturGoMarketplaceSqlGroup.Name)]
public sealed class MarketplaceV91ServiceSqlIntegrationTests
{
    private static readonly Guid TestRunId = Guid.NewGuid();
    private readonly ITestOutputHelper output;
    private const string AuthorizationVariable = "WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_SQL_AUTHORIZATION";
    private const string AuthorizationValue = "I_AUTHORIZE_SYNTHETIC_V91_MARKETPLACE_TEST_DATABASE";
    private const string ConnectionVariable = "ConnectionStrings__GuturGoMarketplaceTests";
    private const string ExpectedServer = "DESKTOP-DQ0868S";
    private const string ExpectedDatabase = "WhatsBizERP_GuturGoMarketplaceTest_V91";

    public MarketplaceV91ServiceSqlIntegrationTests(ITestOutputHelper output) => this.output = output;

    [MarketplaceV91ServiceSqlFact(Timeout = 180_000)]
    public async Task RazorpayMarketplacePaymentUsesParentPayableAndWebhookReplayPostsOnce()
    {
        RequireAuthorization();
        var connectionString = RequireConnectionString();
        var repositoryRoot = FindRepositoryRoot();
        await using var connection = await OpenAndVerifyAsync(connectionString);
        await AssertSyntheticOnlyScopeAsync(connection, repositoryRoot);
        var priorFootprint = await ReadCommittedFixtureFootprintAsync(connection, TestRunId);
        output.WriteLine("Test run {0}; stale retained synthetic footprint: {1}", TestRunId, priorFootprint);
        if (priorFootprint.Tenants != 0)
            throw new InvalidOperationException($"Committed marketplace test found stale synthetic data before writing: {priorFootprint}. Run only against a freshly sanitized and readiness-verified V91 test database; no rows were changed.");
        await using (var guard = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await MarketplaceV85V91SqlIntegrationTests.AssertCanonicalMarketplaceMigrationsAsync(
                connection, guard, repositoryRoot);
            await MarketplaceV85V91SqlIntegrationTests.AssertCheckoutReleaseDisabledAsync(connection, guard);
            await guard.RollbackAsync();
        }

        // Setup is committed deliberately: CommercePaymentService and the webhook processor each
        // open their own connection and transaction. The target database must contain synthetic data only.
        var fixture = MarketplaceV85V91SqlIntegrationTests.Fixture.CreateCommittedSyntheticV91(connection, TestRunId);
        await fixture.SeedAsync();
        var order = await fixture.CreateHeldInvoiceAsync();
        await fixture.CreateMarketplaceParentAsync(order);
        var lineSplit = await fixture.FindInvoiceLineAsync(fixture.SplitProductId, order);
        var lineOther = await fixture.FindInvoiceLineAsync(fixture.OtherProductId, order);
        var lineOwned = await fixture.FindInvoiceLineAsync(fixture.OwnedProductId, order);
        var groupA = await fixture.CreateGroupAsync(order, fixture.WarehouseAId, 1, fixture.PartnerSellerId);
        var groupB = await fixture.CreateGroupAsync(order, fixture.WarehouseBId, 2, fixture.PartnerSellerId);
        var groupC = await fixture.CreateGroupAsync(order, fixture.WarehouseAId, 3);
        var groupD = await fixture.CreateGroupAsync(order, fixture.WarehouseBId, 4, fixture.SecondPartnerSellerId);
        var allocations = new[]
        {
            (await fixture.CreateAllocationAsync(order, lineSplit, fixture.WarehouseAId, 2m, "service-pay-split-a", fixture.PartnerSellerId, fixture.AgreementId), groupA),
            (await fixture.CreateAllocationAsync(order, lineSplit, fixture.WarehouseBId, 1m, "service-pay-split-b", fixture.PartnerSellerId, fixture.AgreementId), groupB),
            (await fixture.CreateAllocationAsync(order, lineOther, fixture.WarehouseBId, 1m, "service-pay-taxable", fixture.SecondPartnerSellerId, fixture.SecondAgreementId, "ITEM_TAXABLE"), groupD),
            (await fixture.CreateAllocationAsync(order, lineOwned, fixture.WarehouseAId, 1m, "service-pay-owned"), groupC)
        };
        foreach (var (allocation, group) in allocations)
        {
            await fixture.AttachAllocationAsync(group, allocation);
            await fixture.ReserveAsync(order, allocation, DateTimeOffset.UtcNow.AddMinutes(30), $"service-pay-{allocation:N}");
        }
        await fixture.AdvanceOrderAsync(order, "RESERVED");

        var actor = new IntegrationCurrentUser(fixture.TenantId, Guid.NewGuid());
        var gateway = new FakePaymentGateway();
        var service = CreatePaymentService(connectionString, actor, gateway);
        await service.SaveRazorpayForTenantAsync(fixture.TenantId,
            new("test_key_id", "synthetic_test_key_secret", "synthetic_test_webhook_secret", true, true, true),
            fixture.TestTag, CancellationToken.None);
        await service.SaveOptionsForTenantAsync(fixture.TenantId,
            new(true, true, true), fixture.TestTag, CancellationToken.None);

        gateway.TimeoutNextCreateAfterProviderAccept = true;
        await FluentActions.Invoking(() => service.CreateAttemptForTenantAsync(fixture.TenantId,
                new(order, PaymentProviders.Razorpay, "UPI"), fixture.TestTag, CancellationToken.None))
            .Should().ThrowAsync<TimeoutException>();
        var attempt = await service.CreateAttemptForTenantAsync(fixture.TenantId,
            new(order, PaymentProviders.Razorpay, "UPI"), fixture.TestTag, CancellationToken.None);
        attempt.Payment.Amount.Should().Be(389m);
        gateway.LastCreateRequest!.Amount.Should().Be(389m);
        gateway.CreateCount.Should().Be(1);
        var paymentId = attempt.Payment.PaymentId;

        var providerOrderId = gateway.LastProviderOrderId!;
        var invalid = WebhookBody(paymentId, providerOrderId);
        gateway.WebhookResult = new(false, "invalid-signature-event", "payment.captured", providerOrderId,
            null, "fake-provider-payment", 389m, "INR", true, false);
        await FluentActions.Invoking(() => service.ProcessRazorpayWebhookAsync(invalid, "bad-signature", null, CancellationToken.None))
            .Should().ThrowAsync<UnauthorizedAccessException>();

        gateway.WebhookResult = new(true, "amount-mismatch-event", "payment.captured", providerOrderId,
            null, "fake-provider-payment", 388m, "INR", true, false);
        await FluentActions.Invoking(() => service.ProcessRazorpayWebhookAsync(invalid, "valid-fake-signature", null, CancellationToken.None))
            .Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.BusinessRuleException>();

        gateway.WebhookResult = new(true, "marketplace-paid-event", "payment.captured", providerOrderId,
            null, "fake-provider-payment", 389m, "INR", true, false);
        await service.ProcessRazorpayWebhookAsync(invalid, "valid-fake-signature", null, CancellationToken.None);
        await service.ProcessRazorpayWebhookAsync(invalid, "valid-fake-signature", null, CancellationToken.None);

        await using var verify = await OpenAndVerifyAsync(connectionString);
        await using var assert = new SqlCommand("""
            SELECT p.Status, p.Amount, i.PaidAmount, i.BalanceAmount,
                   (SELECT COUNT(*) FROM commerce.PaymentProviderEvents e WHERE e.PaymentId=p.PaymentId AND e.ProviderEventId=N'marketplace-paid-event') EventCount,
                   (SELECT COUNT(*) FROM sales.SalesPayments sp WHERE sp.InvoiceId=i.InvoiceId) ReceiptCount,
                   (SELECT COUNT(*) FROM inventory.InventoryTransactions it WHERE it.TenantId=p.TenantId AND it.ReferenceType=N'MARKETPLACE_ORDER' AND it.ReferenceId=i.InvoiceId) InventoryPostingCount,
                   i.OrderType,
                   (SELECT COUNT(*) FROM commerce.CommercePayments parentPayments WHERE parentPayments.TenantId=p.TenantId AND parentPayments.InvoiceId=i.InvoiceId) PaymentCount,
                   (SELECT COUNT(*) FROM commerce.PaymentProviderEvents allEvents WHERE allEvents.PaymentId=p.PaymentId) ProviderEventCount,
                   (SELECT COUNT(*) FROM inventory.InventoryTransactionDetails d JOIN inventory.InventoryTransactions t ON t.TransactionId=d.TransactionId WHERE t.TenantId=p.TenantId AND t.ReferenceType=N'MARKETPLACE_ORDER' AND t.ReferenceId=i.InvoiceId) InventoryDetailCount,
                   (SELECT COALESCE(SUM(d.Quantity),0) FROM inventory.InventoryTransactionDetails d JOIN inventory.InventoryTransactions t ON t.TransactionId=d.TransactionId WHERE t.TenantId=p.TenantId AND t.ReferenceType=N'MARKETPLACE_ORDER' AND t.ReferenceId=i.InvoiceId) ConsumedQuantity,
                   (SELECT COALESCE(SUM(ib.QuantityOnHand),0) FROM inventory.InventoryBalances ib WHERE ib.TenantId=p.TenantId) RemainingOnHand,
                   (SELECT COALESCE(SUM(ib.QuantityReserved),0) FROM inventory.InventoryBalances ib WHERE ib.TenantId=p.TenantId) RemainingReserved,
                   (SELECT COUNT(*) FROM commerce.PartnerSettlementEntries s WHERE s.OperatingTenantId=p.TenantId AND s.MarketplaceOrderId=i.InvoiceId) SettlementEntryCount,
                   (SELECT COUNT(*) FROM commerce.PartnerSettlementEvents e JOIN commerce.PartnerSettlementEntries s ON s.OperatingTenantId=e.OperatingTenantId AND s.SettlementEntryId=e.SettlementEntryId WHERE e.OperatingTenantId=p.TenantId AND s.MarketplaceOrderId=i.InvoiceId) SettlementEventCount
            FROM commerce.CommercePayments p JOIN sales.SalesInvoices i ON i.TenantId=p.TenantId AND i.InvoiceId=p.InvoiceId
            WHERE p.TenantId=@tenant AND p.PaymentId=@payment;
            """, verify);
        assert.Parameters.AddWithValue("@tenant", fixture.TenantId);
        assert.Parameters.AddWithValue("@payment", paymentId);
        await using var reader = await assert.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be(CommercePaymentStatuses.Paid);
        reader.GetDecimal(1).Should().Be(389m);
        reader.GetDecimal(2).Should().Be(389m);
        reader.GetDecimal(3).Should().Be(0m);
        reader.GetInt32(4).Should().Be(1);
        reader.GetInt32(5).Should().Be(1);
        reader.GetInt32(6).Should().Be(2, "the marketplace completion procedure posts one sale movement per warehouse, once");
        reader.GetString(7).Should().Be("MARKETPLACE", "marketplace invoices retain their type and do not use legacy POS completion");
        reader.GetInt32(8).Should().Be(1, "a provider timeout followed by retry reuses the parent payment obligation");
        reader.GetInt32(9).Should().Be(1, "invalid signature and amount mismatch do not commit provider events; replay keeps one event");
        reader.GetInt32(10).Should().Be(4);
        reader.GetDecimal(11).Should().Be(5m);
        reader.GetDecimal(12).Should().Be(0m);
        reader.GetDecimal(13).Should().Be(0m);
        output.WriteLine("Payment run {0}; tenant {1}; invoice {2}; payment {3}; status {4}; receipts {5}; provider events {6}; inventory transactions {7}; inventory details {8}; settlement entries {9}; settlement events {10}.",
            fixture.TestRunId, fixture.TenantId, order, paymentId, reader.GetString(0), reader.GetInt32(5), reader.GetInt32(9),
            reader.GetInt32(6), reader.GetInt32(10), reader.GetInt32(14), reader.GetInt32(15));
        await reader.CloseAsync();
        await fixture.ClearTenantContextBestEffortAsync();
    }

    [MarketplaceV91ServiceSqlFact(Timeout = 180_000)]
    public async Task TwoSqlSessionsCannotReserveTheSameFinalUnitTwice()
    {
        RequireAuthorization();
        var connectionString = RequireConnectionString();
        var repositoryRoot = FindRepositoryRoot();
        await using var setupConnection = await OpenAndVerifyAsync(connectionString);
        await AssertSyntheticOnlyScopeAsync(setupConnection, repositoryRoot);
        var priorFootprint = await ReadCommittedFixtureFootprintAsync(setupConnection, TestRunId);
        output.WriteLine("Test run {0}; stale retained synthetic footprint: {1}", TestRunId, priorFootprint);
        if (priorFootprint.Tenants != 0)
            throw new InvalidOperationException($"Committed marketplace test found stale synthetic data before writing: {priorFootprint}. Run only against a freshly sanitized and readiness-verified V91 test database; no rows were changed.");
        await using (var guard = (SqlTransaction)await setupConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await MarketplaceV85V91SqlIntegrationTests.AssertCanonicalMarketplaceMigrationsAsync(
                setupConnection, guard, repositoryRoot);
            await MarketplaceV85V91SqlIntegrationTests.AssertCheckoutReleaseDisabledAsync(setupConnection, guard);
            await guard.RollbackAsync();
        }

        var fixture = MarketplaceV85V91SqlIntegrationTests.Fixture.CreateCommittedSyntheticV91(setupConnection, TestRunId);
        await fixture.SeedAsync();
        var orderA = await fixture.CreateHeldInvoiceAsync();
        var orderB = await fixture.CreateHeldInvoiceAsync();
        await fixture.CreateMarketplaceParentAsync(orderA);
        await fixture.CreateMarketplaceParentAsync(orderB);
        var lineA = await fixture.FindInvoiceLineAsync(fixture.SplitProductId, orderA);
        var lineB = await fixture.FindInvoiceLineAsync(fixture.SplitProductId, orderB);
        var allocationA = await fixture.CreateAllocationAsync(orderA, lineA, fixture.WarehouseBId, 1m, "last-unit-a");
        var allocationB = await fixture.CreateAllocationAsync(orderB, lineB, fixture.WarehouseBId, 1m, "last-unit-b");

        var bothSessionsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readySessionCount = 0;
        async Task<int> TryReserve(Guid allocation, string key)
        {
            await using var c = await OpenAndVerifyAsync(connectionString);
            await using (var context = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", c))
            {
                context.Parameters.AddWithValue("@tenant", fixture.TenantId);
                await context.ExecuteNonQueryAsync();
            }
            if (Interlocked.Increment(ref readySessionCount) == 2) bothSessionsReady.TrySetResult();
            await bothSessionsReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using var command = new SqlCommand("commerce.Marketplace_ReserveAllocation", c) { CommandType = CommandType.StoredProcedure };
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@AllocationId", allocation);
            command.Parameters.AddWithValue("@ExpiresAt", DateTimeOffset.UtcNow.AddMinutes(20));
            command.Parameters.AddWithValue("@ReservationIdempotencyKey", fixture.TestTag + key);
            command.Parameters.AddWithValue("@CreatedBy", fixture.TestTag);
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                return 0;
            }
            catch (SqlException error) { return error.Number; }
        }

        var results = await Task.WhenAll(TryReserve(allocationA, "-race-a"), TryReserve(allocationB, "-race-b"));
        results.Count(x => x == 0).Should().Be(1);
        results.Count(x => x == 85034).Should().Be(1);

        await using var verify = await OpenAndVerifyAsync(connectionString);
        await using var check = new SqlCommand("""
            SELECT b.QuantityOnHand,b.QuantityReserved,
              (SELECT COUNT(*) FROM commerce.MarketplaceAllocationReservations r WHERE r.TenantId=@tenant AND r.AllocationId IN(@a,@b) AND r.ReservationStatus IN(N'RESERVED',N'CONFIRMED')) ActiveReservations,
              (SELECT COUNT(*) FROM commerce.MarketplaceOrderAllocations a WHERE a.TenantId=@tenant AND a.AllocationId IN(@a,@b)) Allocations
            FROM inventory.InventoryBalances b WHERE b.TenantId=@tenant AND b.ProductId=@product AND b.WarehouseId=@warehouse;
            """, verify);
        check.Parameters.AddWithValue("@tenant", fixture.TenantId);
        check.Parameters.AddWithValue("@a", allocationA);
        check.Parameters.AddWithValue("@b", allocationB);
        check.Parameters.AddWithValue("@product", fixture.SplitProductId);
        check.Parameters.AddWithValue("@warehouse", fixture.WarehouseBId);
        await using var state = await check.ExecuteReaderAsync();
        (await state.ReadAsync()).Should().BeTrue();
        state.GetDecimal(0).Should().Be(1m);
        state.GetDecimal(1).Should().Be(1m);
        state.GetInt32(2).Should().Be(1);
        state.GetInt32(3).Should().Be(2, "two distinct synthetic orders competed for the final unit without duplicate allocation creation");
        output.WriteLine("Concurrency run {0}; tenant {1}; orders {2},{3}; successful reservations {4}; active reservations {5}; allocations {6}; on-hand {7}; reserved {8}.",
            fixture.TestRunId, fixture.TenantId, orderA, orderB, results.Count(x => x == 0), state.GetInt32(2), state.GetInt32(3), state.GetDecimal(0), state.GetDecimal(1));
        await fixture.ClearTenantContextBestEffortAsync();
    }

    private static CommercePaymentService CreatePaymentService(string connectionString, IntegrationCurrentUser currentUser,
        FakePaymentGateway gateway)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString
        }).Build();
        return new(configuration, new EphemeralDataProtectionProvider(), currentUser,
            new FakePaymentGatewayResolver(gateway), new ErpPaymentPosting(), new PermissiveFakeOutboundPolicy(),
            idempotency: new SqlIdempotencyExecutor(configuration, currentUser));
    }

    private static ReadOnlyMemory<byte> WebhookBody(Guid paymentId, string providerOrderId) =>
        JsonSerializer.SerializeToUtf8Bytes(new { payload = new { payment = new { entity = new
        {
            order_id = providerOrderId,
            notes = new { payment_id = paymentId.ToString("N") }
        } } } });

    private static async Task AssertSyntheticOnlyScopeAsync(SqlConnection connection, string repositoryRoot)
    {
        const string markerSql = """
            SELECT
              (SELECT CONVERT(nvarchar(4000),value) FROM sys.extended_properties WHERE class=0 AND name=N'WhatsBiz.DisposableIntegrationDatabase'),
              (SELECT CONVERT(nvarchar(4000),value) FROM sys.extended_properties WHERE class=0 AND name=N'WhatsBiz.GuturGoMarketplaceRestoredV91Readiness');
            """;
        await using (var marker = new SqlCommand(markerSql, connection))
        await using (var reader = await marker.ExecuteReaderAsync())
        {
            (await reader.ReadAsync()).Should().BeTrue("the database readiness properties can be read");
            var canonical = reader.IsDBNull(0) ? null : reader.GetString(0);
            var restored = reader.IsDBNull(1) ? null : reader.GetString(1);
            var canonicalMatches = string.Equals(canonical,
                $"Approved:{ExpectedServer}/{ExpectedDatabase}", StringComparison.Ordinal);
            if (!canonicalMatches)
            {
                if (string.IsNullOrWhiteSpace(restored))
                    throw new InvalidOperationException("The approved target has neither a matching canonical marker nor a registered sanitized V91 restore-readiness record.");
                try
                {
                    using var document = JsonDocument.Parse(restored);
                    var root = document.RootElement;
                    root.GetProperty("contract").GetString().Should().Be("GUTURGO_SYNTHETIC_RESTORED_V91_V1");
                    root.GetProperty("server").GetString().Should().Be(ExpectedServer);
                    root.GetProperty("database").GetString().Should().Be(ExpectedDatabase);
                    root.GetProperty("sourceDatabase").GetString().Should().Be("WhatsBizERP");
                    root.GetProperty("migrationFingerprint").GetString().Should().Be(ComputeV91MigrationFingerprint(repositoryRoot));
                    string.IsNullOrWhiteSpace(root.GetProperty("restoreReference").GetString()).Should().BeFalse();
                    string.IsNullOrWhiteSpace(root.GetProperty("sanitizationReference").GetString()).Should().BeFalse();
                }
                catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    throw new InvalidOperationException("The restored-copy readiness record is malformed or does not match the approved target and V91 source hashes.", error);
                }
            }
        }

        const string sql = """
            DECLARE @defaultTenant uniqueidentifier='11111111-1111-1111-1111-111111111111';
            DECLARE @unexpectedTenantRows bit=0,@scan nvarchar(max)=N'';
            IF (SELECT COUNT(*) FROM core.Tenants WHERE TenantId=@defaultTenant AND TenantKey=N'DEFAULT' AND Name=N'Default V1 Tenant' AND IsActive=1)<>1
                THROW 58011,N'The canonical V40 DEFAULT tenant seed is missing or ambiguous.',1;
            IF EXISTS(SELECT 1 FROM core.Tenants WHERE NOT(TenantId=@defaultTenant AND TenantKey=N'DEFAULT' AND Name=N'Default V1 Tenant' AND IsActive=1)
                AND TenantKey NOT LIKE N'MKTSQLTEST%')
                THROW 58012,N'The database contains a tenant outside the canonical seed and synthetic marketplace fixture namespace.',1;
            SELECT @scan=STRING_AGG(CONVERT(nvarchar(max),N'IF EXISTS(SELECT 1 FROM '+QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)+N' x WHERE x.TenantId IS NOT NULL AND x.TenantId<>@defaultTenant AND NOT EXISTS(SELECT 1 FROM core.Tenants y WHERE y.TenantId=x.TenantId AND y.TenantKey LIKE N''MKTSQLTEST%'')) SET @unexpectedTenantRows=1;'),N' ')
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id AND c.name=N'TenantId'
            JOIN sys.types ty ON ty.user_type_id=c.user_type_id AND ty.name=N'uniqueidentifier'
            WHERE t.is_ms_shipped=0 AND NOT(s.name=N'core' AND t.name=N'Tenants');
            IF @scan IS NOT NULL EXEC sys.sp_executesql @scan,N'@defaultTenant uniqueidentifier,@unexpectedTenantRows bit OUTPUT',@defaultTenant,@unexpectedTenantRows OUTPUT;
            IF @unexpectedTenantRows=1 THROW 58013,N'Tenant-scoped records exist outside the canonical seed and synthetic marketplace fixture namespace.',1;

            IF OBJECT_ID(N'commerce.TenantPaymentProviders',N'U') IS NOT NULL AND EXISTS(
                SELECT 1 FROM commerce.TenantPaymentProviders p JOIN core.Tenants t ON t.TenantId=p.TenantId
                WHERE t.TenantKey NOT LIKE N'MKTSQLTEST%' AND
                  (p.IsEnabled=1 OR NULLIF(LTRIM(RTRIM(p.KeyId)),N'') IS NOT NULL OR
                   NULLIF(LTRIM(RTRIM(p.KeySecretProtected)),N'') IS NOT NULL OR NULLIF(LTRIM(RTRIM(p.WebhookSecretProtected)),N'') IS NOT NULL))
                THROW 58014,N'Usable or stored payment-provider credentials exist outside synthetic marketplace tenants.',1;
            IF OBJECT_ID(N'integration.WhatsAppPlatformConfiguration',N'U') IS NOT NULL AND EXISTS(
                SELECT 1 FROM integration.WhatsAppPlatformConfiguration WHERE IsEnabled=1 OR
                  NULLIF(LTRIM(RTRIM(AppSecretProtected)),N'') IS NOT NULL OR NULLIF(LTRIM(RTRIM(WebhookVerifyTokenProtected)),N'') IS NOT NULL)
                THROW 58015,N'Platform WhatsApp credentials or delivery are configured in the restored test database.',1;
            IF OBJECT_ID(N'integration.WhatsAppConfigurations',N'U') IS NOT NULL AND EXISTS(
                SELECT 1 FROM integration.WhatsAppConfigurations WHERE IsEnabled=1 OR
                  NULLIF(LTRIM(RTRIM(AccessTokenProtected)),N'') IS NOT NULL OR NULLIF(LTRIM(RTRIM(WebhookVerifyTokenProtected)),N'') IS NOT NULL OR
                  NULLIF(LTRIM(RTRIM(AppSecretProtected)),N'') IS NOT NULL)
                THROW 58016,N'Tenant WhatsApp credentials or delivery are configured in the restored test database.',1;
            IF OBJECT_ID(N'admin.SmsConfigurations',N'U') IS NOT NULL AND EXISTS(
                SELECT 1 FROM admin.SmsConfigurations WHERE IsActive=1 OR NULLIF(LTRIM(RTRIM(ApiKeyEncrypted)),N'') IS NOT NULL)
                THROW 58017,N'SMS provider configuration or credentials exist in the restored test database.',1;
            IF OBJECT_ID(N'admin.EmailConfigurations',N'U') IS NOT NULL AND EXISTS(
                SELECT 1 FROM admin.EmailConfigurations WHERE IsActive=1 OR NULLIF(LTRIM(RTRIM(PasswordEncrypted)),N'') IS NOT NULL)
                THROW 58018,N'Email provider configuration or credentials exist in the restored test database.',1;
            IF COL_LENGTH(N'commerce.StorefrontConfigurations',N'CustomSmtpPasswordProtected') IS NOT NULL AND EXISTS(
                SELECT 1 FROM commerce.StorefrontConfigurations WHERE AutomatedEmailMode=N'CUSTOM_SMTP' OR
                  NULLIF(LTRIM(RTRIM(CustomSmtpPasswordProtected)),N'') IS NOT NULL OR NULLIF(LTRIM(RTRIM(CustomSmtpUsername)),N'') IS NOT NULL)
                THROW 58019,N'Custom SMTP configuration or credentials exist in the restored test database.',1;
            """;
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<CommittedFixtureFootprint> ReadCommittedFixtureFootprintAsync(SqlConnection connection, Guid runId)
    {
        await using var command = new SqlCommand("""
            WITH TestTenants AS (
              SELECT TenantId FROM core.Tenants
              WHERE TenantKey LIKE N'MKTSQLTEST%' AND TenantKey NOT LIKE @runPrefix + N'%'
            )
            SELECT
              (SELECT COUNT_BIG(*) FROM TestTenants),
              (SELECT COUNT_BIG(*) FROM sales.SalesInvoices i JOIN TestTenants t ON t.TenantId=i.TenantId),
              (SELECT COUNT_BIG(*) FROM commerce.MarketplaceOrders o JOIN TestTenants t ON t.TenantId=o.TenantId),
              (SELECT COUNT_BIG(*) FROM commerce.CommercePayments p JOIN TestTenants t ON t.TenantId=p.TenantId),
              (SELECT COUNT_BIG(*) FROM commerce.MarketplaceAllocationReservations r JOIN TestTenants t ON t.TenantId=r.TenantId),
              (SELECT COUNT_BIG(*) FROM commerce.PartnerSettlementEntries s JOIN TestTenants t ON t.TenantId=s.OperatingTenantId AND s.MarketplaceOrderId IS NOT NULL),
              (SELECT COUNT_BIG(*) FROM commerce.PartnerSettlementEvents e JOIN TestTenants t ON t.TenantId=e.OperatingTenantId);
            """, connection);
        command.Parameters.AddWithValue("@runPrefix", "MKTSQLTEST" + runId.ToString("N")[..8]);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Committed synthetic fixture footprint query returned no row.");
        return new(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt64(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetInt64(5),reader.GetInt64(6));
    }

    private sealed record CommittedFixtureFootprint(long Tenants,long Invoices,long MarketplaceOrders,long Payments,
        long Reservations,long SettlementEntries,long SettlementEvents);

    private static string ComputeV91MigrationFingerprint(string repositoryRoot)
    {
        var lines = new List<string>
        {
            "V40|Baseline-V40.validate.sql|" + Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(repositoryRoot, "database", "migrations", "validators", "Baseline-V40.validate.sql"))))
        };
        for (var version = 41; version <= 91; version++)
        {
            var prefix = $"V{version}-";
            var migration = Directory.GetFiles(Path.Combine(repositoryRoot, "database", "migrations"), prefix + "*.sql")
                .Single(path => !path.EndsWith(".validate.sql", StringComparison.OrdinalIgnoreCase));
            var validator = Path.Combine(repositoryRoot, "database", "migrations", "validators", Path.GetFileNameWithoutExtension(migration) + ".validate.sql");
            lines.Add($"{version}|{Path.GetFileName(migration)}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(migration)))}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(validator)))}");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
    }

    private static async Task<SqlConnection> OpenAndVerifyAsync(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.DataSource, ExpectedServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(builder.InitialCatalog, ExpectedDatabase, StringComparison.OrdinalIgnoreCase) ||
            builder.InitialCatalog is "WhatsBizERP" or "WhatsBizERP_QA" or "WhatsBizERP_PROD" ||
            !builder.IntegratedSecurity || builder.Pooling)
            throw new InvalidOperationException($"Service SQL tests only allow Integrated Security, Pooling=False on {ExpectedServer}/{ExpectedDatabase}.");
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME(),@@TRANCOUNT;", connection);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync() || !string.Equals(reader.GetString(0), ExpectedServer, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(reader.GetString(1), ExpectedDatabase, StringComparison.OrdinalIgnoreCase) || reader.GetInt32(2) != 0)
            {
                await connection.DisposeAsync();
                throw new InvalidOperationException("Connected SQL identity or transaction state differs from the approved synthetic V91 test database.");
            }
        }
        return connection;
    }

    private static string RequireConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } value
            ? ValidateAuthorizedConnectionString(value)
            : throw SkipException.ForSkip($"Committed marketplace SQL test NOT RUN: {ConnectionVariable} is not configured.");

    private static void RequireAuthorization()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(AuthorizationVariable), AuthorizationValue, StringComparison.Ordinal))
            throw SkipException.ForSkip($"Committed marketplace SQL test NOT RUN: explicit authorization is required in {AuthorizationVariable}.");
    }

    private static string ValidateAuthorizedConnectionString(string value)
    {
        if (value.TrimStart().StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
            throw SkipException.ForSkip($"Committed marketplace SQL test NOT RUN: {ConnectionVariable} must contain only a connection-string value, not PowerShell assignment syntax.");
        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(value); }
        catch (ArgumentException error)
        {
            throw SkipException.ForSkip($"Committed marketplace SQL test NOT RUN: {ConnectionVariable} is malformed ({error.Message}).");
        }
        if (!string.Equals(builder.DataSource, ExpectedServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(builder.InitialCatalog, ExpectedDatabase, StringComparison.OrdinalIgnoreCase) ||
            !builder.IntegratedSecurity || builder.Pooling ||
            !string.IsNullOrWhiteSpace(builder.UserID) || !string.IsNullOrWhiteSpace(builder.Password) ||
            !string.IsNullOrWhiteSpace(builder.AttachDBFilename) || builder.UserInstance)
            throw new InvalidOperationException($"Committed marketplace SQL tests fail closed unless the connection targets {ExpectedServer}/{ExpectedDatabase} with Windows Integrated Security and Pooling=False.");
        return builder.ConnectionString;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "database/migrations/V91-MarketplacePartnerSettlementAccounting.sql"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not find the canonical V91 migration files.");
    }

    private sealed class IntegrationCurrentUser(Guid tenantId, Guid userId) : ICurrentUserService
    {
        public Guid? TenantId => tenantId;
        public Guid? UserId => userId;
        public string? Username => "marketplace-integration-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => Array.Empty<string>();
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
    }

    private sealed class PermissiveFakeOutboundPolicy : IOutboundSafetyPolicy
    {
        public bool AllowsExternalDelivery => true;
        public void EnsureExternalDeliveryAllowed(string integration) { }
    }

    private sealed class FakePaymentGatewayResolver(FakePaymentGateway gateway) : IPaymentGatewayResolver
    {
        public IPaymentGateway Resolve(string provider) => provider == PaymentProviders.Razorpay
            ? gateway : throw new InvalidOperationException("Only the synthetic Razorpay gateway is configured in this test.");
    }

    private sealed class FakePaymentGateway : IPaymentGateway
    {
        public string Provider => PaymentProviders.Razorpay;
        public int CreateCount { get; private set; }
        public bool TimeoutNextCreateAfterProviderAccept { get; set; }
        public GatewayCreateRequest? LastCreateRequest { get; private set; }
        public string? LastProviderOrderId { get; private set; }
        public GatewayWebhookResult WebhookResult { get; set; } = new(false, null, null, null, null, null, null, null, false, false);

        public Task<GatewayCreateResult> CreatePaymentAsync(PaymentGatewayConfiguration configuration, GatewayCreateRequest request, CancellationToken token)
        {
            CreateCount++;
            LastCreateRequest = request;
            LastProviderOrderId = "fake-order-" + request.PaymentId.ToString("N");
            if (TimeoutNextCreateAfterProviderAccept)
            {
                TimeoutNextCreateAfterProviderAccept = false;
                throw new TimeoutException("Synthetic provider accepted the attempt but timed out before returning its response.");
            }
            return Task.FromResult(new GatewayCreateResult(LastProviderOrderId, null, null, "fake-action"));
        }
        public Task<GatewayStatusResult> GetPaymentStatusAsync(PaymentGatewayConfiguration configuration, string providerReference, CancellationToken token) =>
            Task.FromResult(new GatewayStatusResult("PENDING", null));
        public GatewayWebhookResult VerifyWebhook(PaymentGatewayConfiguration configuration, ReadOnlyMemory<byte> rawBody, string signature, string? eventId) =>
            WebhookResult with { SignatureValid = signature == "valid-fake-signature" && WebhookResult.SignatureValid };
    }
}
