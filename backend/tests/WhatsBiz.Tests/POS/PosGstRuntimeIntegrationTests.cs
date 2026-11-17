using System.Text.Json;
using System.Globalization;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using WhatsBiz.Tests.Integration;

namespace WhatsBiz.Tests.POS;

public sealed class PosGstRuntimeIntegrationTests
{
    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task PosGstBaselineKnownInclusiveModeIsCurrentlyDoubleTaxed()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var fixture = await ReadFixtureAsync(connection, transaction);
        fixture.Should().NotBeNull("the isolated database must contain a stocked product and warehouse");
        var f = fixture!.Value;
        await ExecuteAsync(connection, transaction,
            "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant; UPDATE master.Products SET GstPricingType=N'INCLUSIVE' WHERE ProductId=@product;",
            ("@tenant", f.TenantId), ("@product", f.InclusiveProductId));
        var items = JsonSerializer.Serialize(new[] { new { ProductId = f.InclusiveProductId, Quantity = 1m, UnitPrice = 118m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m } });
        var invoiceId = await PostHeldAsync(connection, transaction, f, items);
        var totals = await ReadTotalsAsync(connection, transaction, invoiceId);
        totals.GrandTotal.Should().NotBe(118m, "BASELINE_KNOWN_DEFECT: the current procedure adds GST to an inclusive gross price");
        await transaction.RollbackAsync();
    }

    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task MixedInclusiveAndExclusiveHeldInvoiceUsesPersistedProductModes()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var fixture = await ReadFixtureAsync(connection, transaction);
        fixture.Should().NotBeNull("the isolated database must contain two stocked products and a warehouse");
        var f = fixture!.Value;
        await ExecuteAsync(connection, transaction,
            "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant; UPDATE master.Products SET GstPricingType=CASE WHEN ProductId=@inclusive THEN N'INCLUSIVE' ELSE N'EXCLUSIVE' END WHERE ProductId IN(@inclusive,@exclusive);",
            ("@tenant", f.TenantId), ("@inclusive", f.InclusiveProductId), ("@exclusive", f.ExclusiveProductId));

        var items = JsonSerializer.Serialize(new[]
        {
            new { ProductId = f.InclusiveProductId, Quantity = 1m, UnitPrice = 118m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m },
            new { ProductId = f.ExclusiveProductId, Quantity = 1m, UnitPrice = 100m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m },
        });
        var invoiceId = await PostHeldAsync(connection, transaction, f, items);
        var totals = await ReadTotalsAsync(connection, transaction, invoiceId);
        var lines = await ReadLineTotalsAsync(connection, transaction, invoiceId);

        totals.Should().Be((218m, 36m, 236m));
        lines.Should().ContainSingle(x => x.ProductId == f.InclusiveProductId && x.Taxable == 100m && x.Tax == 18m && x.LineTotal == 118m);
        lines.Should().ContainSingle(x => x.ProductId == f.ExclusiveProductId && x.Taxable == 100m && x.Tax == 18m && x.LineTotal == 118m);
        await transaction.RollbackAsync();
    }

    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task CompletedMixedInvoiceReconcilesStockPaymentAndJournal()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var fixture = await ReadFixtureAsync(connection, transaction);
        fixture.Should().NotBeNull("the isolated database must contain two stocked products and a warehouse");
        var f = fixture!.Value;
        await ExecuteAsync(connection, transaction,
            "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant; UPDATE master.Products SET GstPricingType=CASE WHEN ProductId=@inclusive THEN N'INCLUSIVE' ELSE N'EXCLUSIVE' END WHERE ProductId IN(@inclusive,@exclusive);",
            ("@tenant", f.TenantId), ("@inclusive", f.InclusiveProductId), ("@exclusive", f.ExclusiveProductId));

        var before = await ReadStockAsync(connection, transaction, f);
        var items = JsonSerializer.Serialize(new[]
        {
            new { ProductId = f.InclusiveProductId, Quantity = 1m, UnitPrice = 118m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m },
            new { ProductId = f.ExclusiveProductId, Quantity = 1m, UnitPrice = 100m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m },
        });
        var payments = JsonSerializer.Serialize(new[] { new { MethodCode = "CASH", Amount = 236m, ReferenceNumber = "POS-GST-TEST" } });
        var invoiceId = await PostCompletedAsync(connection, transaction, f, items, payments);
        var totals = await ReadTotalsAsync(connection, transaction, invoiceId);
        var paymentsTotal = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(Amount),0) FROM sales.SalesPayments WHERE InvoiceId=@id AND Status='COMPLETED'", ("@id", invoiceId));
        var after = await ReadStockAsync(connection, transaction, f);
        var journal = await ReadJournalTotalsAsync(connection, transaction, invoiceId, "SALE");

        totals.Should().Be((218m, 36m, 236m));
        paymentsTotal.Should().Be(236m);
        after.Inclusive.Should().Be(before.Inclusive - 1m);
        after.Exclusive.Should().Be(before.Exclusive - 1m);
        journal.Debit.Should().Be(472m);
        journal.Credit.Should().Be(472m);
        journal.EntryCount.Should().Be(1);
        journal.TenantCount.Should().Be(1);
        await transaction.RollbackAsync();
    }

    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task PartialAndFullReturnsUsePersistedLineAmountsAndRestoreStock()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var fixture = await ReadFixtureAsync(connection, transaction);
        fixture.Should().NotBeNull("the isolated database must contain a stocked product and warehouse");
        var f = fixture!.Value;
        await ExecuteAsync(connection, transaction,
            "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant; UPDATE master.Products SET GstPricingType=N'INCLUSIVE' WHERE ProductId=@product;",
            ("@tenant", f.TenantId), ("@product", f.InclusiveProductId));
        var before = await ReadStockAsync(connection, transaction, f);
        var items = JsonSerializer.Serialize(new[] { new { ProductId = f.InclusiveProductId, Quantity = 2m, UnitPrice = 118m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m } });
        var payments = JsonSerializer.Serialize(new[] { new { MethodCode = "CASH", Amount = 236m, ReferenceNumber = "POS-RETURN-TEST" } });
        var invoiceId = await PostCompletedAsync(connection, transaction, f, items, payments);
        var itemId = await ScalarGuidAsync(connection, transaction, "SELECT TOP(1) InvoiceItemId FROM sales.SalesInvoiceItems WHERE InvoiceId=@id", ("@id", invoiceId));

        var first = await ReturnAsync(connection, transaction, f, invoiceId, itemId, 1m);
        first.Refund.Should().Be(118m);
        (await ScalarStringAsync(connection, transaction, "SELECT Status FROM sales.SalesInvoices WHERE InvoiceId=@id", ("@id", invoiceId))).Should().Be("PARTIALLY_RETURNED");
        (await ReadStockAsync(connection, transaction, f)).Inclusive.Should().Be(before.Inclusive - 1m);

        var second = await ReturnAsync(connection, transaction, f, invoiceId, itemId, 1m);
        second.Refund.Should().Be(118m);
        (await ScalarStringAsync(connection, transaction, "SELECT Status FROM sales.SalesInvoices WHERE InvoiceId=@id", ("@id", invoiceId))).Should().Be("RETURNED");
        var returned = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(RefundAmount),0) FROM sales.SalesInvoiceReturns WHERE InvoiceId=@id", ("@id", invoiceId));
        returned.Should().Be(236m);
        (await ReadStockAsync(connection, transaction, f)).Inclusive.Should().Be(before.Inclusive);
        var returnJournal = await ReadJournalTotalsAsync(connection, transaction, invoiceId, "SALE_RETURN");
        returnJournal.Debit.Should().Be(returnJournal.Credit);
        returnJournal.Debit.Should().Be(236m);
        returnJournal.EntryCount.Should().Be(1);
        await transaction.RollbackAsync();
    }

    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task PosPostingRejectsMismatchedTenantContext()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var fixture = await ReadFixtureAsync(connection, transaction);
        fixture.Should().NotBeNull("the isolated database must contain a stocked product and warehouse");
        var f = fixture!.Value;
        await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", ("@tenant", f.TenantId));
        var items = JsonSerializer.Serialize(new[] { new { ProductId = f.InclusiveProductId, Quantity = 1m, UnitPrice = 118m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m } });
        var mismatched = f with { TenantId = Guid.NewGuid() };

        var action = () => PostHeldAsync(connection, transaction, mismatched, items);
        await action.Should().ThrowAsync<SqlException>();
        await transaction.RollbackAsync();
    }

    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task SingleFullReturnAndDuplicateRetryUseOriginalPersistedAmounts()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var fixture = await ReadFixtureAsync(connection, transaction);
        fixture.Should().NotBeNull();
        var f = fixture!.Value;
        await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant; UPDATE master.Products SET GstPricingType=N'INCLUSIVE' WHERE ProductId=@product;", ("@tenant", f.TenantId), ("@product", f.InclusiveProductId));
        var items = JsonSerializer.Serialize(new[] { new { ProductId = f.InclusiveProductId, Quantity = 1m, UnitPrice = 118m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m } });
        var invoiceId = await PostCompletedAsync(connection, transaction, f, items, JsonSerializer.Serialize(new[] { new { MethodCode = "CASH", Amount = 118m } }));
        var itemId = await ScalarGuidAsync(connection, transaction, "SELECT TOP(1) InvoiceItemId FROM sales.SalesInvoiceItems WHERE InvoiceId=@id", ("@id", invoiceId));

        (await ReturnAsync(connection, transaction, f, invoiceId, itemId, 1m)).Refund.Should().Be(118m);
        var journal = await ReadJournalTotalsAsync(connection, transaction, invoiceId, "SALE_RETURN");
        journal.Should().Be((118m, 118m, 1, 1));

        var retry = () => ReturnAsync(connection, transaction, f, invoiceId, itemId, 1m);
        await retry.Should().ThrowAsync<SqlException>();
        await transaction.RollbackAsync();
    }

    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task MixedInclusiveExclusiveReturnUsesOriginalInvoiceAmounts()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        var fixture = await ReadFixtureAsync(connection, transaction);
        fixture.Should().NotBeNull();
        var f = fixture!.Value;
        await ExecuteAsync(connection, transaction, "EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant; UPDATE master.Products SET GstPricingType=CASE WHEN ProductId=@inclusive THEN N'INCLUSIVE' ELSE N'EXCLUSIVE' END WHERE ProductId IN(@inclusive,@exclusive);", ("@tenant", f.TenantId), ("@inclusive", f.InclusiveProductId), ("@exclusive", f.ExclusiveProductId));
        var items = JsonSerializer.Serialize(new[]
        {
            new { ProductId = f.InclusiveProductId, Quantity = 1m, UnitPrice = 118m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m },
            new { ProductId = f.ExclusiveProductId, Quantity = 1m, UnitPrice = 100m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m },
        });
        var invoiceId = await PostCompletedAsync(connection, transaction, f, items, JsonSerializer.Serialize(new[] { new { MethodCode = "CASH", Amount = 236m } }));
        var itemIds = await ReadItemIdsAsync(connection, transaction, invoiceId);
        var returned = await ReturnManyAsync(connection, transaction, f, invoiceId, itemIds);
        returned.Should().Be(236m);
        (await ScalarStringAsync(connection, transaction, "SELECT Status FROM sales.SalesInvoices WHERE InvoiceId=@id", ("@id", invoiceId))).Should().Be("RETURNED");
        (await ReadJournalTotalsAsync(connection, transaction, invoiceId, "SALE_RETURN")).Should().Be((236m, 236m, 1, 1));
        await transaction.RollbackAsync();
    }

    private static async Task<(Guid TenantId, Guid WarehouseId, Guid InclusiveProductId, Guid ExclusiveProductId)?> ReadFixtureAsync(SqlConnection c, SqlTransaction t)
    {
        await using var command = new SqlCommand("""
            SELECT TOP(1) t.TenantId,w.WarehouseId,p1.ProductId,p2.ProductId
            FROM core.Tenants t
            JOIN inventory.Warehouses w ON w.TenantId=t.TenantId AND w.IsActive=1 AND w.IsDeleted=0
            CROSS APPLY (SELECT TOP(1) p.ProductId FROM master.Products p JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId AND b.WarehouseId=w.WarehouseId AND b.QuantityAvailable>0 WHERE p.TenantId=t.TenantId AND p.IsActive=1 AND p.IsDeleted=0 ORDER BY p.ProductId) p1
            CROSS APPLY (SELECT TOP(1) p.ProductId FROM master.Products p JOIN inventory.InventoryBalances b ON b.ProductId=p.ProductId AND b.WarehouseId=w.WarehouseId AND b.QuantityAvailable>0 WHERE p.TenantId=t.TenantId AND p.IsActive=1 AND p.IsDeleted=0 AND p.ProductId<>p1.ProductId ORDER BY p.ProductId) p2
            WHERE t.IsActive=1 ORDER BY t.TenantId;
            """, c, t);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3)) : null;
    }

    private static async Task ExecuteAsync(SqlConnection c, SqlTransaction t, string text, params (string Name, object Value)[] values)
    {
        await using var command = new SqlCommand(text, c, t);
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> PostHeldAsync(SqlConnection c, SqlTransaction t, (Guid TenantId, Guid WarehouseId, Guid InclusiveProductId, Guid ExclusiveProductId) f, string items)
    {
        await using var command = new SqlCommand("sales.POS_PostInvoice", c, t) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", f.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", f.WarehouseId);
        command.Parameters.AddWithValue("@ItemsJson", items);
        command.Parameters.AddWithValue("@Status", "HELD");
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return reader.GetGuid(reader.GetOrdinal("InvoiceId"));
    }

    private static async Task<Guid> PostCompletedAsync(SqlConnection c, SqlTransaction t, (Guid TenantId, Guid WarehouseId, Guid InclusiveProductId, Guid ExclusiveProductId) f, string items, string payments)
    {
        await using var command = new SqlCommand("sales.POS_PostInvoice", c, t) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", f.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", f.WarehouseId);
        command.Parameters.AddWithValue("@ItemsJson", items);
        command.Parameters.AddWithValue("@PaymentsJson", payments);
        command.Parameters.AddWithValue("@Status", "COMPLETED");
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return reader.GetGuid(reader.GetOrdinal("InvoiceId"));
    }

    private static async Task<(decimal Inclusive, decimal Exclusive)> ReadStockAsync(SqlConnection c, SqlTransaction t, (Guid TenantId, Guid WarehouseId, Guid InclusiveProductId, Guid ExclusiveProductId) f)
    {
        await using var command = new SqlCommand("SELECT COALESCE(SUM(CASE WHEN ProductId=@inclusive THEN QuantityOnHand ELSE 0 END),0),COALESCE(SUM(CASE WHEN ProductId=@exclusive THEN QuantityOnHand ELSE 0 END),0) FROM inventory.InventoryBalances WHERE TenantId=@tenant AND WarehouseId=@warehouse AND ProductId IN(@inclusive,@exclusive)", c, t);
        command.Parameters.AddWithValue("@tenant", f.TenantId); command.Parameters.AddWithValue("@warehouse", f.WarehouseId); command.Parameters.AddWithValue("@inclusive", f.InclusiveProductId); command.Parameters.AddWithValue("@exclusive", f.ExclusiveProductId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private static async Task<(decimal Debit, decimal Credit, int EntryCount, int TenantCount)> ReadJournalTotalsAsync(SqlConnection c, SqlTransaction t, Guid referenceId, string type)
    {
        await using var command = new SqlCommand("SELECT COALESCE(SUM(d.DebitAmount),0),COALESCE(SUM(d.CreditAmount),0),COUNT(DISTINCT j.JournalEntryId),COUNT(DISTINCT j.TenantId) FROM finance.JournalEntries j JOIN finance.JournalEntryDetails d ON d.JournalEntryId=j.JournalEntryId WHERE j.ReferenceId=@id AND j.ReferenceType=@type", c, t);
        command.Parameters.AddWithValue("@id", referenceId); command.Parameters.AddWithValue("@type", type);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private static async Task<(decimal Refund, Guid TransactionId)> ReturnAsync(SqlConnection c, SqlTransaction t, (Guid TenantId, Guid WarehouseId, Guid InclusiveProductId, Guid ExclusiveProductId) f, Guid invoiceId, Guid itemId, decimal quantity)
    {
        await using var command = new SqlCommand("sales.POS_ReturnInvoice", c, t) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@InvoiceId", invoiceId); command.Parameters.AddWithValue("@ItemsJson", JsonSerializer.Serialize(new[] { new { InvoiceItemId = itemId, Quantity = quantity } })); command.Parameters.AddWithValue("@Reason", "POS GST integration test"); command.Parameters.AddWithValue("@CreatedBy", "pos-gst-test"); command.Parameters.AddWithValue("@TenantId", f.TenantId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetDecimal(reader.GetOrdinal("RefundAmount")), reader.GetGuid(reader.GetOrdinal("TransactionId")));
    }

    private static async Task<IReadOnlyList<Guid>> ReadItemIdsAsync(SqlConnection c, SqlTransaction t, Guid invoiceId)
    {
        await using var command = new SqlCommand("SELECT InvoiceItemId FROM sales.SalesInvoiceItems WHERE InvoiceId=@id ORDER BY ProductId", c, t);
        command.Parameters.AddWithValue("@id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static async Task<decimal> ReturnManyAsync(SqlConnection c, SqlTransaction t, (Guid TenantId, Guid WarehouseId, Guid InclusiveProductId, Guid ExclusiveProductId) f, Guid invoiceId, IReadOnlyList<Guid> itemIds)
    {
        await using var command = new SqlCommand("sales.POS_ReturnInvoice", c, t) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@InvoiceId", invoiceId); command.Parameters.AddWithValue("@ItemsJson", JsonSerializer.Serialize(itemIds.Select(id => new { InvoiceItemId = id, Quantity = 1m }))); command.Parameters.AddWithValue("@Reason", "POS GST mixed return test"); command.Parameters.AddWithValue("@CreatedBy", "pos-gst-test"); command.Parameters.AddWithValue("@TenantId", f.TenantId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return reader.GetDecimal(reader.GetOrdinal("RefundAmount"));
    }

    private static async Task<decimal> ScalarDecimalAsync(SqlConnection c, SqlTransaction t, string sql, params (string Name, object Value)[] values)
    {
        await using var command = new SqlCommand(sql, c, t); foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        return Convert.ToDecimal(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<Guid> ScalarGuidAsync(SqlConnection c, SqlTransaction t, string sql, params (string Name, object Value)[] values)
    {
        await using var command = new SqlCommand(sql, c, t); foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ScalarStringAsync(SqlConnection c, SqlTransaction t, string sql, params (string Name, object Value)[] values)
    {
        await using var command = new SqlCommand(sql, c, t); foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<(decimal Subtotal, decimal Tax, decimal GrandTotal)> ReadTotalsAsync(SqlConnection c, SqlTransaction t, Guid invoiceId)
    {
        await using var command = new SqlCommand("SELECT Subtotal,TaxAmount,GrandTotal FROM sales.SalesInvoices WHERE InvoiceId=@id", c, t);
        command.Parameters.AddWithValue("@id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2));
    }

    private static async Task<IReadOnlyCollection<(Guid ProductId, decimal Taxable, decimal Tax, decimal LineTotal)>> ReadLineTotalsAsync(SqlConnection c, SqlTransaction t, Guid invoiceId)
    {
        await using var command = new SqlCommand("SELECT i.ProductId,COALESCE(MAX(st.TaxableAmount),0),i.TaxAmount,i.LineTotal FROM sales.SalesInvoiceItems i LEFT JOIN sales.SalesTaxes st ON st.InvoiceItemId=i.InvoiceItemId WHERE i.InvoiceId=@id GROUP BY i.ProductId,i.TaxAmount,i.LineTotal", c, t);
        command.Parameters.AddWithValue("@id", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(Guid, decimal, decimal, decimal)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetGuid(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        return rows;
    }
}
