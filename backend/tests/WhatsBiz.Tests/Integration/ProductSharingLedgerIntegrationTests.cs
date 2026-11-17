using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using WhatsBiz.Infrastructure.Features;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Application.Common.Features;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class ProductSharingLedgerIntegrationTests
{
    [WhatsBiz.Tests.Integration.SqlIntegrationFact]
    public async Task V71RerunsPreserveOverridesAndTenantScopedSendClaimsUntilRollback()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        connection.Database.Should().Be("WhatsBizERP_PosGstIntegrationTest");
        await using (var identity = new SqlCommand("SELECT @@SERVERNAME", connection))
            (await identity.ExecuteScalarAsync()).Should().Be("DESKTOP-DQ0868S");
        async Task<bool> LedgerExists()
        {
            await using var exists = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE object_id=OBJECT_ID(N'integration.WhatsAppProductShareRequests')", connection);
            return Convert.ToInt32(await exists.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        var ledgerExisted = await LedgerExists();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database/migrations/V70-WhatsAppProductSharing.sql"))) root = root.Parent;
        root.Should().NotBeNull();
        var migration = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations/V70-WhatsAppProductSharing.sql"));
        var validator = await File.ReadAllTextAsync(Path.Combine(root.FullName, "database/migrations/validators/V70-WhatsAppProductSharing.validate.sql"));
        var upgrades = new[] { "V67-StorefrontScheduledAvailability", "V68-StorefrontFeaturedCategories", "V69-StorefrontPopupCampaigns", "V70-WhatsAppProductSharing", "V71-StorefrontFeatureEntitlements" };
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); var request = Guid.NewGuid();
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            try
            {
                async Task Execute(string sql)
                {
                    foreach (var batch in Regex.Split(sql, @"(?im)^\s*GO\s*(?:--.*)?$"))
                        if (!string.IsNullOrWhiteSpace(batch)) { await using var command = new SqlCommand(batch, connection, transaction); await command.ExecuteNonQueryAsync(); }
                }
                foreach (var name in upgrades) await Execute(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations",name+".sql")));
                await Execute(migration);await Execute(validator);
                var v71=await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/V71-StorefrontFeatureEntitlements.sql"));
                foreach (var tenant in new[] { tenantA, tenantB })
                {
                    await using var create = new SqlCommand("INSERT core.Tenants(TenantId,TenantKey,Name,IsActive) VALUES(@tenant,@key,N'Isolated sharing test',1)", connection, transaction);
                    create.Parameters.AddWithValue("@tenant", tenant); create.Parameters.AddWithValue("@key", "share-" + tenant.ToString("N")); await create.ExecuteNonQueryAsync();
                }
                await Execute(v71);
                await Execute(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/validators/V71-StorefrontFeatureEntitlements.validate.sql")));
                await using(var entitled=new SqlCommand("""
                    DECLARE @plan uniqueidentifier=NEWID();
                    INSERT core.Plans(PlanId,PlanKey,Name,IsActive) VALUES(@plan,@key,N'Isolated feature test',1);
                    INSERT core.PlanFeatures(PlanFeatureId,PlanId,FeatureId,IsEnabled)
                      SELECT NEWID(),@plan,FeatureId,1 FROM core.Features WHERE FeatureKey IN(N'V1',N'ADMINISTRATION',N'PRODUCTS',N'STOREFRONT_SCHEDULED_SELLING');
                    INSERT core.Subscriptions(SubscriptionId,TenantId,PlanId,StartDate,IsActive) VALUES(NEWID(),@tenant,@plan,DATEADD(day,-1,SYSUTCDATETIME()),1);
                    INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,IsActive)
                      SELECT NEWID(),@tenant,f.FeatureId,1,1 FROM core.Features f WHERE f.FeatureKey IN(N'V1',N'ADMINISTRATION',N'PRODUCTS') AND NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=@tenant AND tf.FeatureId=f.FeatureId);
                    UPDATE tf SET IsEnabled=1 FROM core.TenantFeatures tf JOIN core.Features f ON f.FeatureId=tf.FeatureId WHERE tf.TenantId=@tenant AND f.FeatureKey IN(N'V1',N'ADMINISTRATION',N'PRODUCTS',N'STOREFRONT_SCHEDULED_SELLING');
                    """,connection,transaction))
                {entitled.Parameters.AddWithValue("@tenant",tenantA);entitled.Parameters.AddWithValue("@key","share-"+tenantA.ToString("N"));await entitled.ExecuteNonQueryAsync();}
                await using(var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options))
                {
                    await db.Database.UseTransactionAsync(transaction);
                    using var cache=new MemoryCache(new MemoryCacheOptions());
                    var features=new FeatureService(db,cache,Options.Create(new GlobalFeatureOptions()));
                    (await features.IsEnabledAsync(tenantA,FeatureKeys.StorefrontScheduledSelling)).Should().BeTrue();
                    (await features.GetEffectiveFeaturesAsync(tenantA))[FeatureKeys.StorefrontScheduledSelling].Should().BeTrue();
                    await using(var disable=new SqlCommand("UPDATE tf SET IsEnabled=0 FROM core.TenantFeatures tf JOIN core.Features f ON f.FeatureId=tf.FeatureId WHERE tf.TenantId=@tenant AND f.FeatureKey=N'STOREFRONT_SCHEDULED_SELLING'",connection,transaction))
                    {disable.Parameters.AddWithValue("@tenant",tenantA);await disable.ExecuteNonQueryAsync();}
                    (await features.IsEnabledAsync(tenantA,FeatureKeys.StorefrontScheduledSelling)).Should().BeFalse("runtime must bypass a previously cached enabled result");
                    await Execute(v71);
                    (await features.IsEnabledAsync(tenantA,FeatureKeys.StorefrontScheduledSelling)).Should().BeFalse("rerunning migration must preserve owner overrides");
                    (await features.IsEnabledAsync(tenantB,FeatureKeys.StorefrontScheduledSelling)).Should().BeFalse("another tenant has no entitled subscription");
                }
                var product = Guid.NewGuid(); var customer = Guid.NewGuid(); var actor = Guid.NewGuid();
                // Execute the same locking claim used by the actual send boundary, not a test-only approximation.
                var sourcePath = Path.Combine(root.FullName, "backend/src/WhatsBiz.Infrastructure/WhatsAppCommerce/WhatsAppCommerceService.ProductSharing.cs");
                var source = await File.ReadAllTextAsync(sourcePath);
                var claimSql = Regex.Match(source, "\"\"\"\\s*(INSERT integration\\.WhatsAppProductShareRequests[\\s\\S]*?)\"\"\"").Groups[1].Value;
                claimSql.Should().NotBeEmpty();
                async Task<int> Claim(Guid tenant)
                {
                    await using var claim = new SqlCommand(claimSql, connection, transaction);
                    claim.Parameters.AddWithValue("@tenant", tenant); claim.Parameters.AddWithValue("@request", request);
                    claim.Parameters.AddWithValue("@product", product); claim.Parameters.AddWithValue("@customer", customer);
                    claim.Parameters.AddWithValue("@actor", actor); claim.Parameters.AddWithValue("@mode", "MOCK");
                    claim.Parameters.AddWithValue("@content", "Product\n₹118\nTry today\nhttps://example.test/share");
                    return Convert.ToInt32(await claim.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
                }
                (await Claim(tenantA)).Should().Be(1);
                (await Claim(tenantA)).Should().Be(0, "a repeated click must not reclaim/send the same request");
                (await Claim(tenantB)).Should().Be(1, "request IDs are isolated by tenant");
                await using var rows = new SqlCommand("SELECT COUNT(*) FROM integration.WhatsAppProductShareRequests WHERE TenantId=@tenant AND RequestId=@request AND ConsentConfirmedBy=@actor AND Status='PENDING'", connection, transaction);
                rows.Parameters.AddWithValue("@tenant", tenantA); rows.Parameters.AddWithValue("@request", request); rows.Parameters.AddWithValue("@actor", actor);
                (await rows.ExecuteScalarAsync()).Should().Be(1);
                await using var content=new SqlCommand("SELECT SentContent FROM integration.WhatsAppProductShareRequests WHERE TenantId=@tenant AND RequestId=@request",connection,transaction);
                content.Parameters.AddWithValue("@tenant",tenantA);content.Parameters.AddWithValue("@request",request);
                ((string)(await content.ExecuteScalarAsync())!).Should().Contain("Try today");
            }
            finally { await transaction.RollbackAsync(); }
        }
        await using var cleanup = new SqlCommand("SELECT COUNT(*) FROM core.Tenants WHERE TenantId IN(@a,@b)", connection);
        cleanup.Parameters.AddWithValue("@a", tenantA); cleanup.Parameters.AddWithValue("@b", tenantB);
        (await cleanup.ExecuteScalarAsync()).Should().Be(0, "all fixtures and migration DDL are rolled back");
        await using var configurationCleanup=new SqlCommand("SELECT (SELECT COUNT(*) FROM core.Plans WHERE PlanKey IN(@ka,@kb))+(SELECT COUNT(*) FROM core.Subscriptions WHERE TenantId IN(@a,@b))+(SELECT COUNT(*) FROM core.TenantFeatures WHERE TenantId IN(@a,@b))",connection);
        configurationCleanup.Parameters.AddWithValue("@a",tenantA);configurationCleanup.Parameters.AddWithValue("@b",tenantB);
        configurationCleanup.Parameters.AddWithValue("@ka","share-"+tenantA.ToString("N"));configurationCleanup.Parameters.AddWithValue("@kb","share-"+tenantB.ToString("N"));
        (await configurationCleanup.ExecuteScalarAsync()).Should().Be(0,"subscription/feature fixtures must also be removed");
        (await LedgerExists()).Should().Be(ledgerExisted, "the schema must return to its pre-test state");
        if (ledgerExisted)
        {
            await using var sends = new SqlCommand("SELECT COUNT(*) FROM integration.WhatsAppProductShareRequests WHERE TenantId IN(@a,@b)", connection);
            sends.Parameters.AddWithValue("@a", tenantA); sends.Parameters.AddWithValue("@b", tenantB);
            (await sends.ExecuteScalarAsync()).Should().Be(0);
        }
    }
}
