using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace WhatsBiz.Tests.Integration;

internal static class IntegrationDatabaseTarget
{
    private sealed record TargetConfiguration(string Server, string Database, string[] ProtectedDatabases);

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<TargetConfiguration> Configuration = new(Load);

    internal static string Server => Configuration.Value.Server;
    internal static string Database => Configuration.Value.Database;

    internal static void AssertApproved(SqlConnectionStringBuilder builder)
    {
        var target = Configuration.Value;
        if (!string.Equals(builder.DataSource, target.Server, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(builder.InitialCatalog, target.Database, StringComparison.OrdinalIgnoreCase) ||
            target.ProtectedDatabases.Contains(builder.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQL integration test target does not match the centrally approved disposable database.");
    }

    private static TargetConfiguration Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "integration-database.config.json");
        using var stream = File.OpenRead(path);
        var target = JsonSerializer.Deserialize<TargetConfiguration>(stream, SerializerOptions);
        if (target is null || string.IsNullOrWhiteSpace(target.Server) || string.IsNullOrWhiteSpace(target.Database) ||
            target.ProtectedDatabases is null || target.ProtectedDatabases.Length == 0 ||
            target.ProtectedDatabases.Contains(target.Database, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Shared integration database configuration is invalid.");
        return target;
    }
}
