using FluentAssertions;
using System.Text.RegularExpressions;
using WhatsBiz.Application.Features.Payments;

namespace WhatsBiz.Tests.Storefront;

public sealed class MarketplacePartnerSettlementTests
{
    [Fact]
    public void PercentageAgreementAmountsAreAllocatedAcrossWarehouseSplitsWithoutDuplicatingLineValue()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var allocation = MarketplacePartnerSettlementPolicy.Apportion(100.01m, 10m,
            [new(first, 2m), new(second, 1m)]);

        allocation.Sum(x => x.GrossAmount).Should().Be(100.01m);
        allocation.Sum(x => x.CommissionAmount).Should().Be(10.00m);
        allocation.Sum(x => x.SellerPayable).Should().Be(90.01m);
        allocation.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("PERCENT", 10, "ITEM_GROSS", 0, 0, 0, "PLATFORM", true)]
    [InlineData("FIXED", 10, "ITEM_GROSS", 0, 0, 0, "PLATFORM", false)]
    [InlineData("PERCENT", 10, "ITEM_NET", 0, 0, 0, "PLATFORM", false)]
    [InlineData("PERCENT", 10, "ITEM_GROSS", 1, 0, 0, "PLATFORM", false)]
    [InlineData("PERCENT", 10, "ITEM_GROSS", 0, 1, 0, "PLATFORM", false)]
    [InlineData("PERCENT", 10, "ITEM_GROSS", 0, 0, 1, "PLATFORM", false)]
    [InlineData("PERCENT", 10, "ITEM_GROSS", 0, 0, 0, "SUPPLIER", false)]
    public void AmbiguousAgreementOrSharedAmountsFailClosed(string type, decimal value, string basis,
        decimal itemDiscount, decimal sharedDiscount, decimal delivery, string feeResponsibility, bool expected)
    {
        MarketplacePartnerSettlementPolicy.CanCalculateAutomatically(type, value, basis,
            itemDiscount, sharedDiscount, delivery, feeResponsibility, out var reason)
            .Should().Be(expected);
        if (!expected) reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ZeroPercentAgreementIsRepresentableAndDoesNotCreateNegativeValues()
    {
        var result = MarketplacePartnerSettlementPolicy.Apportion(29.99m, 0,
            [new(Guid.NewGuid(), 1)]).Single();

        result.CommissionAmount.Should().Be(0);
        result.SellerPayable.Should().Be(result.GrossAmount);
    }

    [Fact]
    public void PostingIsAllocationIdempotentAndExcludesOwnedSellerRows()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var writer = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplacePartnerSettlementWriter.cs"));
        writer.Should().Contain("a.SellerKind='PARTNER'")
            .And.Contain("MARKETPLACE-SALE:{orderId:N}:{allocation.AllocationId:N}")
            .And.Contain("Math.Max(0m, first.PromotionDiscount)")
            .And.Contain("a.AgreementVersionSnapshot")
            .And.Contain("REVIEW_REQUIRED");

        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V91-MarketplacePartnerSettlementAccounting.sql"));
        migration.Should().Contain("PartnerSettlementEvents")
            .And.Contain("UQ_PartnerSettlementEvents_Idempotency")
            .And.Contain("TR_PartnerSettlementEntries_StateGuard")
            .And.Contain("PartnerSettlement_CreateCancellationReviews");
        File.ReadAllText(Path.Combine(root, "database/migrations/V74-CityMarketplaceAndPartnerSellers.sql"))
            .Should().Contain("UX_PartnerSettlement_Idempotency");
        var releaseGate = File.ReadAllText(Path.Combine(root, "database/migrations/V90-MarketplaceCheckoutReleaseGate.sql"));
        releaseGate.Should().Contain("MARKETPLACE_CHECKOUT").And.Contain("DISABLED");
    }

    [Fact]
    public void SettlementReviewIsFeatureAndFinancePermissionGatedAndCancellationIsLinked()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var controller = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Api/Controllers/MarketplacePartnerSettlementsController.cs"));
        controller.Should().Contain("RequireFeature(FeatureKeys.PartnerSettlements)")
            .And.Contain("Permissions.Finance.SupplierOutstandingView")
            .And.Contain("Permissions.Finance.PaymentCreate");

        var cancellation = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Delivery/DeliveryService.cs"));
        cancellation.Should().Contain("commerce.PartnerSettlement_CreateCancellationReviews")
            .And.Contain("InsertMarketplaceGroupRefundObligation");

        var service = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplacePartnerSettlementService.cs"));
        service.Should().Contain("WHERE OperatingTenantId=@tenant")
            .And.Contain("CollectionStatus IN('COLLECTED','REFUND_DUE','REFUNDED')")
            .And.Contain("r.RefundStatus IN('NO_REFUND_REQUIRED','REFUNDED')");
    }

    [Fact]
    public void V91UsesTrustedCheckForNonNullStatusAndPassesTheExistingDestructiveScanner()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V91-MarketplacePartnerSettlementAccounting.sql"));
        var validator = File.ReadAllText(Path.Combine(root, "database/migrations/validators/V91-MarketplacePartnerSettlementAccounting.validate.sql"));
        var scanner = @"(?im)\b(DROP\s+(TABLE|COLUMN|CONSTRAINT|INDEX)|TRUNCATE\s+TABLE|DELETE\s+FROM|ALTER\s+TABLE[\s\S]{0,300}?ALTER\s+COLUMN)\b";

        Regex.IsMatch(migration, scanner).Should().BeFalse("V91 should not require the destructive migration override");
        migration.Should().Contain("UPDATE commerce.PartnerSettlementEntries SET SettlementStatus='LEGACY_UNCLASSIFIED' WHERE SettlementStatus IS NULL")
            .And.Contain("DF_PartnerSettlement_Status_V91")
            .And.Contain("CK_PartnerSettlement_V91_StatusNonNull")
            .And.Contain("WITH CHECK ADD CONSTRAINT CK_PartnerSettlement_V91_StatusNonNull")
            .And.Contain("CHECK(SettlementStatus IS NOT NULL)")
            .And.Contain("WITH CHECK CHECK CONSTRAINT CK_PartnerSettlement_V91_StatusNonNull");
        migration.Should().NotContain("ALTER COLUMN SettlementStatus");

        validator.Should().Contain("TYPE_NAME(c.system_type_id)=N'varchar'")
            .And.Contain("c.max_length=24")
            .And.Contain("SettlementStatus IS NULL")
            .And.Contain("sys.default_constraints")
            .And.Contain("cc.is_disabled=0 AND cc.is_not_trusted=0")
            .And.Contain("CK_PartnerSettlement_V91_StatusNonNull");
    }

    [Fact]
    public void V91SeparatesAddedColumnsFromDependentReferencesUsingRunnerGoBatches()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V91-MarketplacePartnerSettlementAccounting.sql"));
        var runner = File.ReadAllText(Path.Combine(root, "deployment/deploy-dev-database.ps1"));
        var goParser = @"(?im)^\s*GO\s*(?:--.*)?$";
        var batches = Regex.Split(migration, goParser).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        runner.Should().Contain("[regex]::Split($Text,'(?im)^\\s*GO\\s*(?:--.*)?$')")
            .And.Contain("$command.Transaction = $Transaction");
        Regex.IsMatch(migration, goParser).Should().BeTrue();
        batches.Should().Contain(batch => batch.Contains("ADD ReturnTreatmentSnapshot")
            && !batch.Contains("CK_MarketplaceAllocations_AgreementSnapshot"));
        batches.Should().Contain(batch => batch.Contains("CK_MarketplaceAllocations_AgreementSnapshot"));
        batches.Should().Contain(batch => batch.Contains("ADD SettlementStatus varchar(24) NULL")
            && !batch.Contains("UPDATE commerce.PartnerSettlementEntries SET SettlementStatus"));
        batches.Should().Contain(batch => batch.Contains("UPDATE commerce.PartnerSettlementEntries SET SettlementStatus='LEGACY_UNCLASSIFIED'"));
        batches.Should().Contain(batch => batch.Contains("ADD ReviewReason nvarchar(1000) NULL")
            && !batch.Contains("CK_PartnerSettlement_V91_Review"));
        batches.Should().Contain(batch => batch.Contains("CK_PartnerSettlement_V91_Review"));
    }
}
