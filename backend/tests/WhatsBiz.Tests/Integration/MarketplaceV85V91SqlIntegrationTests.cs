using System.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit.Sdk;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Integration;

[CollectionDefinition("GuturGo marketplace SQL integration", DisableParallelization = true)]
public sealed class GuturGoMarketplaceSqlGroup
{
    public const string Name = "GuturGo marketplace SQL integration";
}

[Collection(GuturGoMarketplaceSqlGroup.Name)]
public sealed class MarketplaceV85V91SqlIntegrationTests
{
    private const string FixtureAuthorizationVariable = "WHATS_BIZ_DEV_MARKETPLACE_SQL_AUTHORIZATION";
    private const string FixtureAuthorizationValue = "I_AUTHORIZE_DEV_MARKETPLACE_ROLLBACK_FIXTURES";
    private const string DevServer = "DESKTOP-DQ0868S";
    private const string DevDatabase = "WhatsBizERP";
    private const string DevConnectionString = "Server=lpc:DESKTOP-DQ0868S;Database=WhatsBizERP;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Pooling=False;Application Name=GuturGo DEV SQL Marketplace Tests";

    [MarketplaceDevSqlFact(Timeout = 120_000)]
    public async Task V85ThroughV91LedgerValidatorsAndReleaseGateAreCurrent()
    {
        RequireFixtureAuthorization();
        await using var connection = await OpenApprovedAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await AssertDatabaseIdentityAsync(connection, transaction);
            var root = FindRepositoryRoot();
            await AssertCanonicalMarketplaceMigrationsAsync(connection, transaction, root);

            await AssertCheckoutReleaseDisabledAsync(connection, transaction);
        }
        finally { await RollbackAsync(transaction); }
    }

    [MarketplaceDevSqlFact(Timeout = 180_000)]
    public async Task SplitWarehouseReservationDoesNotDeductStockAndCompletionConsumesEachAllocationOnce()
    {
        RequireFixtureAuthorization();
        await using var connection = await OpenApprovedAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        Fixture? fixture = null;
        try
        {
            await AssertDatabaseIdentityAsync(connection, transaction);
            var root = FindRepositoryRoot();
            await AssertCanonicalMarketplaceMigrationsAsync(connection, transaction, root);
            await AssertCheckoutReleaseDisabledAsync(connection, transaction);
            fixture = new Fixture(connection, transaction);
            await fixture.SeedAsync();
            var invoice = await fixture.CreateHeldInvoiceAsync();
            var splitLine = await fixture.FindInvoiceLineAsync(fixture.SplitProductId, invoice);
            var otherLine = await fixture.FindInvoiceLineAsync(fixture.OtherProductId, invoice);
            var ownedLine = await fixture.FindInvoiceLineAsync(fixture.OwnedProductId, invoice);
            await fixture.CreateMarketplaceParentAsync(invoice);

            var groupA = await fixture.CreateGroupAsync(invoice, fixture.WarehouseAId, 1, fixture.PartnerSellerId);
            var groupB = await fixture.CreateGroupAsync(invoice, fixture.WarehouseBId, 2, fixture.PartnerSellerId);
            var groupC = await fixture.CreateGroupAsync(invoice, fixture.WarehouseAId, 3);
            var groupD = await fixture.CreateGroupAsync(invoice, fixture.WarehouseBId, 4, fixture.SecondPartnerSellerId);
            var splitA = await fixture.CreateAllocationAsync(invoice, splitLine, fixture.WarehouseAId, 2m, "split-a", fixture.PartnerSellerId, fixture.AgreementId);
            var splitB = await fixture.CreateAllocationAsync(invoice, splitLine, fixture.WarehouseBId, 1m, "split-b", fixture.PartnerSellerId, fixture.AgreementId);
            var otherB = await fixture.CreateAllocationAsync(invoice, otherLine, fixture.WarehouseBId, 1m, "other-b", fixture.SecondPartnerSellerId, fixture.SecondAgreementId, "ITEM_TAXABLE");
            var ownedA = await fixture.CreateAllocationAsync(invoice, ownedLine, fixture.WarehouseAId, 1m, "owned-a");
            await fixture.AttachAllocationAsync(groupA, splitA);
            await fixture.AttachAllocationAsync(groupB, splitB);
            await fixture.AttachAllocationAsync(groupC, ownedA);
            await fixture.AttachAllocationAsync(groupD, otherB);

            await MarketplacePartnerSettlementWriter.CreateDraftsAsync(connection, transaction,
                fixture.TenantId, invoice, "marketplace-sql-test", CancellationToken.None);
            await fixture.AssertSettlementCalculationsAsync(invoice, splitA, splitB, otherB, ownedA);

            // The duplicate write runs inside the same rollback-only transaction. The unique
            // settlement idempotency key must reject replay without duplicating ledger rows.
            var duplicatePosting = await Assert.ThrowsAsync<SqlException>(() =>
                MarketplacePartnerSettlementWriter.CreateDraftsAsync(connection, transaction,
                    fixture.TenantId, invoice, "marketplace-sql-test-replay", CancellationToken.None));
            duplicatePosting.Number.Should().BeOneOf(2601, 2627);
            await fixture.AssertSettlementCalculationsAsync(invoice, splitA, splitB, otherB, ownedA);

            var settlementGroupA = await fixture.FindGroupAsync(invoice, 1);
            await fixture.CreateRefundObligationAsync(invoice, settlementGroupA, 200m, "settlement-cancel-refund");
            await fixture.CreateCancellationAdjustmentsAsync(invoice, settlementGroupA, "rollback fixture cancellation");
            await fixture.CreateCancellationAdjustmentsAsync(invoice, settlementGroupA, "duplicate rollback fixture cancellation");
            await fixture.AssertCancellationAdjustmentAsync(invoice, settlementGroupA);

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(20);
            await fixture.ReserveAsync(invoice, splitA, expiresAt, "res-a");
            await fixture.ReserveAsync(invoice, splitA, expiresAt, "res-a");
            foreach (var (allocationId, key) in new[] { (splitB, "res-b"), (otherB, "res-other") })
                await fixture.ReserveAsync(invoice, allocationId, expiresAt, key);
            await fixture.ReserveAsync(invoice, ownedA, expiresAt, "res-owned");

            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseAId)).Should().Be((2m, 2m));
            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseBId)).Should().Be((1m, 1m));
            (await fixture.StockAsync(fixture.OtherProductId, fixture.WarehouseBId)).Should().Be((1m, 1m));
            (await fixture.StockAsync(fixture.OwnedProductId, fixture.WarehouseAId)).Should().Be((1m, 1m));
            await fixture.AssertSqlAsync("""
                SELECT CASE WHEN COUNT(*)=0 THEN 1 ELSE 0 END
                FROM inventory.InventoryTransactions WHERE TenantId=@tenant AND ReferenceType=N'MARKETPLACE_ORDER' AND ReferenceId=@order;
                """, invoice, expected: 1, "Reservation must not deduct stock or post inventory transactions.");

            await fixture.AdvanceOrderAsync(invoice, "RESERVED");
            await fixture.AdvanceOrderAsync(invoice, "CONFIRMED");
            (await fixture.CompleteAsync(invoice)).Should().BeFalse("the first completion must perform stock consumption, not report a replay");

            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseAId)).Should().Be((0m, 0m));
            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseBId)).Should().Be((0m, 0m));
            (await fixture.StockAsync(fixture.OtherProductId, fixture.WarehouseBId)).Should().Be((0m, 0m));
            (await fixture.StockAsync(fixture.OwnedProductId, fixture.WarehouseAId)).Should().Be((0m, 0m));
            await fixture.AssertSqlAsync("""
                SELECT CASE WHEN COUNT(DISTINCT t.TransactionId)=2 AND COUNT(*)=4 AND SUM(d.Quantity)=5
                  AND COUNT(DISTINCT CASE WHEN t.WarehouseId=@warehouseA THEN t.TransactionId END)=1
                  AND COUNT(DISTINCT CASE WHEN t.WarehouseId=@warehouseB THEN t.TransactionId END)=1
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseA AND d.ProductId=@split THEN d.Quantity ELSE 0 END)=2
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseA AND d.ProductId=@owned THEN d.Quantity ELSE 0 END)=1
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseB AND d.ProductId=@split THEN d.Quantity ELSE 0 END)=1
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseB AND d.ProductId=@other THEN d.Quantity ELSE 0 END)=1
                  THEN 1 ELSE 0 END
                FROM inventory.InventoryTransactions t
                JOIN inventory.InventoryTransactionDetails d ON d.TransactionId=t.TransactionId
                WHERE t.TenantId=@tenant AND t.ReferenceType=N'MARKETPLACE_ORDER' AND t.ReferenceId=@order AND t.TransactionType=N'SALE';
                """, invoice, expected: 1, "Completion must post exactly one transaction header per warehouse and each product quantity to its allocated source.",
                ("@warehouseA", fixture.WarehouseAId), ("@warehouseB", fixture.WarehouseBId),
                ("@split", fixture.SplitProductId), ("@other", fixture.OtherProductId), ("@owned", fixture.OwnedProductId));
            await fixture.AssertSqlAsync("""
                SELECT CASE WHEN COUNT(*)=4 AND MIN(ReservationStatus)='CONSUMED' AND MAX(ReservationStatus)='CONSUMED'
                  AND SUM(ConsumedQuantity)=5 AND SUM(ReleasedQuantity)=0 THEN 1 ELSE 0 END
                FROM commerce.MarketplaceAllocationReservations WHERE TenantId=@tenant AND OrderId=@order;
                """, invoice, expected: 1, "All allocated quantities must consume their reservations exactly once.");

            var replay = await fixture.CompleteAsync(invoice);
            replay.Should().BeTrue("a repeated completion must be an idempotent replay");
            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseAId)).Should().Be((0m, 0m));
            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseBId)).Should().Be((0m, 0m));
            (await fixture.StockAsync(fixture.OtherProductId, fixture.WarehouseBId)).Should().Be((0m, 0m));
            (await fixture.StockAsync(fixture.OwnedProductId, fixture.WarehouseAId)).Should().Be((0m, 0m));
            await fixture.AssertSqlAsync("""
                SELECT CASE WHEN COUNT(DISTINCT t.TransactionId)=2 AND COUNT(*)=4 AND SUM(d.Quantity)=5
                  AND COUNT(DISTINCT CASE WHEN t.WarehouseId=@warehouseA THEN t.TransactionId END)=1
                  AND COUNT(DISTINCT CASE WHEN t.WarehouseId=@warehouseB THEN t.TransactionId END)=1
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseA AND d.ProductId=@split THEN d.Quantity ELSE 0 END)=2
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseA AND d.ProductId=@owned THEN d.Quantity ELSE 0 END)=1
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseB AND d.ProductId=@split THEN d.Quantity ELSE 0 END)=1
                  AND SUM(CASE WHEN t.WarehouseId=@warehouseB AND d.ProductId=@other THEN d.Quantity ELSE 0 END)=1
                  THEN 1 ELSE 0 END
                FROM inventory.InventoryTransactions t
                JOIN inventory.InventoryTransactionDetails d ON d.TransactionId=t.TransactionId
                WHERE t.TenantId=@tenant AND t.ReferenceType=N'MARKETPLACE_ORDER' AND t.ReferenceId=@order AND t.TransactionType=N'SALE';
                """, invoice, expected: 1, "Completion replay must not add headers, detail lines or warehouse quantity changes.",
                ("@warehouseA", fixture.WarehouseAId), ("@warehouseB", fixture.WarehouseBId),
                ("@split", fixture.SplitProductId), ("@other", fixture.OtherProductId), ("@owned", fixture.OwnedProductId));

            var tenantFailure = await Assert.ThrowsAsync<SqlException>(() => fixture.ReserveAsync(invoice, splitA, expiresAt, "cross-tenant", Guid.NewGuid()));
            tenantFailure.Number.Should().Be(85030, "the stored procedure must reject a tenant context mismatch");
        }
        finally
        {
            if (fixture is not null) await fixture.ClearTenantContextBestEffortAsync();
            await RollbackAsync(transaction);
        }
    }

    [MarketplaceDevSqlFact(Timeout = 120_000)]
    public async Task InsufficientEligibleStockRollsBackTheWholeParentOrderTransaction()
    {
        RequireFixtureAuthorization();
        await using var connection = await OpenApprovedAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var fixture = new Fixture(connection, transaction);
        try
        {
            await AssertDatabaseIdentityAsync(connection, transaction);
            await AssertCanonicalMarketplaceMigrationsAsync(connection, transaction, FindRepositoryRoot());
            await AssertCheckoutReleaseDisabledAsync(connection, transaction);
            await fixture.SeedAsync();
            var invoice = await fixture.CreateHeldInvoiceAsync();
            var line = await fixture.FindInvoiceLineAsync(fixture.SplitProductId, invoice);
            await fixture.CreateMarketplaceParentAsync(invoice);
            var allocation = await fixture.CreateAllocationAsync(invoice, line, fixture.WarehouseAId, 3m, "insufficient-three-at-one-source");

            var rollback = await ReserveInsufficientAndCountRowsAsync(connection, transaction, fixture.TenantId, invoice, allocation,
                DateTimeOffset.UtcNow.AddMinutes(5), $"{fixture.TestTag}-insufficient-stock", fixture.TestTag);
            rollback.ErrorNumber.Should().Be(85034, "the source warehouse has only two unreserved units");
            rollback.OrphanCount.Should().Be(0, "the procedure's rollback must remove all temporary order rows");
        }
        finally
        {
            // The stored procedure rolls back the caller transaction on an allocation failure.
            await fixture.ClearTenantContextBestEffortAsync();
            await RollbackAsync(transaction);
        }
    }

    [MarketplaceDevSqlFact(Timeout = 120_000)]
    public async Task UnsupportedPartnerAgreementCreatesReviewRequiredZeroPayable()
    {
        RequireFixtureAuthorization();
        await using var connection = await OpenApprovedAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var fixture = new Fixture(connection, transaction);
        try
        {
            await AssertDatabaseIdentityAsync(connection, transaction);
            await AssertCanonicalMarketplaceMigrationsAsync(connection, transaction, FindRepositoryRoot());
            await AssertCheckoutReleaseDisabledAsync(connection, transaction);
            await fixture.SeedAsync();
            var invoice = await fixture.CreateHeldInvoiceAsync();
            var line = await fixture.FindInvoiceLineAsync(fixture.SplitProductId, invoice);
            await fixture.CreateMarketplaceParentAsync(invoice);
            var group = await fixture.CreateGroupAsync(invoice, fixture.WarehouseAId, 1, fixture.PartnerSellerId);
            var allocation = await fixture.CreateAllocationAsync(invoice, line, fixture.WarehouseAId, 3m,
                "unsupported-commission", fixture.PartnerSellerId, fixture.AgreementId,
                commissionTypeSnapshot: "FIXED");
            await fixture.AttachAllocationAsync(group, allocation);

            await MarketplacePartnerSettlementWriter.CreateDraftsAsync(connection, transaction,
                fixture.TenantId, invoice, "marketplace-unsupported-agreement-test", CancellationToken.None);
            await fixture.AssertReviewRequiredSettlementAsync(invoice, allocation);
        }
        finally
        {
            await fixture.ClearTenantContextBestEffortAsync();
            await RollbackAsync(transaction);
        }
    }

    [MarketplaceDevSqlFact(Timeout = 120_000)]
    public async Task ExpiredReservationBatchReleasesReservedQuantityWithoutDeductingOnHandStock()
    {
        RequireFixtureAuthorization();
        await using var connection = await OpenApprovedAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var fixture = new Fixture(connection, transaction);
        try
        {
            await AssertDatabaseIdentityAsync(connection, transaction);
            await AssertCanonicalMarketplaceMigrationsAsync(connection, transaction, FindRepositoryRoot());
            await AssertCheckoutReleaseDisabledAsync(connection, transaction);
            await fixture.SeedAsync();
            var invoice = await fixture.CreateHeldInvoiceAsync();
            var splitLine = await fixture.FindInvoiceLineAsync(fixture.SplitProductId, invoice);
            var partnerLine = await fixture.FindInvoiceLineAsync(fixture.OtherProductId, invoice);
            var ownedLine = await fixture.FindInvoiceLineAsync(fixture.OwnedProductId, invoice);
            var expiry = DateTimeOffset.UtcNow.AddSeconds(10);
            await fixture.CreateMarketplaceParentAsync(invoice, expiry);
            var groupA = await fixture.CreateGroupAsync(invoice, fixture.WarehouseAId, 1, fixture.PartnerSellerId);
            var groupB = await fixture.CreateGroupAsync(invoice, fixture.WarehouseBId, 2, fixture.PartnerSellerId);
            var groupC = await fixture.CreateGroupAsync(invoice, fixture.WarehouseAId, 3);
            var groupD = await fixture.CreateGroupAsync(invoice, fixture.WarehouseBId, 4, fixture.SecondPartnerSellerId);
            var splitA = await fixture.CreateAllocationAsync(invoice, splitLine, fixture.WarehouseAId, 2m, "expire-split-a", fixture.PartnerSellerId, fixture.AgreementId);
            var splitB = await fixture.CreateAllocationAsync(invoice, splitLine, fixture.WarehouseBId, 1m, "expire-split-b", fixture.PartnerSellerId, fixture.AgreementId);
            var partner = await fixture.CreateAllocationAsync(invoice, partnerLine, fixture.WarehouseBId, 1m, "expire-partner", fixture.SecondPartnerSellerId, fixture.SecondAgreementId, "ITEM_TAXABLE");
            var owned = await fixture.CreateAllocationAsync(invoice, ownedLine, fixture.WarehouseAId, 1m, "expire-owned");
            await fixture.AttachAllocationAsync(groupA, splitA);
            await fixture.AttachAllocationAsync(groupB, splitB);
            await fixture.AttachAllocationAsync(groupC, owned);
            await fixture.AttachAllocationAsync(groupD, partner);
            foreach (var (allocationId, key) in new[] { (splitA, "expire-split-a"), (splitB, "expire-split-b"), (partner, "expire-partner"), (owned, "expire-owned") })
                await fixture.ReserveAsync(invoice, allocationId, expiry, key);
            await fixture.AdvanceOrderAsync(invoice, "RESERVED");

            await Task.Delay(TimeSpan.FromSeconds(11));
            await fixture.ExpireReservationsAsync(100);

            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseAId)).Should().Be((2m, 0m));
            (await fixture.StockAsync(fixture.SplitProductId, fixture.WarehouseBId)).Should().Be((1m, 0m));
            (await fixture.StockAsync(fixture.OtherProductId, fixture.WarehouseBId)).Should().Be((1m, 0m));
            (await fixture.StockAsync(fixture.OwnedProductId, fixture.WarehouseAId)).Should().Be((1m, 0m));
            await fixture.AssertSqlAsync("""
                SELECT CASE WHEN COUNT(*)=4 AND MIN(ReservationStatus)='EXPIRED'
                  AND SUM(ReleasedQuantity)=5 AND SUM(ConsumedQuantity)=0 THEN 1 ELSE 0 END
                FROM commerce.MarketplaceAllocationReservations WHERE TenantId=@tenant AND OrderId=@order;
                """, invoice, expected: 1, "Expiry releases the reservation without consuming inventory.");
            await fixture.AssertSqlAsync("""
                SELECT CASE WHEN COUNT(*)=1 AND MIN(LifecycleStatus)='EXPIRED' THEN 1 ELSE 0 END
                FROM commerce.MarketplaceOrders WHERE TenantId=@tenant AND OrderId=@order;
                """, invoice, expected: 1, "The parent order must become expired with its reservation.");
        }
        finally
        {
            await fixture.ClearTenantContextBestEffortAsync();
            await RollbackAsync(transaction);
        }
    }

    private static async Task<SqlConnection> OpenApprovedAsync()
    {
        var builder = new SqlConnectionStringBuilder(DevConnectionString);
        if (!string.Equals(builder.DataSource, "lpc:DESKTOP-DQ0868S", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(builder.InitialCatalog, DevDatabase, StringComparison.OrdinalIgnoreCase) ||
            !builder.IntegratedSecurity || builder.UserID.Length != 0 || builder.Password.Length != 0)
            throw new InvalidOperationException("Marketplace SQL test connection is not the hard-coded Windows-authenticated DEV target.");

        var connection = new SqlConnection(DevConnectionString);
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task AssertDatabaseIdentityAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var identity = new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME();", connection, transaction);
        await using var reader = await identity.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || !string.Equals(reader.GetString(0), DevServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(1), DevDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Marketplace SQL test target identity does not exactly match DESKTOP-DQ0868S/WhatsBizERP.");
    }

    private static async Task RollbackAsync(SqlTransaction transaction)
    {
        try { await transaction.RollbackAsync(); }
        catch (Exception error) when (error is InvalidOperationException or SqlException) { }
    }

    internal static async Task AssertCheckoutReleaseDisabledAsync(SqlConnection connection, SqlTransaction transaction)
    {
        await using var gate = new SqlCommand("SELECT COUNT(*) FROM core.Features WHERE FeatureKey=N'MARKETPLACE_CHECKOUT' AND ReleaseState=N'DISABLED';", connection, transaction);
        Convert.ToInt32(await gate.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(1, "the global marketplace checkout release gate must be disabled before any fixture writes");
    }

    private static void RequireFixtureAuthorization()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(FixtureAuthorizationVariable), FixtureAuthorizationValue, StringComparison.Ordinal))
            throw SkipException.ForSkip($"DEV marketplace SQL test NOT RUN: explicit authorization is required in {FixtureAuthorizationVariable}.");
    }

    internal static async Task AssertCanonicalMarketplaceMigrationsAsync(SqlConnection connection, SqlTransaction transaction, string root)
    {
        var migrations = Enumerable.Range(41, 51).Select(version =>
        {
            var prefix = $"V{version}-";
            var file = Directory.GetFiles(Path.Combine(root, "database", "migrations"), prefix + "*.sql")
                .SingleOrDefault(path => !path.EndsWith(".validate.sql", StringComparison.OrdinalIgnoreCase));
            if (file is null) throw new InvalidOperationException($"Canonical migration V{version} is missing from this source checkout.");
            var validator = Path.Combine(root, "database", "migrations", "validators", Path.GetFileNameWithoutExtension(file) + ".validate.sql");
            if (!File.Exists(validator)) throw new InvalidOperationException($"V{version} validator is missing from this source checkout.");
            return (Version: version, File: file, Validator: validator,
                ScriptHash: Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                ValidatorHash: Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(validator))));
        }).ToArray();

        await using (var ledger = new SqlCommand("SELECT MigrationVersion,MigrationName,ScriptHash,ValidatorHash FROM deployment.DatabaseMigrationHistory WHERE MigrationVersion BETWEEN 41 AND 91 ORDER BY MigrationVersion;", connection, transaction))
        await using (var reader = await ledger.ExecuteReaderAsync())
        {
            var index = 0;
            while (await reader.ReadAsync())
            {
                if (index >= migrations.Length) throw new InvalidOperationException("The migration ledger contains extra rows in V41–V91.");
                var migration = migrations[index++];
                reader.GetInt32(0).Should().Be(migration.Version);
                reader.GetString(1).Should().Be(Path.GetFileName(migration.File));
                reader.GetString(2).Trim().Should().Be(migration.ScriptHash);
                reader.GetString(3).Trim().Should().Be(migration.ValidatorHash);
            }
            index.Should().Be(migrations.Length, "the V41–V91 canonical ledger range must be complete");
        }

        using var configuration = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "deployment", "integration-database.config.json")));
        var historicalCommit = configuration.RootElement.GetProperty("historicalBaselineCommit").GetString()
            ?? throw new InvalidOperationException("The pinned V40 historical source commit is missing.");
        var baselineHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "database", "migrations", "validators", "Baseline-V40.validate.sql"))));
        await using (var baseline = new SqlCommand("SELECT BaselineVersion,BaselineName,BaselineValidatorFile,BaselineValidatorHash FROM deployment.DatabaseMigrationBaseline WHERE SingletonId=1;", connection, transaction))
        await using (var reader = await baseline.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Canonical V40 baseline metadata is absent.");
            reader.GetInt32(0).Should().Be(40);
            reader.GetString(1).Should().Be($"Historical V40 source {historicalCommit}");
            reader.GetString(2).Should().Be("Baseline-V40.validate.sql");
            reader.GetString(3).Trim().Should().Be(baselineHash);
            if (await reader.ReadAsync()) throw new InvalidOperationException("Multiple canonical V40 baseline records exist.");
        }

        await using (var pending = new SqlCommand("SELECT COUNT(*) FROM deployment.DatabaseMigrationHistory WHERE MigrationVersion<=40 OR MigrationVersion>91;", connection, transaction))
            Convert.ToInt32(await pending.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture).Should().Be(0,
                "the database must match the latest checked-in migration sequence");

        var baselineValidator = Path.Combine(root, "database", "migrations", "validators", "Baseline-V40.validate.sql");
        var baselineValidatorSql = await File.ReadAllTextAsync(baselineValidator);
        if (Regex.IsMatch(baselineValidatorSql, @"(?im)\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE)\b"))
            throw new InvalidOperationException("The V40 baseline validator contains a write/schema operation and will not be executed by the test harness.");
        await ExecuteScriptAsync(connection, transaction, baselineValidator);

        foreach (var migration in migrations.Where(x => x.Version >= 85))
        {
            var validatorSql = await File.ReadAllTextAsync(migration.Validator);
            if (Regex.IsMatch(validatorSql, @"(?im)\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE)\b"))
                throw new InvalidOperationException($"V{migration.Version} validator contains a write/schema operation and will not be executed by the test harness.");
            await ExecuteScriptAsync(connection, transaction, migration.Validator);
        }
    }

    private static async Task<(int ErrorNumber, int OrphanCount)> ReserveInsufficientAndCountRowsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid orderId, Guid allocationId,
        DateTimeOffset expiresAt, string idempotencyKey, string actor)
    {
        await using var command = new SqlCommand("""
            DECLARE @error int=NULL;
            BEGIN TRY
              EXEC commerce.Marketplace_ReserveAllocation
                @TenantId=@tenant,@AllocationId=@allocation,@ExpiresAt=@expires,
                @ReservationIdempotencyKey=@key,@CreatedBy=@actor;
            END TRY
            BEGIN CATCH
              SET @error=ERROR_NUMBER();
            END CATCH;
            SELECT @error ErrorNumber,
              (SELECT COUNT(*) FROM sales.SalesInvoices WHERE InvoiceId=@order)
              +(SELECT COUNT(*) FROM commerce.MarketplaceOrders WHERE OrderId=@order)
              +(SELECT COUNT(*) FROM commerce.MarketplaceOrderAllocations WHERE OrderId=@order)
              +(SELECT COUNT(*) FROM commerce.MarketplaceFulfilmentGroups WHERE OrderId=@order) OrphanCount;
            """, connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@order", orderId);
        command.Parameters.AddWithValue("@allocation", allocationId);
        command.Parameters.AddWithValue("@expires", expiresAt);
        command.Parameters.AddWithValue("@key", idempotencyKey);
        command.Parameters.AddWithValue("@actor", actor);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("The insufficient-stock reservation returned no rollback result.");
        return (reader.GetInt32(reader.GetOrdinal("ErrorNumber")), reader.GetInt32(reader.GetOrdinal("OrphanCount")));
    }

    private static async Task ExecuteScriptAsync(SqlConnection connection, SqlTransaction transaction, string path)
    {
        var sql = await File.ReadAllTextAsync(path);
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                     .Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            await using var command = new SqlCommand(batch, connection, transaction) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "database/migrations/V91-MarketplacePartnerSettlementAccounting.sql"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate canonical V85-V91 migrations for the marketplace SQL test.");
    }

    internal sealed class Fixture
    {
        private const string CommittedFixtureServer = "DESKTOP-DQ0868S";
        private const string CommittedFixtureDatabase = "WhatsBizERP_GuturGoMarketplaceTest_V91";
        private readonly SqlConnection connection;
        private readonly SqlTransaction? transaction;
        private readonly Guid tenantId = Guid.NewGuid();
        private readonly Guid customerId = Guid.NewGuid();
        private readonly Guid categoryId = Guid.NewGuid();
        private readonly Guid brandId = Guid.NewGuid();
        private readonly Guid unitId = Guid.NewGuid();
        private readonly Guid warehouseA = Guid.NewGuid();
        private readonly Guid warehouseB = Guid.NewGuid();
        private readonly Guid splitProduct = Guid.NewGuid();
        private readonly Guid otherProduct = Guid.NewGuid();
        private readonly Guid ownedProduct = Guid.NewGuid();
        private readonly Guid testRunId;
        private readonly Guid fixtureId = Guid.NewGuid();
        private string tag => "MKTSQLTEST" + testRunId.ToString("N")[..8] + fixtureId.ToString("N")[..4];

        public Fixture(SqlConnection connection, SqlTransaction transaction)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            testRunId = Guid.NewGuid();
            if (transaction.Connection != connection)
                throw new InvalidOperationException("Rollback-only marketplace fixtures require their explicit transaction on the same SQL connection.");
        }

        private Fixture(SqlConnection connection, Guid testRunId)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.testRunId = testRunId;
            if (!string.Equals(connection.DataSource, CommittedFixtureServer, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(connection.Database, CommittedFixtureDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Committed marketplace fixtures are allowed only on the dedicated synthetic V91 test database.");
            transaction = null;
        }

        public static Fixture CreateCommittedSyntheticV91(SqlConnection connection, Guid testRunId) => new(connection,testRunId);

        public Guid SplitProductId => splitProduct;
        public Guid OtherProductId => otherProduct;
        public Guid OwnedProductId => ownedProduct;
        public Guid WarehouseAId => warehouseA;
        public Guid WarehouseBId => warehouseB;
        public Guid TenantId => tenantId;
        public string TestTag => tag;
        public Guid TestRunId => testRunId;
        public Guid PartnerSellerId { get; } = Guid.NewGuid();
        public Guid AgreementId { get; } = Guid.NewGuid();
        public Guid SecondPartnerSellerId { get; } = Guid.NewGuid();
        public Guid SecondAgreementId { get; } = Guid.NewGuid();

        public async Task SeedAsync()
        {
            // Create the new fixture tenant first, then establish the normal application context
            // before any tenant-guarded warehouse, seller, customer or inventory writes.
            await ExecuteAsync("INSERT core.Tenants(TenantId,TenantKey,Name) VALUES(@tenant,@tag,@tag);",
                ("@tenant", tenantId), ("@tag", tag));
            await SetTenantContextAsync();
            await ExecuteAsync("""
                SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
                SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
                INSERT master.ProductCategories(ProductCategoryId,CategoryCode,CategoryName,DisplayOrder) VALUES(@category,@tag,@tag,1);
                INSERT master.Brands(BrandId,BrandCode,BrandName) VALUES(@brand,@tag,@tag);
                INSERT master.UnitsOfMeasure(UnitId,UnitCode,UnitName,ShortName,DecimalPlaces) VALUES(@unit,@tag,@tag,N'ea',0);
                INSERT sales.Customers(CustomerId,CustomerCode,CustomerName,CustomerType,TenantId) VALUES(@customer,@tag,@tag,N'RETAIL',@tenant);
                INSERT commerce.PartnerSellers(PartnerSellerId,OperatingTenantId,SellerName,SellerType,VerificationStatus,SellerStatus,CreatedBy)
                  VALUES(@seller,@tenant,N'Rollback-only marketplace partner',N'BUSINESS',N'VERIFIED',N'ACTIVE',@tag);
                INSERT commerce.PartnerCommissionAgreements(AgreementId,OperatingTenantId,PartnerSellerId,ProductId,AgreementVersion,
                  CommissionType,CommissionValue,CalculationBasis,DiscountResponsibility,DeliveryResponsibility,PackagingResponsibility,
                  PaymentFeeResponsibility,ReturnTreatment,SettlementCycle,EffectiveFrom,AgreementStatus,CreatedBy)
                  VALUES(@agreement,@tenant,@seller,NULL,1,N'PERCENT',10,N'ITEM_GROSS',N'PLATFORM',N'RETAILER',N'RETAILER',
                    N'PLATFORM',N'STANDARD',N'WEEKLY',DATEADD(day,-1,SYSUTCDATETIME()),N'ACTIVE',@tag);
                INSERT commerce.PartnerSellers(PartnerSellerId,OperatingTenantId,SellerName,SellerType,VerificationStatus,SellerStatus,CreatedBy)
                  VALUES(@seller2,@tenant,N'Rollback-only second partner',N'BUSINESS',N'VERIFIED',N'ACTIVE',@tag);
                INSERT commerce.PartnerCommissionAgreements(AgreementId,OperatingTenantId,PartnerSellerId,ProductId,AgreementVersion,
                  CommissionType,CommissionValue,CalculationBasis,DiscountResponsibility,DeliveryResponsibility,PackagingResponsibility,
                  PaymentFeeResponsibility,ReturnTreatment,SettlementCycle,EffectiveFrom,AgreementStatus,CreatedBy)
                  VALUES(@agreement2,@tenant,@seller2,NULL,1,N'PERCENT',10,N'ITEM_TAXABLE',N'PLATFORM',N'RETAILER',N'RETAILER',
                    N'PLATFORM',N'STANDARD',N'WEEKLY',DATEADD(day,-1,SYSUTCDATETIME()),N'ACTIVE',@tag);
                INSERT inventory.Warehouses(WarehouseId,TenantId,WarehouseCode,WarehouseName,WarehouseTypeId,IsDefault)
                  SELECT @warehouseA,@tenant,@tag+N'A',@tag+N'A',WarehouseTypeId,0 FROM inventory.WarehouseTypes WHERE TypeCode=N'GENERAL';
                INSERT inventory.Warehouses(WarehouseId,TenantId,WarehouseCode,WarehouseName,WarehouseTypeId,IsDefault)
                  SELECT @warehouseB,@tenant,@tag+N'B',@tag+N'B',WarehouseTypeId,0 FROM inventory.WarehouseTypes WHERE TypeCode=N'GENERAL';
                INSERT master.Products(ProductId,ProductCode,ProductName,CategoryId,BrandId,UnitId,GSTPercentage,PurchasePrice,SellingPrice,MRP,
                  MinimumStock,MaximumStock,ReorderLevel,IsBatchManaged,IsSerialManaged,TenantId)
                  VALUES(@split,@tag+N'1',@tag+N' split',@category,@brand,@unit,0,10,100,100,0,1000,0,0,0,@tenant),
                        (@other,@tag+N'2',@tag+N' partner-taxable',@category,@brand,@unit,18,10,50,50,0,1000,0,0,0,@tenant),
                        (@owned,@tag+N'3',@tag+N' owned',@category,@brand,@unit,0,10,30,30,0,1000,0,0,0,@tenant);
                INSERT inventory.InventoryBalances(InventoryBalanceId,ProductId,WarehouseId,TenantId,QuantityOnHand,QuantityReserved,AverageCost,LastPurchaseCost)
                  VALUES(NEWID(),@split,@warehouseA,@tenant,2,0,10,10),(NEWID(),@split,@warehouseB,@tenant,1,0,10,10),
                    (NEWID(),@other,@warehouseB,@tenant,1,0,10,10),(NEWID(),@owned,@warehouseA,@tenant,1,0,10,10);
                """, ("@tenant", tenantId), ("@customer", customerId), ("@category", categoryId), ("@brand", brandId), ("@unit", unitId),
                ("@warehouseA", warehouseA), ("@warehouseB", warehouseB), ("@split", splitProduct), ("@other", otherProduct),
                ("@seller", PartnerSellerId), ("@agreement", AgreementId), ("@seller2", SecondPartnerSellerId),
                ("@agreement2", SecondAgreementId), ("@owned", ownedProduct), ("@tag", tag));
        }

        public async Task SetTenantContextAsync()
        {
            await ExecuteAsync("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;", ("@tenant", tenantId));
            await using var verify = new SqlCommand("SELECT TRY_CONVERT(uniqueidentifier,SESSION_CONTEXT(N'TenantId'));", connection, transaction);
            (await verify.ExecuteScalarAsync()).Should().Be(tenantId, "fixture context uses the application's Guid-valued TenantId convention");
        }

        public async Task ClearTenantContextBestEffortAsync()
        {
            try
            {
                await ExecuteAsync("EXEC sys.sp_set_session_context @key=N'TenantId',@value=NULL;");
            }
            catch (Exception error) when (error is InvalidOperationException or SqlException)
            {
                // Stored procedures can roll back the caller transaction. This fixture disables
                // connection pooling, so closing the physical SQL session clears any remaining context.
            }
        }

        public async Task<Guid> CreateHeldInvoiceAsync()
        {
            var items = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new { ProductId = splitProduct, Quantity = 3m, UnitPrice = 100m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 0m },
                new { ProductId = otherProduct, Quantity = 1m, UnitPrice = 50m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 18m },
                new { ProductId = ownedProduct, Quantity = 1m, UnitPrice = 30m, DiscountPercentage = 0m, DiscountAmount = 0m, TaxPercentage = 0m }
            });
            await using var command = new SqlCommand("sales.POS_PostInvoice", connection, transaction) { CommandType = CommandType.StoredProcedure };
            Add(command, "@WarehouseId", warehouseA); Add(command, "@CustomerId", customerId); Add(command, "@ItemsJson", items);
            Add(command, "@PaymentsJson", "[]"); Add(command, "@Status", "HELD"); Add(command, "@TenantId", tenantId); Add(command, "@CreatedBy", tag);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("POS invoice procedure returned no parent invoice.");
            var invoiceId = reader.GetGuid(reader.GetOrdinal("InvoiceId"));
            var amount = reader.GetDecimal(reader.GetOrdinal("GrandTotal"));
            amount.Should().Be(389m, "the persisted parent invoice is the one payable obligation");
            await reader.CloseAsync();
            await ExecuteAsync("UPDATE sales.SalesInvoices SET OrderType='MARKETPLACE' WHERE TenantId=@tenant AND InvoiceId=@order AND Status='HELD';", ("@tenant", tenantId), ("@order", invoiceId));
            return invoiceId;
        }

        public Task<Guid> FindInvoiceLineAsync(Guid productId, Guid invoiceId) => ScalarGuidAsync(
            "SELECT InvoiceItemId FROM sales.SalesInvoiceItems WHERE InvoiceId=@order AND ProductId=@product;", ("@order", invoiceId), ("@product", productId));

        public Task CreateMarketplaceParentAsync(Guid invoiceId, DateTimeOffset? expiresAt = null) => ExecuteAsync("""
            INSERT commerce.MarketplaceOrders(TenantId,OrderId,OrderKind,LifecycleStatus,CheckoutIdempotencyKey,ReservationExpiresAt)
            VALUES(@tenant,@order,'MARKETPLACE','DRAFT',@key,@expires);
            """, ("@tenant", tenantId), ("@order", invoiceId), ("@key", tag), ("@expires", expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(20)));

        public async Task<Guid> CreateGroupAsync(Guid invoiceId, Guid warehouseId, short number, Guid? partnerSellerId = null)
        {
            await ExecuteAsync("""
                INSERT commerce.MarketplaceFulfilmentGroups(TenantId,OrderId,GroupNumber,GroupIdempotencyKey,WarehouseId,SellerKind,PartnerSellerId,DeliveryAddressSnapshot)
                VALUES(@tenant,@order,@number,@key,@warehouse,@sellerKind,@seller,N'rollback integration fixture');
                """, ("@tenant", tenantId), ("@order", invoiceId), ("@number", number), ("@key", $"{tag}-group-{number}"),
                ("@warehouse", warehouseId), ("@sellerKind", partnerSellerId.HasValue ? "PARTNER" : "OWNED"),
                ("@seller", (object?)partnerSellerId ?? DBNull.Value));
            return await ScalarGuidAsync("SELECT FulfilmentGroupId FROM commerce.MarketplaceFulfilmentGroups WHERE TenantId=@tenant AND OrderId=@order AND GroupNumber=@number;",
                ("@tenant", tenantId), ("@order", invoiceId), ("@number", number));
        }

        public Task<Guid> FindGroupAsync(Guid invoiceId, short number) => ScalarGuidAsync(
            "SELECT FulfilmentGroupId FROM commerce.MarketplaceFulfilmentGroups WHERE TenantId=@tenant AND OrderId=@order AND GroupNumber=@number;",
            ("@tenant", tenantId), ("@order", invoiceId), ("@number", number));

        public Task CreateRefundObligationAsync(Guid invoiceId, Guid groupId, decimal amount, string key) => ExecuteAsync("""
            INSERT commerce.MarketplaceFulfilmentRefundObligations(TenantId,OrderId,FulfilmentGroupId,RefundStatus,RefundAmount,ReasonCode,IdempotencyKey,CreatedBy)
            VALUES(@tenant,@order,@group,'REFUND_REQUIRED',@amount,'CANCELLED_GROUP',@key,@actor);
            """, ("@tenant", tenantId), ("@order", invoiceId), ("@group", groupId), ("@amount", amount),
            ("@key", $"{tag}-{key}"), ("@actor", tag));

        public async Task CreateCancellationAdjustmentsAsync(Guid invoiceId, Guid groupId, string reason)
        {
            await using var command = new SqlCommand("commerce.PartnerSettlement_CreateCancellationReviews", connection, transaction)
                { CommandType = CommandType.StoredProcedure };
            Add(command, "@TenantId", tenantId); Add(command, "@OrderId", invoiceId);
            Add(command, "@FulfilmentGroupId", groupId); Add(command, "@Actor", tag); Add(command, "@Reason", reason);
            await command.ExecuteNonQueryAsync();
        }

        public Task AssertCancellationAdjustmentAsync(Guid invoiceId, Guid groupId) => AssertSqlAsync("""
            SELECT CASE WHEN COUNT(*)=1 AND MIN(s.SettlementStatus)='REVIEW_REQUIRED'
              AND MIN(s.EntryType)='ADJUSTMENT' AND MIN(s.AdjustmentDirection)='DECREASE'
              AND MIN(s.SellerPayable)=0 AND SUM(CASE WHEN s.ReversesEntryId=sale.SettlementEntryId THEN 1 ELSE 0 END)=1
              AND MIN(r.RefundStatus)='REFUND_REQUIRED' AND MIN(r.RefundAmount)=200
              THEN 1 ELSE 0 END
            FROM commerce.PartnerSettlementEntries s
            JOIN commerce.PartnerSettlementEntries sale ON sale.OperatingTenantId=s.OperatingTenantId
              AND sale.SettlementEntryId=s.ReversesEntryId AND sale.EntryType='SALE'
            JOIN commerce.MarketplaceFulfilmentRefundObligations r ON r.TenantId=s.OperatingTenantId
              AND r.OrderId=s.MarketplaceOrderId AND r.FulfilmentGroupId=s.FulfilmentGroupId
            WHERE s.OperatingTenantId=@tenant AND s.MarketplaceOrderId=@order
              AND s.FulfilmentGroupId=@group AND s.EntryType='ADJUSTMENT';
            """, invoiceId, expected: 1,
            "Cancellation adjustment is linked once to its sale entry and remains held for refund review.",
            ("@group", groupId));

        public async Task<Guid> CreateAllocationAsync(Guid invoiceId, Guid lineId, Guid warehouseId, decimal quantity, string key,
            Guid? partnerSellerId = null, Guid? agreementId = null, string calculationBasis = "ITEM_GROSS",
            string commissionTypeSnapshot = "PERCENT")
        {
            var id = Guid.NewGuid();
            if (partnerSellerId is Guid seller && agreementId is Guid agreement)
            {
                await ExecuteAsync("""
                    INSERT commerce.MarketplaceOrderAllocations(AllocationId,TenantId,OrderId,InvoiceItemId,WarehouseId,AllocatedQuantity,
                      SellerKind,PartnerSellerId,AgreementId,AllocationIdempotencyKey,AgreementVersionSnapshot,CommissionTypeSnapshot,
                      CommissionValueSnapshot,CalculationBasisSnapshot,DiscountResponsibilitySnapshot,DeliveryResponsibilitySnapshot,
                      PackagingResponsibilitySnapshot,PaymentFeeResponsibilitySnapshot,ReturnTreatmentSnapshot)
                    VALUES(@id,@tenant,@order,@line,@warehouse,@quantity,'PARTNER',@seller,@agreement,@key,1,@commissionType,10,
                      @basis,'PLATFORM','RETAILER','RETAILER','PLATFORM','STANDARD');
                    """, ("@id", id), ("@tenant", tenantId), ("@order", invoiceId), ("@line", lineId), ("@warehouse", warehouseId),
                    ("@quantity", quantity), ("@key", $"{tag}-{key}"), ("@seller", seller), ("@agreement", agreement),
                    ("@basis", calculationBasis), ("@commissionType", commissionTypeSnapshot));
            }
            else
                await ExecuteAsync("""
                    INSERT commerce.MarketplaceOrderAllocations(AllocationId,TenantId,OrderId,InvoiceItemId,WarehouseId,AllocatedQuantity,SellerKind,AllocationIdempotencyKey)
                    VALUES(@id,@tenant,@order,@line,@warehouse,@quantity,'OWNED',@key);
                    """, ("@id", id), ("@tenant", tenantId), ("@order", invoiceId), ("@line", lineId), ("@warehouse", warehouseId), ("@quantity", quantity), ("@key", $"{tag}-{key}"));
            return id;
        }

        public Task AssertSettlementCalculationsAsync(Guid orderId, Guid allocationA, Guid allocationB, Guid taxableAllocation, Guid ownedAllocation) => AssertSqlAsync("""
            SELECT CASE WHEN COUNT(*)=3 AND SUM(GrossAmount)=350 AND SUM(CommissionAmount)=35 AND SUM(SellerPayable)=315
              AND SUM(CASE WHEN SettlementStatus='DRAFT' THEN 1 ELSE 0 END)=3
              AND SUM(CASE WHEN AllocationId IN(@a,@b,@taxable) THEN 1 ELSE 0 END)=3
              AND SUM(CASE WHEN AllocationId=@owned THEN 1 ELSE 0 END)=0
              AND SUM(CASE WHEN AllocationId=@taxable AND CalculationBasis='ITEM_TAXABLE' AND GrossAmount=50 AND CommissionAmount=5 AND SellerPayable=45 THEN 1 ELSE 0 END)=1
              THEN 1 ELSE 0 END
            FROM commerce.PartnerSettlementEntries
            WHERE OperatingTenantId=@tenant AND MarketplaceOrderId=@order AND EntryType='SALE';
            """, orderId, 1, "The real settlement writer must apportion one partner line over its 2+1 warehouse allocations and omit owned stock.",
            ("@a", allocationA), ("@b", allocationB), ("@taxable", taxableAllocation), ("@owned", ownedAllocation));

        public Task AssertReviewRequiredSettlementAsync(Guid orderId, Guid allocationId) => AssertSqlAsync("""
            SELECT CASE WHEN COUNT(*)=1 AND MIN(SettlementStatus)='REVIEW_REQUIRED'
              AND MIN(GrossAmount)=0 AND MIN(CommissionAmount)=0 AND MIN(SellerPayable)=0
              AND MIN(CASE WHEN NULLIF(LTRIM(RTRIM(ReviewReason)),N'') IS NULL THEN 0 ELSE 1 END)=1
              THEN 1 ELSE 0 END
            FROM commerce.PartnerSettlementEntries
            WHERE OperatingTenantId=@tenant AND MarketplaceOrderId=@order AND AllocationId=@allocation AND EntryType='SALE';
            """, orderId, 1, "Unsupported agreement terms must create a review entry with zero unresolved payable.",
            ("@allocation", allocationId));

        public Task AttachAllocationAsync(Guid groupId, Guid allocationId) => ExecuteAsync(
            "INSERT commerce.MarketplaceFulfilmentGroupItems(TenantId,FulfilmentGroupId,AllocationId) VALUES(@tenant,@group,@allocation);",
            ("@tenant", tenantId), ("@group", groupId), ("@allocation", allocationId));

        public async Task ReserveAsync(Guid orderId, Guid allocationId, DateTimeOffset expiresAt, string key, Guid? requestedTenantId = null)
        {
            await using var command = new SqlCommand("commerce.Marketplace_ReserveAllocation", connection, transaction) { CommandType = CommandType.StoredProcedure };
            Add(command, "@TenantId", requestedTenantId ?? tenantId); Add(command, "@AllocationId", allocationId); Add(command, "@ExpiresAt", expiresAt);
            Add(command, "@ReservationIdempotencyKey", $"{tag}-{key}"); Add(command, "@CreatedBy", tag);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(reader.GetOrdinal("ReservationStatus")).Should().Be("RESERVED");
        }

        public Task<(decimal OnHand, decimal Reserved)> StockAsync(Guid productId, Guid warehouseId) => ScalarTupleAsync("""
            SELECT QuantityOnHand,QuantityReserved FROM inventory.InventoryBalances
            WHERE TenantId=@tenant AND ProductId=@product AND WarehouseId=@warehouse;
            """, ("@tenant", tenantId), ("@product", productId), ("@warehouse", warehouseId));

        public Task AdvanceOrderAsync(Guid orderId, string state) => ExecuteAsync(
            "UPDATE commerce.MarketplaceOrders SET LifecycleStatus=@state,UpdatedAt=SYSUTCDATETIME() WHERE TenantId=@tenant AND OrderId=@order;",
            ("@state", state), ("@tenant", tenantId), ("@order", orderId));

        public async Task<bool> CompleteAsync(Guid orderId)
        {
            await using var command = new SqlCommand("commerce.Marketplace_CompleteOrder", connection, transaction) { CommandType = CommandType.StoredProcedure };
            Add(command, "@TenantId", tenantId); Add(command, "@OrderId", orderId); Add(command, "@ModifiedBy", tag);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return reader.GetBoolean(reader.GetOrdinal("IsReplay"));
        }

        public async Task ExpireReservationsAsync(int batchSize)
        {
            await using var command = new SqlCommand("commerce.Marketplace_ExpireReservationBatch", connection, transaction) { CommandType = CommandType.StoredProcedure };
            Add(command, "@TenantId", tenantId); Add(command, "@BatchSize", batchSize); Add(command, "@ModifiedBy", tag);
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                var countOrdinal = -1;
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    if (string.Equals(reader.GetName(ordinal), "ExpiredOrderCount", StringComparison.OrdinalIgnoreCase))
                    {
                        countOrdinal = ordinal;
                        break;
                    }
                }

                if (countOrdinal >= 0)
                {
                    (await reader.ReadAsync()).Should().BeTrue();
                    Convert.ToInt32(reader.GetValue(countOrdinal), System.Globalization.CultureInfo.InvariantCulture).Should().Be(1);
                    return;
                }
            }
            while (await reader.NextResultAsync());

            throw new InvalidOperationException("Marketplace_ExpireReservationBatch did not return its final ExpiredOrderCount result set.");
        }

        public async Task AssertSqlAsync(string sql, Guid orderId, int expected, string because,
            params (string Name, object Value)[] additionalParameters)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            Add(command, "@tenant", tenantId); Add(command, "@order", orderId);
            foreach (var (name, value) in additionalParameters) Add(command, name, value);
            Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture).Should().Be(expected, because);
        }

        private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters) Add(command, name, value);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<Guid> ScalarGuidAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters) Add(command, name, value);
            return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected fixture row was not created."));
        }

        private async Task<(decimal, decimal)> ScalarTupleAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            foreach (var (name, value) in parameters) Add(command, name, value);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) throw new InvalidOperationException("Expected inventory balance was not found.");
            return (reader.GetDecimal(0), reader.GetDecimal(1));
        }

        private static void Add(SqlCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
    }
}

