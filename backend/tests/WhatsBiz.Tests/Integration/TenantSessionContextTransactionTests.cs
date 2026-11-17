using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class TenantSessionContextTransactionTests
{
    [WhatsBiz.Tests.Integration.SqlIntegrationTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TenantContextCommandEnlistsInTheCurrentEfTransaction(bool useTransaction)
    {
        // Uses the existing disposable-database guard. No application rows are written.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SqlIntegrationDatabase.ConnectionString).Options;
        var tenantId = Guid.NewGuid();
        await using var db = new ApplicationDbContext(options, new User(tenantId));
        await db.Database.OpenConnectionAsync();
        var connection = (SqlConnection)db.Database.GetDbConnection();
        try
        {
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
            await using (var transaction = useTransaction ? await db.Database.BeginTransactionAsync() : null)
            {
                // This failed before the fix even with no pending tracked changes:
                // SaveChangesAsync sets SESSION_CONTEXT before calling EF's implementation.
                (await db.SaveChangesAsync()).Should().Be(0);
                await using var command = new SqlCommand(
                    "SELECT TRY_CONVERT(uniqueidentifier, SESSION_CONTEXT(N'TenantId'))", connection,
                    (SqlTransaction?)transaction?.GetDbTransaction());
                (await command.ExecuteScalarAsync()).Should().Be(tenantId);
                if (transaction is not null) await transaction.RollbackAsync();
            }
        }
        finally
        {
            // SESSION_CONTEXT is connection-scoped, not rolled back with the transaction.
            await using var clear = new SqlCommand(
                "EXEC sys.sp_set_session_context @key=N'TenantId', @value=NULL", connection);
            await clear.ExecuteNonQueryAsync();
            await db.Database.CloseConnectionAsync();
        }
    }

    private sealed class User(Guid tenantId) : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid? TenantId => tenantId;
        public string? Username => null;
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
