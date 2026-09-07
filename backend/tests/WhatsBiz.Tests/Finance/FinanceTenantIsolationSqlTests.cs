using System.Data;
using System.Globalization;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace WhatsBiz.Tests.Finance;

[Collection("SQL finance tenant isolation")]
public sealed class FinanceTenantIsolationSqlTests
{
    private static string ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("ConnectionStrings__IntegrationTests")
                ?? "Server=DESKTOP-DQ0868S;Database=WhatsBizERP;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connection Timeout=10";
            var separator = value.IndexOf('=');
            return separator >= 0 && value[..separator].Trim() == "$env:ConnectionStrings__IntegrationTests"
                ? value[(separator + 1)..].Trim().Trim('"')
                : value;
        }
    }

    [Fact]
    public async Task TwoTenantPostingAndDashboardReadsAreIsolatedAndAtomic()
    {
        var marker = $"P3-SQL-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var supplierB = Guid.NewGuid();
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await Gate(connection);
        try
        {
            await Execute(connection, """
                EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenantA;
                INSERT core.Tenants(TenantId,TenantKey,Name,CreatedBy) VALUES(@tenantA,@keyA,@nameA,@marker);
                INSERT sales.Customers(CustomerId,CustomerCode,CustomerName,CustomerType,TenantId,CreatedBy)
                VALUES(@customerA,@customerCode,@customerName,N'RETAIL',@tenantA,@marker);
                EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenantB;
                INSERT core.Tenants(TenantId,TenantKey,Name,CreatedBy) VALUES(@tenantB,@keyB,@nameB,@marker);
                INSERT purchase.Suppliers(SupplierId,SupplierCode,SupplierName,SupplierType,TenantId,CreatedBy)
                VALUES(@supplierB,@supplierCode,@supplierName,N'LOCAL',@tenantB,@marker);
                EXEC sys.sp_set_session_context @key=N'TenantId',@value=NULL;
                """, Parameters());

            await Gate(connection);
            await SetTenant(connection, tenantA);
            await Post(connection, tenantA, "RECEIPT", "CUSTOMER", customerA, "CASH", 125.50m, sourceA, marker);
            await Gate(connection);
            await SetTenant(connection, tenantB);
            await Post(connection, tenantB, "PAYMENT", "SUPPLIER", supplierB, "BANK", 77.25m, sourceB, marker);

            var journals = await Query(connection, """
                SELECT j.TenantId,d.DebitTotal,d.CreditTotal,
                 (SELECT COUNT(*) FROM finance.JournalEntryDetails x WHERE x.JournalEntryId=j.JournalEntryId
                  AND NOT((x.DebitAmount>0 AND x.CreditAmount=0)OR(x.CreditAmount>0 AND x.DebitAmount=0))) InvalidSides
                FROM finance.JournalEntries j JOIN finance.DayBook d ON d.JournalEntryId=j.JournalEntryId
                WHERE j.Narration=@marker ORDER BY j.TenantId;
                """, Parameters());
            journals.Rows.Should().HaveCount(2);
            journals.AsEnumerable().Should().OnlyContain(x => x.Field<decimal>("DebitTotal") == x.Field<decimal>("CreditTotal") && x.Field<int>("InvalidSides") == 0);
            journals.AsEnumerable().Select(x => x.Field<Guid>("TenantId")).Should().BeEquivalentTo([tenantA, tenantB]);

            await Gate(connection);
            await SetTenant(connection, tenantA);
            var dashboardA = await Dashboard(connection, tenantA);
            await Gate(connection);
            await SetTenant(connection, tenantB);
            var dashboardB = await Dashboard(connection, tenantB);
            dashboardA.Cash.Should().Be(125.50m);
            dashboardA.Bank.Should().Be(0m);
            dashboardB.Cash.Should().Be(0m);
            dashboardB.Bank.Should().Be(-77.25m);

            var before = await Scalar<int>(connection, "SELECT COUNT(*) FROM finance.JournalEntries WHERE Narration=@marker", Parameters());
            await SetTenant(connection, tenantA);
            var attack = async () => await Post(connection, tenantA, "PAYMENT", "SUPPLIER", supplierB, "CASH", 1m, Guid.NewGuid(), marker);
            await attack.Should().ThrowAsync<SqlException>().Where(x => x.Number == 51517);
            await SetTenant(connection, tenantB);
            var reverseAttack = async () => await Post(connection, tenantB, "RECEIPT", "CUSTOMER", customerA, "CASH", 1m, Guid.NewGuid(), marker);
            await reverseAttack.Should().ThrowAsync<SqlException>().Where(x => x.Number == 51517);
            var after = await Scalar<int>(connection, "SELECT COUNT(*) FROM finance.JournalEntries WHERE Narration=@marker", Parameters());
            after.Should().Be(before);
        }
        finally
        {
            await Gate(connection);
            await Execute(connection, """
                DECLARE @journals table(Id uniqueidentifier); INSERT @journals SELECT JournalEntryId FROM finance.JournalEntries WHERE Narration=@marker;
                DELETE FROM finance.BankBook WHERE JournalEntryId IN(SELECT Id FROM @journals);
                DELETE FROM finance.CashBook WHERE JournalEntryId IN(SELECT Id FROM @journals);
                DELETE FROM finance.CustomerLedger WHERE JournalEntryId IN(SELECT Id FROM @journals);
                DELETE FROM finance.SupplierLedger WHERE JournalEntryId IN(SELECT Id FROM @journals);
                DELETE FROM finance.DayBook WHERE JournalEntryId IN(SELECT Id FROM @journals);
                DELETE FROM finance.LedgerEntries WHERE JournalEntryId IN(SELECT Id FROM @journals);
                DELETE FROM finance.JournalEntryDetails WHERE JournalEntryId IN(SELECT Id FROM @journals);
                DELETE FROM finance.JournalEntries WHERE JournalEntryId IN(SELECT Id FROM @journals);
                EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenantA;
                DELETE FROM sales.Customers WHERE CustomerId=@customerA;
                EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenantB;
                DELETE FROM purchase.Suppliers WHERE SupplierId=@supplierB;
                EXEC sys.sp_set_session_context @key=N'TenantId',@value=NULL;
                DELETE FROM core.Tenants WHERE TenantId IN(@tenantA,@tenantB);
                """, Parameters());
        }

        IEnumerable<SqlParameter> Parameters() =>
        [
            new("@marker", marker), new("@tenantA", tenantA), new("@tenantB", tenantB),
            new("@customerA", customerA), new("@supplierB", supplierB),
            new("@keyA", $"{marker}-A"), new("@keyB", $"{marker}-B"),
            new("@nameA", $"{marker} Tenant A"), new("@nameB", $"{marker} Tenant B"),
            new("@customerCode", $"{marker}-CA"), new("@customerName", $"{marker} Customer A"),
            new("@supplierCode", $"{marker}-SB"), new("@supplierName", $"{marker} Supplier B")
        ];
    }

    private static async Task Gate(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT @@SERVERNAME,DB_NAME(),SYSTEM_USER", connection);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("DESKTOP-DQ0868S");
        reader.GetString(1).Should().Be("WhatsBizERP");
    }

    private static async Task SetTenant(SqlConnection connection, Guid tenant)
    {
        await using var command = new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant", connection);
        command.Parameters.AddWithValue("@tenant", tenant);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task Post(SqlConnection connection, Guid tenant, string transaction, string partyType, Guid party, string mode, decimal amount, Guid source, string marker)
    {
        await using var command = new SqlCommand("finance.PostPartyTransaction", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", tenant);
        command.Parameters.AddWithValue("@TransactionType", transaction);
        command.Parameters.AddWithValue("@PartyType", partyType);
        command.Parameters.AddWithValue("@PartyId", party);
        command.Parameters.AddWithValue("@PaymentMode", mode);
        command.Parameters.AddWithValue("@Amount", amount);
        command.Parameters.AddWithValue("@EntryDate", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@ReferenceNumber", marker);
        command.Parameters.AddWithValue("@Narration", marker);
        command.Parameters.AddWithValue("@CreatedBy", marker);
        command.Parameters.AddWithValue("@SourceId", source);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(decimal Cash, decimal Bank)> Dashboard(SqlConnection connection, Guid tenant)
    {
        await using var command = new SqlCommand("dashboard.Finance_Get", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@TenantId", tenant);
        command.Parameters.AddWithValue("@From", DateTimeOffset.UtcNow.AddDays(-1));
        command.Parameters.AddWithValue("@To", DateTimeOffset.UtcNow.AddDays(1));
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private static async Task Execute(SqlConnection connection, string sql, IEnumerable<SqlParameter> parameters)
    {
        await using var command = new SqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<DataTable> Query(SqlConnection connection, string sql, IEnumerable<SqlParameter> parameters)
    {
        await using var command = new SqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    private static async Task<T> Scalar<T>(SqlConnection connection, string sql, IEnumerable<SqlParameter> parameters)
    {
        await using var command = new SqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException(), typeof(T), CultureInfo.InvariantCulture);
    }
}

[CollectionDefinition("SQL finance tenant isolation", DisableParallelization = true)]
public sealed class FinanceTenantIsolationSqlCollectionDefinition;
