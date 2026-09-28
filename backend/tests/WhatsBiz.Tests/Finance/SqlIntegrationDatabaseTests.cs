using FluentAssertions;
using WhatsBiz.Tests.Integration;

namespace WhatsBiz.Tests.Finance;

public sealed class SqlIntegrationDatabaseTests
{
    [Fact]
    public void MissingExplicitConnectionFailsClosed()
    {
        var action = () => SqlIntegrationDatabase.Validate(null);
        action.Should().Throw<InvalidOperationException>().WithMessage("*explicit disposable integration database*");
    }

    [Theory]
    [InlineData("WhatsBizERP")]
    [InlineData("WhatsBizERP_QA")]
    [InlineData("WhatsBizERP_PROD")]
    [InlineData("RetailerLive")]
    public void OrdinaryDatabaseNamesAreRejected(string database)
    {
        var action = () => SqlIntegrationDatabase.Validate($"Server=localhost;Database={database};Integrated Security=True");
        action.Should().Throw<InvalidOperationException>().WithMessage("*disposable Test/Integration database*");
    }

    [Fact]
    public void ExplicitDisposableDatabaseIsAccepted()
    {
        var connection = SqlIntegrationDatabase.Validate("Server=localhost;Database=WhatsBizERP_Phase1CommerceTest;Integrated Security=True");
        connection.Should().Contain("WhatsBizERP_Phase1CommerceTest");
    }
}
