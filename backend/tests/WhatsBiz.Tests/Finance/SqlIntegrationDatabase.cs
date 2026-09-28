using Microsoft.Data.SqlClient;

namespace WhatsBiz.Tests.Integration;

// Mutating SQL tests must opt into a disposable database explicitly.
internal static class SqlIntegrationDatabase
{
    private const string EnvironmentVariable = "ConnectionStrings__IntegrationTests";

    internal static string ConnectionString => Validate(Environment.GetEnvironmentVariable(EnvironmentVariable));

    internal static string Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Set {EnvironmentVariable} to an explicit disposable integration database before running SQL tests.");

        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(value); }
        catch (ArgumentException exception)
        { throw new InvalidOperationException($"{EnvironmentVariable} is not a valid SQL connection string.", exception); }

        var database = builder.InitialCatalog.Trim();
        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            database.Equals("WhatsBizERP", StringComparison.OrdinalIgnoreCase) ||
            database.Equals("WhatsBizERP_QA", StringComparison.OrdinalIgnoreCase) ||
            database.Equals("WhatsBizERP_PROD", StringComparison.OrdinalIgnoreCase) ||
            !database.Contains("Test", StringComparison.OrdinalIgnoreCase) &&
            !database.Contains("Integration", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQL integration tests require an explicitly named disposable Test/Integration database.");

        return builder.ConnectionString;
    }

    internal static async Task VerifyOpenedDatabaseAsync(SqlConnection connection, CancellationToken token = default)
    {
        var expected = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        await using var command = new SqlCommand("SELECT DB_NAME()", connection);
        var actual = (string?)await command.ExecuteScalarAsync(token);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQL integration test connection resolved to an unexpected database.");
    }
}
