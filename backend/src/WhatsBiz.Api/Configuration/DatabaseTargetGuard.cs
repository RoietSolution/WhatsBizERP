using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace WhatsBiz.Api.Configuration;

public static class DatabaseTargetGuard
{
    private const string QaDatabase = "WhatsBizERP_QA";
    private const string ProductionDatabase = "WhatsBizERP_PROD";

    public static SqlConnectionStringBuilder Validate(string environmentName, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        var target = new SqlConnectionStringBuilder(connectionString);
        if (string.Equals(environmentName, "QA", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target.InitialCatalog, QaDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA must use the WhatsBizERP_QA database. Startup was stopped before accepting requests.");

        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target.InitialCatalog, ProductionDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production must use the WhatsBizERP_PROD database. Startup was stopped before accepting requests.");

        return target;
    }

    public static async Task<string> ReadOpenConnectionDatabaseNameAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var boundedTarget = new SqlConnectionStringBuilder(connectionString);
        boundedTarget.ConnectTimeout = Math.Clamp(boundedTarget.ConnectTimeout, 1, 5);
        await using var connection = new SqlConnection(boundedTarget.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME();";
        command.CommandTimeout = 5;
        var databaseName = await command.ExecuteScalarAsync(cancellationToken);
        return databaseName as string ?? throw new InvalidOperationException("The open SQL connection did not return a database name.");
    }

    public static string ValidateOpenConnectionDatabaseName(string environmentName, string configuredDatabase, string connectedDatabase)
    {
        var requiredDatabase = string.Equals(environmentName, "QA", StringComparison.OrdinalIgnoreCase) ? QaDatabase
            : string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) ? ProductionDatabase
            : null;

        if (requiredDatabase is not null &&
            (!string.Equals(configuredDatabase, requiredDatabase, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(connectedDatabase, requiredDatabase, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{environmentName} SQL connection identity verification failed. Startup was stopped before accepting requests.");

        if (!string.Equals(configuredDatabase, connectedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The open SQL connection database does not match the configured database. Startup was stopped before accepting requests.");

        return connectedDatabase;
    }
}
