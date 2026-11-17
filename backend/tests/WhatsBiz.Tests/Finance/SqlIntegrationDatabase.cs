using Microsoft.Data.SqlClient;

namespace WhatsBiz.Tests.Integration;

// Mutating SQL tests must opt into a disposable database explicitly.
internal static class SqlIntegrationDatabase
{
    private const string EnvironmentVariable = "ConnectionStrings__IntegrationTests";
    private const string AuthorizationVariable = "WHATS_BIZ_SQL_INTEGRATION_TEST_AUTHORIZATION";
    private const string AuthorizationValue = "I_AUTHORIZE_DISPOSABLE_SQL_INTEGRATION_TESTS";
    private const string ExpectedServer = "DESKTOP-DQ0868S";
    private const string ExpectedDatabase = "WhatsBizERP_GuturGoMarketplaceTest_V91";

    internal static string ConnectionString
    {
        get
        {
            return Validate(Environment.GetEnvironmentVariable(EnvironmentVariable));
        }
    }

    internal static string? GetUnavailableReason()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(AuthorizationVariable), AuthorizationValue, StringComparison.Ordinal))
            return $"SQL-dependent test NOT RUN: explicit authorization is required in {AuthorizationVariable}.";
        try
        {
            _ = Validate(Environment.GetEnvironmentVariable(EnvironmentVariable));
            return null;
        }
        catch (InvalidOperationException error)
        {
            return $"SQL-dependent test NOT RUN: {error.Message}";
        }
    }

    internal static string Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Set {EnvironmentVariable} to an explicit disposable integration database before running SQL tests.");
        if (value.TrimStart().StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{EnvironmentVariable} must contain only the SQL connection string value; do not include PowerShell $env: assignment syntax.");

        SqlConnectionStringBuilder builder;
        try { builder = new SqlConnectionStringBuilder(value); }
        catch (ArgumentException exception)
        { throw new InvalidOperationException($"{EnvironmentVariable} is not a valid SQL connection string.", exception); }

        var database = builder.InitialCatalog.Trim();
        if (!string.Equals(builder.DataSource, ExpectedServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(database, ExpectedDatabase, StringComparison.OrdinalIgnoreCase) ||
            !builder.IntegratedSecurity || builder.Pooling ||
            !string.IsNullOrWhiteSpace(builder.UserID) || !string.IsNullOrWhiteSpace(builder.Password) ||
            !string.IsNullOrWhiteSpace(builder.AttachDBFilename) || builder.UserInstance)
            throw new InvalidOperationException($"SQL integration tests require Windows Integrated Security, Pooling=False and the exact approved disposable target {ExpectedServer}/{ExpectedDatabase}.");

        return builder.ConnectionString;
    }

    internal static async Task VerifyOpenedDatabaseAsync(SqlConnection connection, CancellationToken token = default)
    {
        await using var command = new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME()", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) ||
            !string.Equals(reader.GetString(0), ExpectedServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(1), ExpectedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQL integration test connection resolved to an unexpected server or database.");
    }
}
