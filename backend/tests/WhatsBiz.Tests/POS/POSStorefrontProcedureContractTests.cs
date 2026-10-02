using FluentAssertions;

namespace WhatsBiz.Tests.POS;

public sealed class POSStorefrontProcedureContractTests
{
    [Fact]
    public void StorefrontPostCallAndManagedProcedureContractHaveTheSameParameters()
    {
        var engine = Read("backend/src/WhatsBiz.Infrastructure/POS/POSEngine.cs");
        var migration = Read("database/migrations/V44-POSStorefrontInvoiceContract.sql");
        var expected = new[]
        {
            "@TenantId", "@CounterId", "@ShiftId", "@CustomerId", "@WarehouseId", "@SalesPersonId",
            "@ItemsJson", "@PaymentsJson", "@BillDiscount", "@RoundOff", "@Remarks", "@Status", "@InterState",
            "@DiscountAuthorizedBy", "@CreatedBy", "@DeliveryCharge", "@PromotionDiscountAmount", "@AppliedPromotionId",
            "@AppliedPromotionName", "@FreeDeliveryApplied", "@FreeDeliveryThresholdSnapshot", "@ServicePincode"
        };

        var call = engine[(engine.IndexOf("Command(connection, transaction, \"sales.POS_PostInvoice\", [", StringComparison.Ordinal))..];
        var callParameters = expected.Where(name => call.Contains("(\"" + name + "\"", StringComparison.Ordinal)).ToArray();
        callParameters.Should().Equal(expected);

        var declaration = migration[..migration.IndexOf("AS", StringComparison.Ordinal)];
        var declarationParameters = expected.Where(name => declaration.Contains(name + " ", StringComparison.Ordinal)).ToArray();
        declarationParameters.Should().Equal(expected);
        migration.Should().Contain("CREATE OR ALTER PROCEDURE [sales].[POS_PostInvoice]");
    }

    [Fact]
    public void ValidatorRequiresTheFullTwentyTwoParameterContract()
    {
        var validator = Read("database/migrations/validators/V44-POSStorefrontInvoiceContract.validate.sql");
        validator.Should().Contain("COUNT(*) FROM sys.parameters").And.Contain("V44_VALID");
        validator.Should().Contain("@DeliveryCharge").And.Contain("@PromotionDiscountAmount").And.Contain("@ServicePincode").And.Contain("is_output").And.Contain("2000,0,0,0").And.Contain("1,1,0,0");
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."), relativePath));
    }
}