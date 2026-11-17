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

    [Fact]
    public void PowerShellAssignmentIsNotAcceptedAsAConnectionStringValue()
    {
        var action = () => SqlIntegrationDatabase.Validate("$env:ConnectionStrings__IntegrationTests='Server=localhost;Database=WhatsBizERP_Phase1CommerceTest'");
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*must contain only the SQL connection string value*")
            .WithMessage("*do not include PowerShell $env: assignment syntax*");
    }

    [Theory]
    [InlineData("WhatsBizERP")]
    [InlineData("WhatsBizERP_QA")]
    [InlineData("WhatsBizERP_PROD")]
    [InlineData("RetailerLive")]
    public void OrdinaryDatabaseNamesAreRejected(string database)
    {
        var action = () => SqlIntegrationDatabase.Validate($"Server=localhost;Database={database};Integrated Security=True");
        action.Should().Throw<InvalidOperationException>().WithMessage("*exact approved disposable target*");
    }

    [Fact]
    public void ExplicitDisposableDatabaseIsAccepted()
    {
        var connection = SqlIntegrationDatabase.Validate(
            "Server=DESKTOP-DQ0868S;Database=WhatsBizERP_GuturGoMarketplaceTest_V91;Integrated Security=True;Pooling=False");
        connection.Should().Contain("WhatsBizERP_GuturGoMarketplaceTest_V91");
    }

    [Theory]
    [InlineData("Server=DESKTOP-DQ0868S;Database=WhatsBizERP;Integrated Security=True;Pooling=False")]
    [InlineData("Server=DESKTOP-DQ0868S;Database=WhatsBizERP_GuturGoMarketplaceTest_V91;User Id=operator;Password=not-a-real-secret;Pooling=False")]
    [InlineData("Server=other-host;Database=WhatsBizERP_GuturGoMarketplaceTest_V91;Integrated Security=True;Pooling=False")]
    [InlineData("Server=DESKTOP-DQ0868S;Database=WhatsBizERP_GuturGoMarketplaceTest_V91;Integrated Security=True;Pooling=True")]
    public void UnexpectedOrCredentialBearingTargetsAreRejected(string value)
    {
        var action = () => SqlIntegrationDatabase.Validate(value);
        action.Should().Throw<InvalidOperationException>().WithMessage("*exact approved disposable target*");
    }
}
