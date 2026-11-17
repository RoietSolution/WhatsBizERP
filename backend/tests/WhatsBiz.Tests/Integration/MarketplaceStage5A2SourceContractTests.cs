using FluentAssertions;

namespace WhatsBiz.Tests.Integration;

/// <summary>
/// Source-only contracts. These tests do not open SQL connections and are not evidence of
/// persisted SQL behavior; real SQL coverage lives in MarketplaceV85V91SqlIntegrationTests.
/// </summary>
public sealed class MarketplaceStage5A2SourceContractTests
{
    [Fact]
    public void OnlinePaymentCaptureIsAmountCheckedIdempotentAndReconcilesLateCapture()
    {
        var root = FindRepositoryRoot();
        var payments = Read(root, "backend/src/WhatsBiz.Infrastructure/Payments/CommercePaymentService.cs");
        var schema = Read(root, "database/migrations/V88-MarketplacePaymentReconciliation.sql");
        var providerEvents = Read(root, "database/WhatsBiz.Database/Scripts/V36-TenantCommercePayments.sql");

        payments.Should().Contain("balance!=amount || grandTotal!=amount")
            .And.Contain("MarketplacePaymentReconciliations WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("AppliedAt=NULL")
            .And.Contain("if(status==CommercePaymentStatuses.Paid)return false")
            .And.Contain("if(!verified.SignatureValid)")
            .And.Contain("ProviderEventId=@event");
        schema.Should().Contain("UQ_MarketplacePaymentReconciliations_TenantPayment")
            .And.Contain("REFUND_REQUIRED");
        providerEvents.Should().Contain("UX_PaymentProviderEvents_ProviderEvent");
    }

    [Fact]
    public void ParentCodObligationHasOneAuthorizedCollectorAndReassignmentPath()
    {
        var root = FindRepositoryRoot();
        var payments = Read(root, "backend/src/WhatsBiz.Infrastructure/Payments/CommercePaymentService.cs");
        var schema = Read(root, "database/migrations/V85-MarketplaceMultiWarehouseFoundation.sql");

        payments.Should().Contain("MarketplaceCodCollections WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("groupStatus!=\"OUT_FOR_DELIVERY\"||agentUser!=actorUserId")
            .And.Contain("CollectionStatus='PENDING'")
            .And.Contain("AssignMarketplaceCod")
            .And.Contain("posted.BalanceAmount!=0");
        schema.Should().Contain("UQ_MarketplaceCodCollections_TenantOrder")
            .And.Contain("UX_MarketplaceCodCollections_Idempotency");
    }

    [Fact]
    public void MarketplaceDeliveryAuthorizationAndOtpAreGroupScoped()
    {
        var root = FindRepositoryRoot();
        var delivery = Read(root, "backend/src/WhatsBiz.Infrastructure/Delivery/DeliveryService.cs");
        var schema = Read(root, "database/migrations/V89-MarketplaceGroupDeliverySafety.sql");

        delivery.Should().Contain("WHERE o.TenantId=@tenant AND (@manage=1 OR a.UserId=@user)")
            .And.Contain("g.TenantId=@tenant AND g.OrderId=@order AND (@manage=1 OR a.UserId=@user)")
            .And.Contain("MarketplaceOtpHash(tenantId,orderId,groupId,input.Otp,salt)")
            .And.Contain("attempts>=maxOtpAttempts")
            .And.Contain("expires<=DateTimeOffset.UtcNow");
        schema.Should().Contain("PRIMARY KEY(TenantId,FulfilmentGroupId)")
            .And.Contain("UX_MarketplaceGroupOtps_ParentFingerprint");
    }

    [Fact]
    public void SettlementApprovalWaitsForDeliveryPaymentAndRefundResolution()
    {
        var root = FindRepositoryRoot();
        var settlement = Read(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplacePartnerSettlementService.cs");
        var cancellation = Read(root, "database/migrations/V91-MarketplacePartnerSettlementAccounting.sql");

        settlement.Should().Contain("g.FulfilmentStatus='DELIVERED'")
            .And.Contain("p.Status IN('PAID','REFUNDED')")
            .And.Contain("cod.CollectionStatus IN('COLLECTED','REFUND_DUE','REFUNDED')")
            .And.Contain("r.RefundStatus IN('NO_REFUND_REQUIRED','REFUNDED')")
            .And.Contain("independently confirmed payment reference");
        cancellation.Should().Contain("PartnerSettlement_CreateCancellationReviews")
            .And.Contain("NOT EXISTS(SELECT 1 FROM commerce.PartnerSettlementEntries x")
            .And.Contain("''REVIEW_REQUIRED'',''DECREASE''");
    }

    [Fact]
    public void ReservationProcedureSerializesStockChecksAndRejectsInsufficientQuantity()
    {
        var root = FindRepositoryRoot();
        var reservation = Read(root, "database/migrations/V85-MarketplaceMultiWarehouseFoundation.sql");

        reservation.Should().Contain("Marketplace_ReserveAllocation")
            .And.Contain("WITH(UPDLOCK,HOLDLOCK)")
            .And.Contain("@Available<@Quantity")
            .And.Contain("THROW 85034");
    }

    private static string Read(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
