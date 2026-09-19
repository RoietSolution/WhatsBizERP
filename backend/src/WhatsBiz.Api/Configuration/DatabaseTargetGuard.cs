using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace WhatsBiz.Api.Configuration;

public static class DatabaseTargetGuard
{
    public static SqlConnectionStringBuilder Validate(string environmentName, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        var target = new SqlConnectionStringBuilder(connectionString);
        if (string.Equals(environmentName, "QA", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target.InitialCatalog, "WhatsBizERP_QA", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA must use the WhatsBizERP_QA database. Startup was stopped before accepting requests.");

        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target.InitialCatalog, "WhatsBizERP_PROD", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production must use the WhatsBizERP_PROD database. Startup was stopped before accepting requests.");

        return target;
    }
}
