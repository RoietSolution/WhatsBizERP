using Xunit;

namespace WhatsBiz.Tests.Integration;

/// <summary>
/// Conditional discovery-time skip for SQL tests. The explicit reason is included in test output,
/// while configured and authorized tests still execute normally and fail on real SQL errors.
/// </summary>
public sealed class SqlIntegrationFactAttribute : FactAttribute
{
    public SqlIntegrationFactAttribute() => Skip = SqlIntegrationDatabase.GetUnavailableReason();
}

public sealed class SqlIntegrationTheoryAttribute : TheoryAttribute
{
    public SqlIntegrationTheoryAttribute() => Skip = SqlIntegrationDatabase.GetUnavailableReason();
}

public sealed class MarketplaceDevSqlFactAttribute : FactAttribute
{
    private const string AuthorizationVariable = "WHATS_BIZ_DEV_MARKETPLACE_SQL_AUTHORIZATION";
    private const string AuthorizationValue = "I_AUTHORIZE_DEV_MARKETPLACE_ROLLBACK_FIXTURES";

    public MarketplaceDevSqlFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(AuthorizationVariable), AuthorizationValue, StringComparison.Ordinal))
            Skip = $"DEV marketplace SQL test NOT RUN: explicit operator authorization is required in {AuthorizationVariable}.";
    }
}

public sealed class MarketplaceV91ServiceSqlFactAttribute : FactAttribute
{
    private const string AuthorizationVariable = "WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_SQL_AUTHORIZATION";
    private const string AuthorizationValue = "I_AUTHORIZE_SYNTHETIC_V91_MARKETPLACE_TEST_DATABASE";
    private const string ConnectionVariable = "ConnectionStrings__GuturGoMarketplaceTests";

    public MarketplaceV91ServiceSqlFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(AuthorizationVariable), AuthorizationValue, StringComparison.Ordinal))
        {
            Skip = $"Committed marketplace SQL test NOT RUN: explicit operator authorization is required in {AuthorizationVariable}.";
            return;
        }

        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
            Skip = $"Committed marketplace SQL test NOT RUN: {ConnectionVariable} is not configured.";
        else if (connection.TrimStart().StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
            Skip = $"Committed marketplace SQL test NOT RUN: {ConnectionVariable} contains PowerShell assignment syntax instead of a connection-string value.";
        else
        {
            try { _ = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection); }
            catch (ArgumentException)
            {
                Skip = $"Committed marketplace SQL test NOT RUN: {ConnectionVariable} is malformed.";
            }
        }
    }
}
