using Microsoft.Data.SqlClient;

namespace WhatsBiz.Tests.Integration;

public sealed class IntegrationDatabaseTargetTests
{
    [Fact]
    public void SharedTargetConfigurationAcceptsOnlyTheReusableDatabase()
    {
        var approved = new SqlConnectionStringBuilder
        {
            DataSource = IntegrationDatabaseTarget.Server,
            InitialCatalog = IntegrationDatabaseTarget.Database,
            IntegratedSecurity = true
        };

        IntegrationDatabaseTarget.AssertApproved(approved);
        Assert.Throws<InvalidOperationException>(() => IntegrationDatabaseTarget.AssertApproved(new SqlConnectionStringBuilder
        {
            DataSource = IntegrationDatabaseTarget.Server,
            InitialCatalog = "WhatsBizERP_IntegrationTest",
            IntegratedSecurity = true
        }));
        Assert.Throws<InvalidOperationException>(() => IntegrationDatabaseTarget.AssertApproved(new SqlConnectionStringBuilder
        {
            DataSource = "other-server",
            InitialCatalog = IntegrationDatabaseTarget.Database,
            IntegratedSecurity = true
        }));
        Assert.Throws<InvalidOperationException>(() => IntegrationDatabaseTarget.AssertApproved(new SqlConnectionStringBuilder
        {
            DataSource = IntegrationDatabaseTarget.Server,
            InitialCatalog = "WhatsBizERP_QA",
            IntegratedSecurity = true
        }));
    }
}
