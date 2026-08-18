using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;

namespace WhatsBiz.Infrastructure.Persistence;

internal static class IdempotencyKeyReader
{
    public static Guid? Read(IHttpContextAccessor accessor)
    {
        var value = accessor.HttpContext?.Request.Headers["Idempotency-Key"].FirstOrDefault();
        return Guid.TryParse(value, out var key) ? key : null;
    }
}

public sealed class SqlIdempotencyExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString;

    public SqlIdempotencyExecutor(IConfiguration configuration)
    {
        connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is missing or empty. " +
                "Configure ConnectionStrings:DefaultConnection before using database operations.");
    }

    public async Task<T> Execute<T>(Guid? key, string operation, object request, string? user,
        Func<SqlConnection, SqlTransaction, CancellationToken, Task<T>> action, CancellationToken token)
    {
        if (!key.HasValue || key == Guid.Empty)
            throw new BusinessRuleException("An idempotency key is required.");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions)));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await AcquireLock(connection, transaction, key.Value, token);
            var existing = await Find(connection, transaction, key.Value, token);
            if (existing is not null)
            {
                if (existing.Value.Operation != operation
                    || !CryptographicOperations.FixedTimeEquals(existing.Value.Hash, hash)
                    || !string.Equals(existing.Value.User, user, StringComparison.OrdinalIgnoreCase))
                    throw new BusinessRuleException("The idempotency key was already used for a different request.");
                if (existing.Value.Status != "COMPLETED" || string.IsNullOrWhiteSpace(existing.Value.Response))
                    throw new BusinessRuleException("The idempotent request is still processing.");
                var replay = JsonSerializer.Deserialize<T>(existing.Value.Response, JsonOptions)
                    ?? throw new InvalidOperationException("Stored idempotency response is invalid.");
                await transaction.CommitAsync(token);
                return replay;
            }

            await Insert(connection, transaction, key.Value, operation, hash, user, token);
            var result = await action(connection, transaction, token);
            await Complete(connection, transaction, key.Value, JsonSerializer.Serialize(result, JsonOptions), token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task AcquireLock(SqlConnection c, SqlTransaction tx, Guid key, CancellationToken token)
    {
        await using var q = c.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=30000;SELECT @r;";
        q.Parameters.AddWithValue("@resource", $"KhataDhari:Idempotency:{key:D}");
        if (Convert.ToInt32(await q.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) < 0)
            throw new BusinessRuleException("The idempotent request could not acquire its database lock.");
    }

    private static async Task<(string Operation, byte[] Hash, string? User, string Status, string? Response)?> Find(
        SqlConnection c, SqlTransaction tx, Guid key, CancellationToken token)
    {
        await using var q = c.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "SELECT OperationType,RequestHash,RequestedBy,Status,ResponseJson FROM core.IdempotencyRequests WITH(UPDLOCK,HOLDLOCK) WHERE IdempotencyKey=@key;";
        q.Parameters.AddWithValue("@key", key);
        await using var r = await q.ExecuteReaderAsync(token);
        if (!await r.ReadAsync(token)) return null;
        return (r.GetString(0), (byte[])r[1], r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4));
    }

    private static async Task Insert(SqlConnection c, SqlTransaction tx, Guid key, string operation, byte[] hash, string? user, CancellationToken token)
    {
        await using var q = c.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "INSERT core.IdempotencyRequests(IdempotencyKey,OperationType,RequestHash,RequestedBy,Status)VALUES(@key,@operation,@hash,@user,N'PROCESSING');";
        q.Parameters.AddWithValue("@key", key);
        q.Parameters.AddWithValue("@operation", operation);
        q.Parameters.Add("@hash", SqlDbType.Binary, 32).Value = hash;
        q.Parameters.AddWithValue("@user", user ?? (object)DBNull.Value);
        await q.ExecuteNonQueryAsync(token);
    }

    private static async Task Complete(SqlConnection c, SqlTransaction tx, Guid key, string response, CancellationToken token)
    {
        await using var q = c.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "UPDATE core.IdempotencyRequests SET Status=N'COMPLETED',ResponseJson=@response,CompletedOn=SYSUTCDATETIME() WHERE IdempotencyKey=@key AND Status=N'PROCESSING';IF @@ROWCOUNT<>1 THROW 51000,'Idempotency completion failed.',1;";
        q.Parameters.AddWithValue("@key", key);
        q.Parameters.AddWithValue("@response", response);
        await q.ExecuteNonQueryAsync(token);
    }
}
