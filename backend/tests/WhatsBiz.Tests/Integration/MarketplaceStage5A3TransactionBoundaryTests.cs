using FluentAssertions;

namespace WhatsBiz.Tests.Integration;

/// <summary>
/// Source-only boundary checks. They classify isolation needs but do not verify SQL state.
/// </summary>
public sealed class MarketplaceStage5A3TransactionBoundaryTests
{
    [Fact]
    public void OnlinePaymentAndWebhookOwnCommittedTransactionsAndProviderCallsAreOutsideThem()
    {
        var root = FindRepositoryRoot();
        var payment = Read(root, "backend/src/WhatsBiz.Infrastructure/Payments/CommercePaymentService.cs");
        var idempotency = Read(root, "backend/src/WhatsBiz.Infrastructure/Persistence/SqlIdempotencyExecutor.cs");

        payment.Should().Contain("BeginTransactionAsync(token)")
            .And.Contain("await tx.CommitAsync(token)")
            .And.Contain("CreatePaymentAsync(ToGateway(config)")
            .And.Contain("ProcessRazorpayWebhookAsync")
            .And.Contain("ProviderEventId=@event")
            .And.Contain("CollectMarketplaceCodAsync")
            .And.Contain("AssignMarketplaceCodCollectionAsync");
        idempotency.Should().Contain("BeginTransactionAsync(IsolationLevel.Serializable, token)")
            .And.Contain("await transaction.CommitAsync(token)");
    }

    [Fact]
    public void DeliveryOtpCommitsBeforeNotificationAndGroupOperationsCommitIndependently()
    {
        var root = FindRepositoryRoot();
        var delivery = Read(root, "backend/src/WhatsBiz.Infrastructure/Delivery/DeliveryService.cs");

        delivery.Should().Contain("RequestMarketplaceGroupOtp")
            .And.Contain("await tx.CommitAsync(token);await NotifyMarketplaceGroupOtp")
            .And.Contain("TransitionMarketplaceGroup")
            .And.Contain("await tx.CommitAsync(token);return await ReadMarketplaceGroup")
            .And.Contain("MarketplaceOtpHash(tenantId,orderId,groupId,input.Otp,salt)")
            .And.Contain("ShouldCreateNotification(mode,outboundSafety)");
    }

    [Fact]
    public void SettlementAndCancellationServicesOwnTransactionsAndRefundPreparationCanFollowCommit()
    {
        var root = FindRepositoryRoot();
        var settlement = Read(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplacePartnerSettlementService.cs");
        var cancellation = Read(root, "backend/src/WhatsBiz.Infrastructure/Storefront/StorefrontCancellationService.cs");
        var lifecycle = Read(root, "backend/src/WhatsBiz.Infrastructure/Storefront/MarketplaceOrderLifecycleService.cs");

        settlement.Should().Contain("BeginTransactionAsync(IsolationLevel.Serializable,token)")
            .And.Contain("ReviewAsync")
            .And.Contain("MarkPaidAsync")
            .And.Contain("await tx.CommitAsync(token)")
            .And.Contain("independently confirmed payment reference")
            .And.NotContain("BankTransfer");
        cancellation.Should().Contain("BeginTransactionAsync(IsolationLevel.Serializable, token)")
            .And.Contain("await transaction.CommitAsync(token)")
            .And.Contain("refunds.PrepareFullCancellationAsync");
        lifecycle.Should().Contain("idempotency.ExecuteForTenant")
            .And.Contain("commerce.Marketplace_ReleaseOrder");
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
