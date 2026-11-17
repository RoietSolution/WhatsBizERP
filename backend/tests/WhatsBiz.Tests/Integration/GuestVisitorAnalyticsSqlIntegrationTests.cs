using System.Text.RegularExpressions;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class GuestVisitorAnalyticsSqlIntegrationTests
{
    private const string FixtureAuthorizationVariable = "WHATS_BIZ_V82_SQL_FIXTURE_AUTHORIZATION";
    private const string FixtureAuthorizationValue = "I_AUTHORIZE_V82_SQL_FIXTURES";

    [WhatsBiz.Tests.Integration.SqlIntegrationFact(Timeout = 90_000)]
    public async Task V82ConstraintsAndRetentionWorkerAreTenantScopedAndCleanUpSafely()
    {
        var connectionString = SqlIntegrationDatabase.ConnectionString;
        IntegrationDatabaseTarget.AssertApproved(new SqlConnectionStringBuilder(connectionString));
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
            await AssertApprovedDatabaseIdentityAsync(connection);
            var root = FindRepositoryRoot();
            await AssertV82CanonicalMigrationAsync(connection,
                Path.Combine(root, "database/migrations/V82-GuestVisitorAnalytics.sql"),
                Path.Combine(root, "database/migrations/validators/V82-GuestVisitorAnalytics.validate.sql"));
        }
        if (!string.Equals(Environment.GetEnvironmentVariable(FixtureAuthorizationVariable), FixtureAuthorizationValue, StringComparison.Ordinal))
            throw new InvalidOperationException($"Set {FixtureAuthorizationVariable} to its exact value before allowing rollback/cleanup SQL fixtures to run.");

        var tenantId = Guid.NewGuid();
        var storeKey = "V82_INTEGRATION_" + tenantId.ToString("N");
        var expiredSessionId = Guid.NewGuid();
        var liveSessionId = Guid.NewGuid();
        var expiredVisitorId = Guid.NewGuid();
        var liveVisitorId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var searchId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var consentSessionId = Guid.NewGuid();
        var consentVisitorId = Guid.NewGuid();
        var consentEventId = Guid.NewGuid();
        var blockedEventId = Guid.NewGuid();
        var withdrawalSearchId = Guid.NewGuid();
        var withdrawalRequestId = Guid.NewGuid();
        var legacyEventId = Guid.NewGuid();
        var legacySearchId = Guid.NewGuid();
        var searchTerm = "v82-fixture-" + Guid.NewGuid().ToString("N");
        var withdrawalTerm = "v82-withdrawal-" + Guid.NewGuid().ToString("N");
        var legacyTerm = "v78-legacy-" + Guid.NewGuid().ToString("N");
        var policyInserted = false;
        var tenantFixturesInserted = false;
        var otherTenantId = Guid.NewGuid();
        var otherStoreKey = "V82_INTEGRATION_" + otherTenantId.ToString("N");
        var otherPolicyInserted = false;
        Exception? testFailure = null;

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
            await AssertApprovedDatabaseIdentityAsync(connection);

            await using (var tenantTransaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                await using var tenantFixture = new SqlCommand("INSERT core.Tenants(TenantId,TenantKey,Name,IsActive,CreatedBy) VALUES(@tenant,@tenantKey,N'V82 SQL integration fixture',1,N'v82-sql-test'),(@otherTenant,@otherTenantKey,N'V82 SQL integration fixture',1,N'v82-sql-test');", connection, tenantTransaction);
                tenantFixture.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                tenantFixture.Parameters.Add("@tenantKey", System.Data.SqlDbType.NVarChar, 100).Value = storeKey;
                tenantFixture.Parameters.Add("@otherTenant", System.Data.SqlDbType.UniqueIdentifier).Value = otherTenantId;
                tenantFixture.Parameters.Add("@otherTenantKey", System.Data.SqlDbType.NVarChar, 100).Value = otherStoreKey;
                await tenantFixture.ExecuteNonQueryAsync();
                tenantFixturesInserted = true;
                await tenantTransaction.CommitAsync();
            }

            await using (var policy = new SqlCommand("INSERT commerce.GuestVisitorAnalyticsPolicies(TenantId,RetentionDays,PolicyReviewedAtUtc,PolicyReviewedBy) VALUES(@tenant,1,SYSUTCDATETIME(),N'v82-sql-test');", connection))
            {
                policy.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                await policy.ExecuteNonQueryAsync();
                policyInserted = true;
            }

            await using (var fixture = new SqlCommand("""
INSERT commerce.GuestVisitorAnalyticsSessions(TenantId,SessionId,VisitorId,NoticeVersion,DeviceCategory,BrowserCategory,OperatingSystemCategory,ViewportCategory,TrafficSource)
VALUES(@tenant,@expired,@expiredVisitor,N'guest-analytics-v1','MOBILE','OTHER','OTHER','SMALL','DIRECT'),(@tenant,@live,@liveVisitor,N'guest-analytics-v1','DESKTOP','OTHER','OTHER','LARGE','DIRECT');
UPDATE commerce.GuestVisitorAnalyticsSessions SET LastActivityAtUtc=DATEADD(day,-2,SYSUTCDATETIME()) WHERE TenantId=@tenant AND SessionId=@expired;
INSERT commerce.AnalyticsEvents(TenantId,EventType,CustomerId,MetadataJson,GuestSessionId,ClientEventId,ResultCount,NormalizedSearchTerm)
VALUES(@tenant,N'STORE_VISIT',NULL,NULL,@expired,@event,NULL,NULL);
INSERT commerce.ProductSearchEvents(SearchEventId,TenantId,CustomerId,AnonymousSessionId,OriginalKeyword,NormalizedKeyword,ResultCount,InStockResultCount,OutOfStockResultCount,Outcome,GuestSessionId)
VALUES(@search,@tenant,NULL,NULL,@term,@term,0,0,0,N'NO_RESULTS',@expired);
INSERT commerce.MissingProductRequests(MissingProductRequestId,TenantId,SearchEventId,CustomerId,AnonymousSessionId,RequestedKeyword,NormalizedKeyword)
VALUES(@request,@tenant,@search,NULL,NULL,@term,@term);
INSERT commerce.AnalyticsEvents(AnalyticsEventId,TenantId,EventType,CustomerId,MetadataJson,GuestSessionId,ClientEventId,CreatedOn)
VALUES(@legacyEvent,@tenant,N'PAGE_VIEW',NULL,NULL,NULL,NULL,DATEADD(year,-2,SYSUTCDATETIME()));
INSERT commerce.ProductSearchEvents(SearchEventId,TenantId,CustomerId,AnonymousSessionId,OriginalKeyword,NormalizedKeyword,ResultCount,InStockResultCount,OutOfStockResultCount,Outcome,CreatedAtUtc)
VALUES(@legacySearch,@tenant,NULL,NULL,@legacyTerm,@legacyTerm,0,0,0,N'NO_RESULTS',DATEADD(year,-2,SYSUTCDATETIME()));
""", connection))
            {
                fixture.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                fixture.Parameters.Add("@expired", System.Data.SqlDbType.UniqueIdentifier).Value = expiredSessionId;
                fixture.Parameters.Add("@live", System.Data.SqlDbType.UniqueIdentifier).Value = liveSessionId;
                fixture.Parameters.Add("@expiredVisitor", System.Data.SqlDbType.UniqueIdentifier).Value = expiredVisitorId;
                fixture.Parameters.Add("@liveVisitor", System.Data.SqlDbType.UniqueIdentifier).Value = liveVisitorId;
                fixture.Parameters.Add("@event", System.Data.SqlDbType.UniqueIdentifier).Value = eventId;
                fixture.Parameters.Add("@legacyEvent", System.Data.SqlDbType.UniqueIdentifier).Value = legacyEventId;
                fixture.Parameters.Add("@search", System.Data.SqlDbType.UniqueIdentifier).Value = searchId;
                fixture.Parameters.Add("@request", System.Data.SqlDbType.UniqueIdentifier).Value = requestId;
                fixture.Parameters.Add("@legacySearch", System.Data.SqlDbType.UniqueIdentifier).Value = legacySearchId;
                fixture.Parameters.Add("@legacyTerm", System.Data.SqlDbType.NVarChar, 200).Value = legacyTerm;
                fixture.Parameters.Add("@term", System.Data.SqlDbType.NVarChar, 200).Value = searchTerm;
                await fixture.ExecuteNonQueryAsync();
            }

            // The composite tenant/session FK must reject pairing a valid session with another tenant.
            await using (var otherPolicy = new SqlCommand("INSERT commerce.GuestVisitorAnalyticsPolicies(TenantId,RetentionDays,PolicyReviewedAtUtc,PolicyReviewedBy) VALUES(@tenant,1,SYSUTCDATETIME(),N'v82-sql-test');", connection))
            {
                otherPolicy.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = otherTenantId;
                await otherPolicy.ExecuteNonQueryAsync();
                otherPolicyInserted = true;
            }
            await AssertConstraintRejectsAsync(connection, "INSERT commerce.AnalyticsEvents(TenantId,EventType,GuestSessionId,ClientEventId) VALUES(@other,N'STORE_VISIT',@session,NEWID());", otherTenantId, expiredSessionId);
            await AssertConstraintRejectsAsync(connection, "INSERT commerce.AnalyticsEvents(TenantId,EventType,GuestSessionId,ClientEventId,ResultCount) VALUES(@tenant,N'STORE_VISIT',@session,NEWID(),-1);", tenantId, expiredSessionId);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["GuestAnalytics:CollectionEnabled"] = "true",
                ["GuestAnalytics:RetentionCleanupEnabled"] = "true"
            }).Build();
            var health = new GuestVisitorAnalyticsRetentionHealth();
            health.MarkHealthy();
            var analytics = new GuestVisitorAnalyticsService(configuration, new EnabledFeatures(), new TestCurrentUser(tenantId, true), new EmptyStorefront(), health);
            var status = await analytics.StatusAsync(storeKey, CancellationToken.None);
            status.Available.Should().BeTrue();
            await analytics.RecordAsync(storeKey, new(consentSessionId, [new(Guid.NewGuid(), "STORE_VISIT")]), CancellationToken.None);
            (await GuestSessionEventCountAsync(connection, tenantId, consentSessionId)).Should().Be(0);
            await analytics.SetConsentAsync(storeKey, new(consentVisitorId, consentSessionId, true, GuestVisitorAnalyticsService.NoticeVersion, "SMALL", "DIRECT"), "Mozilla/5.0 (Linux; Android 14; Tablet) Chrome/120.0", CancellationToken.None);
            var duplicate = new GuestAnalyticsEventInput(consentEventId, "FOREGROUND_ENGAGEMENT", EngagedSeconds: 23);
            await analytics.RecordAsync(storeKey, new(consentSessionId, [duplicate, duplicate]), CancellationToken.None);
            await VerifyConsentAndIdempotencyAsync(connection, tenantId, consentSessionId, consentEventId);

            await using (var withdrawalFixture = new SqlCommand("""
INSERT commerce.ProductSearchEvents(SearchEventId,TenantId,CustomerId,AnonymousSessionId,OriginalKeyword,NormalizedKeyword,ResultCount,InStockResultCount,OutOfStockResultCount,Outcome,GuestSessionId)
VALUES(@search,@tenant,NULL,NULL,@term,@term,0,0,0,N'NO_RESULTS',@session);
INSERT commerce.MissingProductRequests(MissingProductRequestId,TenantId,SearchEventId,CustomerId,AnonymousSessionId,RequestedKeyword,NormalizedKeyword)
VALUES(@request,@tenant,@search,NULL,NULL,@term,@term);
""", connection))
            {
                withdrawalFixture.Parameters.Add("@search", System.Data.SqlDbType.UniqueIdentifier).Value = withdrawalSearchId;
                withdrawalFixture.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                withdrawalFixture.Parameters.Add("@term", System.Data.SqlDbType.NVarChar, 200).Value = withdrawalTerm;
                withdrawalFixture.Parameters.Add("@session", System.Data.SqlDbType.UniqueIdentifier).Value = consentSessionId;
                withdrawalFixture.Parameters.Add("@request", System.Data.SqlDbType.UniqueIdentifier).Value = withdrawalRequestId;
                await withdrawalFixture.ExecuteNonQueryAsync();
            }
            await analytics.RecordAsync(storeKey, new(consentSessionId, [new(consentEventId, "PAGE_VIEW")]), CancellationToken.None);

            var worker = new GuestVisitorAnalyticsRetentionWorker(configuration, health, NullLogger<GuestVisitorAnalyticsRetentionWorker>.Instance);
            var failedWorkerConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "not a SQL connection string",
                ["GuestAnalytics:RetentionCleanupEnabled"] = "true"
            }).Build();
            var failingWorker = new GuestVisitorAnalyticsRetentionWorker(failedWorkerConfiguration, health, NullLogger<GuestVisitorAnalyticsRetentionWorker>.Instance);
            await Assert.ThrowsAnyAsync<Exception>(() => failingWorker.RunRetentionBatchAsync(10, CancellationToken.None, tenantId));
            health.IsHealthy.Should().BeFalse();
            await analytics.RecordAsync(storeKey, new(liveSessionId, [new(blockedEventId, "STORE_VISIT")]), CancellationToken.None);
            (await EventCountAsync(connection, tenantId, blockedEventId)).Should().Be(0);

            var result = await worker.RunRetentionBatchAsync(10, CancellationToken.None, tenantId);
            health.IsHealthy.Should().BeTrue("a successful retention batch re-enables opted-in collection");
            result.Sessions.Should().Be(1);
            result.Events.Should().Be(1);
            result.Searches.Should().Be(1);

            await analytics.RecordAsync(storeKey, new(liveSessionId, [new(blockedEventId, "STORE_VISIT")]), CancellationToken.None);
            (await EventCountAsync(connection, tenantId, blockedEventId)).Should().Be(1);

            var crossTenantAnalytics = new GuestVisitorAnalyticsService(configuration, new EnabledFeatures(), new TestCurrentUser(otherTenantId, false), new EmptyStorefront(), health);
            await crossTenantAnalytics.RecordAsync(otherStoreKey, new(consentSessionId, [new(blockedEventId, "STORE_VISIT")]), CancellationToken.None);
            (await EventCountAsync(connection, otherTenantId, blockedEventId)).Should().Be(0);

            var retailerReport = await analytics.RetailerReportAsync(tenantId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);
            retailerReport.Sessions.Should().BeGreaterThan(0);
            var ownerReport = await analytics.OwnerReportAsync(tenantId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None);
            ownerReport.Sessions.Should().BeGreaterThan(0);
            var nonOwnerAnalytics = new GuestVisitorAnalyticsService(configuration, new EnabledFeatures(), new TestCurrentUser(tenantId, false), new EmptyStorefront(), health);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => nonOwnerAnalytics.OwnerReportAsync(tenantId, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None));

            await analytics.SetConsentAsync(storeKey, new(consentVisitorId, consentSessionId, false, GuestVisitorAnalyticsService.NoticeVersion, "SMALL"), null, CancellationToken.None);
            await VerifyWithdrawalAsync(connection, tenantId, consentSessionId, consentEventId, withdrawalSearchId, withdrawalRequestId);

            await using (var verify = new SqlCommand("""
SELECT
 (SELECT COUNT(*) FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId=@expired),
 (SELECT COUNT(*) FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND ClientEventId=@event),
 (SELECT COUNT(*) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND SearchEventId=@search),
 (SELECT COUNT(*) FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId=@live),
 (SELECT COUNT(*) FROM commerce.MissingProductRequests WHERE TenantId=@tenant AND MissingProductRequestId=@request AND SearchEventId IS NULL),
 (SELECT COUNT(*) FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND AnalyticsEventId=@legacyEvent AND GuestSessionId IS NULL),
 (SELECT COUNT(*) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND SearchEventId=@legacySearch AND GuestSessionId IS NULL);
""", connection))
            {
                verify.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                verify.Parameters.Add("@expired", System.Data.SqlDbType.UniqueIdentifier).Value = expiredSessionId;
                verify.Parameters.Add("@live", System.Data.SqlDbType.UniqueIdentifier).Value = liveSessionId;
                verify.Parameters.Add("@event", System.Data.SqlDbType.UniqueIdentifier).Value = eventId;
                verify.Parameters.Add("@search", System.Data.SqlDbType.UniqueIdentifier).Value = searchId;
                verify.Parameters.Add("@request", System.Data.SqlDbType.UniqueIdentifier).Value = requestId;
                verify.Parameters.Add("@legacyEvent", System.Data.SqlDbType.UniqueIdentifier).Value = legacyEventId;
                verify.Parameters.Add("@legacySearch", System.Data.SqlDbType.UniqueIdentifier).Value = legacySearchId;
                await using var reader = await verify.ExecuteReaderAsync();
                (await reader.ReadAsync()).Should().BeTrue();
                reader.GetInt32(0).Should().Be(0);
                reader.GetInt32(1).Should().Be(0);
                reader.GetInt32(2).Should().Be(0);
                reader.GetInt32(3).Should().Be(1);
                reader.GetInt32(4).Should().Be(1);
                reader.GetInt32(5).Should().Be(1);
                reader.GetInt32(6).Should().Be(1);
            }
        }
        catch (Exception exception)
        {
            testFailure = exception;
        }
        finally
        {
            // Cleanup is always attempted, including after assertion or SQL failures.
            if (tenantFixturesInserted)
            {
                try
                {
                    await using var cleanup = new SqlConnection(connectionString);
                    await cleanup.OpenAsync();
                    await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(cleanup);
                    await AssertApprovedDatabaseIdentityAsync(cleanup);
                    await using var cleanupTransaction = (SqlTransaction)await cleanup.BeginTransactionAsync();
                    try
                    {
                    await using var removeRequest = new SqlCommand("DELETE commerce.MissingProductRequests WHERE TenantId=@tenant AND MissingProductRequestId IN(@request,@withdrawalRequest);", cleanup, cleanupTransaction);
                    removeRequest.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                    removeRequest.Parameters.Add("@request", System.Data.SqlDbType.UniqueIdentifier).Value = requestId;
                    removeRequest.Parameters.Add("@withdrawalRequest", System.Data.SqlDbType.UniqueIdentifier).Value = withdrawalRequestId;
                    await removeRequest.ExecuteNonQueryAsync();
                    await using var removeSessions = new SqlCommand("DELETE commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId IN(@expired,@live,@consent);", cleanup, cleanupTransaction);
                    removeSessions.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                    removeSessions.Parameters.Add("@expired", System.Data.SqlDbType.UniqueIdentifier).Value = expiredSessionId;
                    removeSessions.Parameters.Add("@live", System.Data.SqlDbType.UniqueIdentifier).Value = liveSessionId;
                    removeSessions.Parameters.Add("@consent", System.Data.SqlDbType.UniqueIdentifier).Value = consentSessionId;
                    await removeSessions.ExecuteNonQueryAsync();
                    await using (var removeLegacy = new SqlCommand("DELETE commerce.AnalyticsEvents WHERE TenantId=@tenant AND AnalyticsEventId=@legacyEvent; DELETE commerce.ProductSearchEvents WHERE TenantId=@tenant AND SearchEventId=@legacySearch;", cleanup, cleanupTransaction))
                    {
                        removeLegacy.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                        removeLegacy.Parameters.Add("@legacyEvent", System.Data.SqlDbType.UniqueIdentifier).Value = legacyEventId;
                        removeLegacy.Parameters.Add("@legacySearch", System.Data.SqlDbType.UniqueIdentifier).Value = legacySearchId;
                        await removeLegacy.ExecuteNonQueryAsync();
                    }
                    if (policyInserted)
                    {
                        await using var removePolicy = new SqlCommand("DELETE commerce.GuestVisitorAnalyticsPolicies WHERE TenantId=@tenant AND PolicyReviewedBy=N'v82-sql-test';", cleanup, cleanupTransaction);
                        removePolicy.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                        await removePolicy.ExecuteNonQueryAsync();
                    }
                    if (otherPolicyInserted)
                    {
                        await using var removePolicy = new SqlCommand("DELETE commerce.GuestVisitorAnalyticsPolicies WHERE TenantId=@tenant AND PolicyReviewedBy=N'v82-sql-test';", cleanup, cleanupTransaction);
                        removePolicy.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = otherTenantId;
                        await removePolicy.ExecuteNonQueryAsync();
                    }
                    await using (var removeTenants = new SqlCommand("DELETE core.Tenants WHERE TenantId IN(@tenant,@otherTenant) AND CreatedBy=N'v82-sql-test';", cleanup, cleanupTransaction))
                    {
                        removeTenants.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                        removeTenants.Parameters.Add("@otherTenant", System.Data.SqlDbType.UniqueIdentifier).Value = otherTenantId;
                        await removeTenants.ExecuteNonQueryAsync();
                    }
                    await cleanupTransaction.CommitAsync();

                    await using var cleanupCheck = new SqlCommand("""
SELECT
 (SELECT COUNT(*) FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId IN(@expired,@live,@consent)),
 (SELECT COUNT(*) FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND AnalyticsEventId IN(@event,@consentEvent,@blockedEvent,@legacyEvent)),
 (SELECT COUNT(*) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND SearchEventId IN(@search,@withdrawalSearch,@legacySearch)),
 (SELECT COUNT(*) FROM commerce.MissingProductRequests WHERE TenantId=@tenant AND MissingProductRequestId IN(@request,@withdrawalRequest)),
 (SELECT COUNT(*) FROM commerce.GuestVisitorAnalyticsPolicies WHERE TenantId IN(@tenant,@otherTenant) AND PolicyReviewedBy=N'v82-sql-test'),
 (SELECT COUNT(*) FROM core.Tenants WHERE TenantId IN(@tenant,@otherTenant) AND CreatedBy=N'v82-sql-test');
""", cleanup);
                    cleanupCheck.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenantId;
                    cleanupCheck.Parameters.Add("@otherTenant", System.Data.SqlDbType.UniqueIdentifier).Value = otherTenantId;
                    cleanupCheck.Parameters.Add("@expired", System.Data.SqlDbType.UniqueIdentifier).Value = expiredSessionId;
                    cleanupCheck.Parameters.Add("@live", System.Data.SqlDbType.UniqueIdentifier).Value = liveSessionId;
                    cleanupCheck.Parameters.Add("@consent", System.Data.SqlDbType.UniqueIdentifier).Value = consentSessionId;
                    cleanupCheck.Parameters.Add("@event", System.Data.SqlDbType.UniqueIdentifier).Value = eventId;
                    cleanupCheck.Parameters.Add("@consentEvent", System.Data.SqlDbType.UniqueIdentifier).Value = consentEventId;
                    cleanupCheck.Parameters.Add("@blockedEvent", System.Data.SqlDbType.UniqueIdentifier).Value = blockedEventId;
                    cleanupCheck.Parameters.Add("@legacyEvent", System.Data.SqlDbType.UniqueIdentifier).Value = legacyEventId;
                    cleanupCheck.Parameters.Add("@search", System.Data.SqlDbType.UniqueIdentifier).Value = searchId;
                    cleanupCheck.Parameters.Add("@withdrawalSearch", System.Data.SqlDbType.UniqueIdentifier).Value = withdrawalSearchId;
                    cleanupCheck.Parameters.Add("@legacySearch", System.Data.SqlDbType.UniqueIdentifier).Value = legacySearchId;
                    cleanupCheck.Parameters.Add("@request", System.Data.SqlDbType.UniqueIdentifier).Value = requestId;
                    cleanupCheck.Parameters.Add("@withdrawalRequest", System.Data.SqlDbType.UniqueIdentifier).Value = withdrawalRequestId;
                    await using var cleanupReader = await cleanupCheck.ExecuteReaderAsync();
                    if (!await cleanupReader.ReadAsync() || Enumerable.Range(0, cleanupReader.FieldCount).Any(i => cleanupReader.GetInt32(i) != 0))
                        testFailure = testFailure is null ? new InvalidOperationException("SQL integration cleanup left fixture rows behind.") : new AggregateException(testFailure, new InvalidOperationException("SQL integration cleanup left fixture rows behind."));
                    }
                    catch (Exception cleanupException)
                    {
                        try { await cleanupTransaction.RollbackAsync(CancellationToken.None); }
                        catch (Exception rollbackException) { cleanupException = new AggregateException(cleanupException, rollbackException); }
                        testFailure = testFailure is null ? cleanupException : new AggregateException(testFailure, cleanupException);
                    }
                }
                catch (Exception cleanupException)
                {
                    testFailure = testFailure is null ? cleanupException : new AggregateException(testFailure, cleanupException);
                }
            }
        }
        if (testFailure is not null) ExceptionDispatchInfo.Capture(testFailure).Throw();
    }

    private static async Task AssertConstraintRejectsAsync(SqlConnection connection, string sql, Guid tenant, Guid session)
    {
        await using var command = new SqlCommand($"SET XACT_ABORT OFF; DECLARE @rejected bit=0; BEGIN TRY {sql} END TRY BEGIN CATCH IF ERROR_NUMBER() NOT IN(547,2601,2627) THROW; SET @rejected=1; END CATCH; SELECT @rejected;", connection);
        command.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenant;
        command.Parameters.Add("@other", System.Data.SqlDbType.UniqueIdentifier).Value = tenant;
        command.Parameters.Add("@session", System.Data.SqlDbType.UniqueIdentifier).Value = session;
        Convert.ToBoolean(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture).Should().BeTrue();
    }

    private static async Task<int> EventCountAsync(SqlConnection connection, Guid tenant, Guid eventId)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND ClientEventId=@event;", connection);
        command.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenant;
        command.Parameters.Add("@event", System.Data.SqlDbType.UniqueIdentifier).Value = eventId;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> GuestSessionEventCountAsync(SqlConnection connection, Guid tenant, Guid sessionId)
    {
        await using var command = new SqlCommand("SELECT COUNT(*) FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND GuestSessionId=@session;", connection);
        command.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenant;
        command.Parameters.Add("@session", System.Data.SqlDbType.UniqueIdentifier).Value = sessionId;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task VerifyConsentAndIdempotencyAsync(SqlConnection connection, Guid tenant, Guid session, Guid eventId)
    {
        await using var command = new SqlCommand("""
SELECT s.DeviceCategory,s.EngagedSeconds,
 (SELECT COUNT(*) FROM commerce.AnalyticsEvents e WHERE e.TenantId=s.TenantId AND e.GuestSessionId=s.SessionId AND e.ClientEventId=@event),
 (SELECT COUNT(*) FROM commerce.AnalyticsEvents e WHERE e.TenantId=s.TenantId AND e.GuestSessionId=s.SessionId AND e.ClientEventId=@event AND e.CustomerId IS NULL AND e.VisitorSessionId IS NULL AND e.MetadataJson IS NULL)
FROM commerce.GuestVisitorAnalyticsSessions s WHERE s.TenantId=@tenant AND s.SessionId=@session;
""", connection);
        command.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenant;
        command.Parameters.Add("@session", System.Data.SqlDbType.UniqueIdentifier).Value = session;
        command.Parameters.Add("@event", System.Data.SqlDbType.UniqueIdentifier).Value = eventId;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("consent creates a tenant-scoped session");
        reader.GetString(0).Should().Be("TABLET");
        reader.GetInt32(1).Should().Be(23);
        reader.GetInt32(2).Should().Be(1, "duplicate client event IDs are idempotent");
        reader.GetInt32(3).Should().Be(1, "guest events carry no customer ID, visitor ID, or JSON identity payload");
    }

    private static async Task VerifyWithdrawalAsync(SqlConnection connection, Guid tenant, Guid session, Guid eventId, Guid searchId, Guid requestId)
    {
        await using var command = new SqlCommand("""
SELECT
 (SELECT COUNT(*) FROM commerce.GuestVisitorAnalyticsSessions WHERE TenantId=@tenant AND SessionId=@session),
 (SELECT COUNT(*) FROM commerce.AnalyticsEvents WHERE TenantId=@tenant AND ClientEventId=@event),
 (SELECT COUNT(*) FROM commerce.ProductSearchEvents WHERE TenantId=@tenant AND SearchEventId=@search),
 (SELECT COUNT(*) FROM commerce.MissingProductRequests WHERE TenantId=@tenant AND MissingProductRequestId=@request AND SearchEventId IS NULL);
""", connection);
        command.Parameters.Add("@tenant", System.Data.SqlDbType.UniqueIdentifier).Value = tenant;
        command.Parameters.Add("@session", System.Data.SqlDbType.UniqueIdentifier).Value = session;
        command.Parameters.Add("@event", System.Data.SqlDbType.UniqueIdentifier).Value = eventId;
        command.Parameters.Add("@search", System.Data.SqlDbType.UniqueIdentifier).Value = searchId;
        command.Parameters.Add("@request", System.Data.SqlDbType.UniqueIdentifier).Value = requestId;
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(0);
        reader.GetInt32(1).Should().Be(0);
        reader.GetInt32(2).Should().Be(0);
        reader.GetInt32(3).Should().Be(1, "the customer-demand request is detached before its guest search row is cascaded");
    }

    private sealed class EnabledFeatures : IFeatureService
    {
        public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default) => Task.FromResult(featureKey == FeatureKeys.GuestVisitorAnalytics);
        public Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId, IReadOnlyCollection<TenantFeatureUpdate> updates, string? changedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenantFeatureConfiguration> UpdateTenantDisplayNameAsync(Guid tenantId, string? displayName, string? changedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void InvalidateTenant(Guid tenantId) { }
        public void InvalidateAll() { }
    }

    private sealed class TestCurrentUser(Guid tenantId, bool owner) : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid? TenantId { get; } = tenantId;
        public string? Username => "v82-sql-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles { get; } = owner ? ["ApplicationOwner"] : [];
        public IReadOnlyCollection<string> Permissions => [];
    }

    private sealed class EmptyStorefront : IStorefrontService
    {
        public Task<StorefrontStoreDto?> GetStoreAsync(string storeKey, CancellationToken token) => Task.FromResult<StorefrontStoreDto?>(null);
        public Task<IReadOnlyCollection<StorefrontCategoryDto>?> GetCategoriesAsync(string storeKey, CancellationToken token) => Task.FromResult<IReadOnlyCollection<StorefrontCategoryDto>?>([]);
        public Task<StorefrontProductPageDto?> GetProductsAsync(string storeKey, int page, int pageSize, CancellationToken token) => Task.FromResult<StorefrontProductPageDto?>(null);
        public Task<StorefrontProductPageDto?> GetProductsAsync(string storeKey, int page, int pageSize, Guid? categoryId, CancellationToken token) => Task.FromResult<StorefrontProductPageDto?>(null);
        public Task<StorefrontProductDto?> GetProductAsync(string storeKey, Guid productId, CancellationToken token) => Task.FromResult<StorefrontProductDto?>(null);
        public Task<StorefrontOfferDto?> GetOfferAsync(string storeKey, Guid offerId, CancellationToken token) => Task.FromResult<StorefrontOfferDto?>(null);
        public Task<StorefrontImage?> GetProductImageAsync(string storeKey, Guid productId, Guid? imageId, bool thumbnail, CancellationToken token) => Task.FromResult<StorefrontImage?>(null);
        public Task<StorefrontImage?> GetPresentationImageAsync(string storeKey, string resource, Guid? categoryId, CancellationToken token) => Task.FromResult<StorefrontImage?>(null);
        public Task<StorefrontImage?> GetPwaIconAsync(string storeKey, int size, CancellationToken token) => Task.FromResult<StorefrontImage?>(null);
    }

    private static async Task AssertApprovedDatabaseIdentityAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME();", connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || !string.Equals(reader.GetString(0), IntegrationDatabaseTarget.Server, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(1), IntegrationDatabaseTarget.Database, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQL integration test connected to a database other than the specifically approved disposable target.");
    }

    private static async Task AssertV82CanonicalMigrationAsync(SqlConnection connection, string migrationPath, string validatorPath)
    {
        await AssertV82PrerequisitesAsync(connection);
        await using var countCommand = new SqlCommand("SELECT COUNT(*) FROM deployment.DatabaseMigrationHistory WHERE MigrationVersion=82;", connection);
        if (Convert.ToInt32(await countCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("V82 must be applied and recorded by the canonical migration runner before its SQL integration test.");

        await using var ledgerCommand = new SqlCommand("SELECT ScriptHash,ValidatorHash FROM deployment.DatabaseMigrationHistory WHERE MigrationVersion=82;", connection);
        await using var reader = await ledgerCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Canonical V82 migration ledger record is missing.");
        var scriptHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(migrationPath)));
        var validatorHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(validatorPath)));
        if (!string.Equals(reader.GetString(0), scriptHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(1), validatorHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("V82 migration or validator hash does not match the canonical ledger.");
        if (await reader.ReadAsync()) throw new InvalidOperationException("Multiple canonical V82 migration ledger records exist.");
        await reader.DisposeAsync();
        await ExecuteScriptAsync(connection, validatorPath);
    }

    private static async Task AssertV82PrerequisitesAsync(SqlConnection connection)
    {
        const string sql = """
SELECT Requirement FROM (VALUES
 (N'core.Tenants',CASE WHEN OBJECT_ID(N'core.Tenants',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'core.Features',CASE WHEN OBJECT_ID(N'core.Features',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'core.Plans',CASE WHEN OBJECT_ID(N'core.Plans',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'core.PlanFeatures',CASE WHEN OBJECT_ID(N'core.PlanFeatures',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'core.TenantFeatures',CASE WHEN OBJECT_ID(N'core.TenantFeatures',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'commerce.AnalyticsEvents',CASE WHEN OBJECT_ID(N'commerce.AnalyticsEvents',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'commerce.ProductSearchEvents',CASE WHEN OBJECT_ID(N'commerce.ProductSearchEvents',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'commerce.MissingProductRequests',CASE WHEN OBJECT_ID(N'commerce.MissingProductRequests',N'U') IS NULL THEN 0 ELSE 1 END),
 (N'commerce.AnalyticsEvents.CreatedOn',CASE WHEN COL_LENGTH(N'commerce.AnalyticsEvents',N'CreatedOn') IS NULL THEN 0 ELSE 1 END)
) requirements(Requirement,IsPresent) WHERE IsPresent=0;
""";
        var missing = new List<string>();
        await using (var command = new SqlCommand(sql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) missing.Add(reader.GetString(0));
        }

        if (missing.Count == 0)
        {
            await using var parent = new SqlCommand("SELECT COUNT(*) FROM core.Features WHERE FeatureKey=N'ANALYTICS';", connection);
            if (Convert.ToInt32(await parent.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 0)
                missing.Add("Parent ANALYTICS feature");
        }
        if (missing.Count > 0)
            throw new InvalidOperationException($"The approved integration database lacks canonical V82 prerequisites ({string.Join(", ", missing)}); no migration or fixture writes were attempted.");
    }

    private static async Task ExecuteScriptAsync(SqlConnection connection, string path)
    {
        var script = await File.ReadAllTextAsync(path);
        foreach (var batch in Regex.Split(script, @"(?im)^\s*GO\s*(?:--.*)?$"))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "database/migrations/V82-GuestVisitorAnalytics.sql")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository migration directory was not found.");
    }
}
