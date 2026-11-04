using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class OrderIntakeSalesOrderSqlIntegrationTests
{
    [Fact]
    public async Task V79V80TenantScopedQuoteAndSalesOrderSchemaRollsBackAllFixtures()
    {
        await using var connection = new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();
        await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        (connection.Database.Contains("Test", StringComparison.OrdinalIgnoreCase) || connection.Database.Contains("Integration", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("SQL scenarios must run only in a disposable integration database");

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database/migrations/V80-OrderIntakeQuotesAndSalesOrders.sql"))) root = root.Parent;
        root.Should().NotBeNull();
        var tenantId = Guid.NewGuid(); var otherTenantId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        var quoteId = Guid.NewGuid(); var orderId = Guid.NewGuid();
        Exception? fixtureError = null; Exception? cleanupError = null;
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                async Task Execute(string sql)
                {
                    foreach (var batch in Regex.Split(sql, @"(?im)^\s*GO\s*(?:--.*)?$"))
                        if (!string.IsNullOrWhiteSpace(batch)) { await using var command = new SqlCommand(batch, connection, transaction); await command.ExecuteNonQueryAsync(); }
                }
                async Task Migration(string file) => await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations", file)));
                await Migration("V79-MultiChannelOrderIntake.sql");
                await Migration("V80-OrderIntakeQuotesAndSalesOrders.sql");
                await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations/validators/V80-OrderIntakeQuotesAndSalesOrders.validate.sql")));
                await Migration("V81-OrderIntakeDeliveryAddressSnapshots.sql");
                await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "database/migrations/validators/V81-OrderIntakeDeliveryAddressSnapshots.validate.sql")));

                await using (var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options))
                {
                    await db.Database.UseTransactionAsync(transaction);
                    db.Tenants.AddRange(new Tenant { TenantId = tenantId, TenantKey = "intake-test-" + tenantId.ToString("N"), Name = "Order Intake fixture", IsActive = true },
                        new Tenant { TenantId = otherTenantId, TenantKey = "intake-test-" + otherTenantId.ToString("N"), Name = "Order Intake other fixture", IsActive = true });
                    await db.SaveChangesAsync();
                }
                const string addressJson = "{\"recipientName\":\"Fixture customer\",\"mobile\":\"9876543210\",\"houseStreet\":\"12 Main Road\",\"locality\":\"Civil Lines\",\"city\":\"Kanpur\",\"state\":\"Uttar Pradesh\",\"pincode\":\"208001\"}";
                await using (var request = new SqlCommand("INSERT commerce.OrderIntakeRequests(TenantId,OrderIntakeRequestId,RequestReference,SourceChannel,IntakeStatus,CustomerName,OriginalRequest,FulfilmentPreference,DeliveryAddressDraftJson,CustomerConfirmedAtUtc,ConfirmedQuoteRevision) VALUES(@tenant,@request,N'OI-SQL-TEST','PHONE_CALL','CONVERTED',N'Fixture customer',N'one item','DELIVERY',@address,SYSUTCDATETIME(),1);", connection, transaction))
                { request.Parameters.AddWithValue("@tenant", tenantId); request.Parameters.AddWithValue("@request", requestId); request.Parameters.AddWithValue("@address", addressJson); await request.ExecuteNonQueryAsync(); }
                await using (var quote = new SqlCommand("INSERT commerce.OrderIntakeQuotes(TenantId,QuoteId,OrderIntakeRequestId,QuoteRevision,QuoteStatus,FulfilmentPreference,DeliveryAddressSnapshotJson,PricingSnapshotJson,MerchandiseSubtotal,MerchandiseAmount,MerchandiseTaxAmount,IncludedTaxAmount,AdditionalTaxAmount,ProductSavings,PromotionDiscount,DeliveryCharge,PayableAmount,ExpiresAtUtc) VALUES(@tenant,@quote,@request,1,'CURRENT','DELIVERY',@address,N'{}',100,100,0,0,0,0,0,0,100,DATEADD(hour,1,SYSUTCDATETIME()));", connection, transaction))
                { quote.Parameters.AddWithValue("@tenant", tenantId); quote.Parameters.AddWithValue("@quote", quoteId); quote.Parameters.AddWithValue("@request", requestId); quote.Parameters.AddWithValue("@address", addressJson); await quote.ExecuteNonQueryAsync(); }
                await using (var quoteItem = new SqlCommand("INSERT commerce.OrderIntakeQuoteItems(TenantId,QuoteId,QuoteItemId,ProductId,ProductCode,ProductName,RequestedText,IsSubstitution,UnitName,UnitShortName,Quantity,UnitPrice,TaxPercentage,CompareAtPrice,GstPricingType) VALUES(@tenant,@quote,NEWID(),NEWID(),N'FIXTURE',N'Fixture product',N'Fixture request',0,N'Each',N'ea',1,100,0,100,'EXCLUSIVE');", connection, transaction))
                { quoteItem.Parameters.AddWithValue("@tenant", tenantId); quoteItem.Parameters.AddWithValue("@quote", quoteId); await quoteItem.ExecuteNonQueryAsync(); }
                await using (var order = new SqlCommand("INSERT commerce.IntakeSalesOrders(TenantId,SalesOrderId,OrderNumber,OrderIntakeRequestId,QuoteId,QuoteRevision,OrderStatus,FulfilmentPreference,DeliveryAddressSnapshotJson,PricingSnapshotJson,PayableAmount,ConfirmedAtUtc) SELECT @tenant,@order,N'SOI-SQL-TEST',@request,@quote,1,'CONFIRMED','DELIVERY',q.DeliveryAddressSnapshotJson,N'{}',100,SYSUTCDATETIME() FROM commerce.OrderIntakeQuotes q WHERE q.TenantId=@tenant AND q.QuoteId=@quote; UPDATE commerce.OrderIntakeRequests SET ConvertedOrderId=@order WHERE TenantId=@tenant AND OrderIntakeRequestId=@request;", connection, transaction))
                { order.Parameters.AddWithValue("@tenant", tenantId); order.Parameters.AddWithValue("@order", orderId); order.Parameters.AddWithValue("@request", requestId); order.Parameters.AddWithValue("@quote", quoteId); await order.ExecuteNonQueryAsync(); }
                await using (var addressSnapshot = new SqlCommand("SELECT CASE WHEN q.DeliveryAddressSnapshotJson=o.DeliveryAddressSnapshotJson THEN 1 ELSE 0 END FROM commerce.OrderIntakeQuotes q JOIN commerce.IntakeSalesOrders o ON o.TenantId=q.TenantId AND o.QuoteId=q.QuoteId WHERE q.TenantId=@tenant AND q.QuoteId=@quote;", connection, transaction))
                { addressSnapshot.Parameters.AddWithValue("@tenant", tenantId); addressSnapshot.Parameters.AddWithValue("@quote", quoteId); Convert.ToInt32(await addressSnapshot.ExecuteScalarAsync(), CultureInfo.InvariantCulture).Should().Be(1); }
                await using (var orderItem = new SqlCommand("INSERT commerce.IntakeSalesOrderItems(TenantId,SalesOrderId,SalesOrderItemId,ProductId,ProductCode,ProductName,Quantity,UnitPrice,TaxPercentage,CompareAtPrice,GstPricingType) SELECT TenantId,@order,NEWID(),ProductId,ProductCode,ProductName,Quantity,UnitPrice,TaxPercentage,CompareAtPrice,GstPricingType FROM commerce.OrderIntakeQuoteItems WHERE TenantId=@tenant AND QuoteId=@quote;", connection, transaction))
                { orderItem.Parameters.AddWithValue("@tenant", tenantId); orderItem.Parameters.AddWithValue("@order", orderId); orderItem.Parameters.AddWithValue("@quote", quoteId); await orderItem.ExecuteNonQueryAsync(); }

                var crossTenant = async () =>
                {
                    await using var command = new SqlCommand("INSERT commerce.IntakeSalesOrders(TenantId,SalesOrderId,OrderNumber,OrderIntakeRequestId,QuoteId,QuoteRevision,OrderStatus,FulfilmentPreference,PricingSnapshotJson,PayableAmount,ConfirmedAtUtc) VALUES(@tenant,NEWID(),N'SOI-CROSS-TEST',@request,@quote,1,'CONFIRMED','PICKUP',N'{}',100,SYSUTCDATETIME());", connection, transaction);
                    command.Parameters.AddWithValue("@tenant", otherTenantId); command.Parameters.AddWithValue("@request", requestId); command.Parameters.AddWithValue("@quote", quoteId);
                    await command.ExecuteNonQueryAsync();
                };
                await crossTenant.Should().ThrowAsync<SqlException>();
                var duplicateRequest = async () =>
                {
                    await using var command = new SqlCommand("INSERT commerce.IntakeSalesOrders(TenantId,SalesOrderId,OrderNumber,OrderIntakeRequestId,QuoteId,QuoteRevision,OrderStatus,FulfilmentPreference,PricingSnapshotJson,PayableAmount,ConfirmedAtUtc) VALUES(@tenant,NEWID(),N'SOI-DUP-TEST',@request,@quote,1,'CONFIRMED','PICKUP',N'{}',100,SYSUTCDATETIME());", connection, transaction);
                    command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@request", requestId); command.Parameters.AddWithValue("@quote", quoteId);
                    await command.ExecuteNonQueryAsync();
                };
                await duplicateRequest.Should().ThrowAsync<SqlException>();
                await using var noInvoice = new SqlCommand("SELECT COUNT(1) FROM sales.SalesInvoices WHERE TenantId=@tenant AND InvoiceNumber=N'SOI-SQL-TEST';", connection, transaction);
                noInvoice.Parameters.AddWithValue("@tenant", tenantId);
                Convert.ToInt32(await noInvoice.ExecuteScalarAsync(), CultureInfo.InvariantCulture).Should().Be(0);
            }
            catch (Exception exception) { fixtureError = exception; }
            finally { try { await transaction.RollbackAsync(); } catch (Exception exception) { cleanupError = exception; } }
        }
        try
        {
            await using var clean = new SqlCommand("SELECT (SELECT COUNT(1) FROM core.Tenants WHERE TenantId IN(@tenant,@other))+(CASE WHEN OBJECT_ID(N'commerce.OrderIntakeRequests',N'U') IS NULL THEN 0 ELSE (SELECT COUNT(1) FROM commerce.OrderIntakeRequests WHERE TenantId=@tenant AND OrderIntakeRequestId=@request) END)+(CASE WHEN OBJECT_ID(N'commerce.OrderIntakeQuotes',N'U') IS NULL THEN 0 ELSE (SELECT COUNT(1) FROM commerce.OrderIntakeQuotes WHERE TenantId=@tenant AND QuoteId=@quote) END)+(CASE WHEN OBJECT_ID(N'commerce.IntakeSalesOrders',N'U') IS NULL THEN 0 ELSE (SELECT COUNT(1) FROM commerce.IntakeSalesOrders WHERE TenantId=@tenant AND SalesOrderId=@order) END);", connection);
            clean.Parameters.AddWithValue("@tenant", tenantId); clean.Parameters.AddWithValue("@other", otherTenantId); clean.Parameters.AddWithValue("@request", requestId); clean.Parameters.AddWithValue("@quote", quoteId); clean.Parameters.AddWithValue("@order", orderId);
            Convert.ToInt32(await clean.ExecuteScalarAsync(), CultureInfo.InvariantCulture).Should().Be(0, "fixtures and transactional schema changes must roll back");
        }
        catch (Exception exception) { cleanupError = exception; }
        if (fixtureError is not null && cleanupError is not null) throw new AggregateException("SQL fixture failed and rollback verification failed.", fixtureError, cleanupError);
        if (cleanupError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupError).Throw();
        if (fixtureError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fixtureError).Throw();
    }
}
