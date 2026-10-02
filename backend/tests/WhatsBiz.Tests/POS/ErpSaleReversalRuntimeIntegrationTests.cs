using System.Data;
using System.Text;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Payments;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Payments;
using WhatsBiz.Infrastructure.POS;
using WhatsBiz.Infrastructure.Storefront;
using WhatsBiz.Tests.Integration;
using Xunit.Abstractions;

namespace WhatsBiz.Tests.POS;

[Collection("SQL phase 2 full sale reversal")]
public sealed class ErpSaleReversalRuntimeIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PaidStorefrontApprovalReversesAndPreparesRefundWithoutSettlingMoney()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(1000m,50m,40m,0m,"BANK",990m);
        await fixture.SeedCollectionForRefundAsync(invoice,"RAZORPAY",990m);
        var actor=await fixture.CreateRetailerActorAsync();
        var service=fixture.CancellationService();
        var requested=await service.RequestAsync(fixture.StoreKey,"synthetic-session",invoice,
            "Customer changed mind",default);
        requested!.RequestStatus.Should().Be("REQUESTED");
        var approved=await service.DecideAsync(fixture.TenantId,invoice,actor,true,null,default);
        approved.RequestStatus.Should().Be("APPROVED");
        approved.RefundStatus.Should().Be("REFUND_REQUIRED");
        approved.RefundAmount.Should().Be(990m);
        var replay=await service.DecideAsync(fixture.TenantId,invoice,actor,true,null,default);
        replay.RequestStatus.Should().Be("APPROVED");
        var status=await fixture.CancellationSnapshotAsync(invoice);
        status.Should().Be((1,1,1,0,0,0,990m,"APPROVED","REFUND_REQUIRED"));
        var original=await fixture.SnapshotAsync(invoice);
        original.PaymentJournals.Should().Be(1);
        original.BankInAmount.Should().Be(990m);
        original.StockRestorationCount.Should().Be(1);
        output.WriteLine("PAID_CANCELLATION_APPROVAL {0}",status);
    }

    [Fact]
    public async Task PaidStorefrontRejectionAndWrongTenantDoNotReverseSale()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,"BANK",440m);
        await fixture.SeedCollectionForRefundAsync(invoice,"DIRECT_UPI",440m);
        var actor=await fixture.CreateRetailerActorAsync();
        await fixture.SeedCancellationRequestAsync(invoice);
        var service=fixture.CancellationService();
        var wrong=()=>service.DecideAsync(Guid.NewGuid(),invoice,actor,true,null,default);
        await wrong.Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.EntityNotFoundException>();
        var rejected=await service.DecideAsync(fixture.TenantId,invoice,actor,false,"Customer contacted",default);
        rejected.RequestStatus.Should().Be("REJECTED");
        rejected.DecisionNote.Should().Be("Customer contacted");
        var status=await fixture.CancellationSnapshotAsync(invoice);
        status.ReversalCount.Should().Be(0);
        status.RefundCount.Should().Be(0);
        status.ApprovalStatus.Should().Be("REJECTED");
        (await fixture.SnapshotAsync(invoice)).Status.Should().Be("COMPLETED");
    }

    [Fact]
    public async Task AdvancedDeliveryCannotStartPaidApproval()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,"BANK",440m);
        await fixture.SeedCollectionForRefundAsync(invoice,"DIRECT_UPI",440m);
        var actor=await fixture.CreateRetailerActorAsync();
        await fixture.SeedCancellationRequestAsync(invoice);
        await fixture.AddDeliveryAsync(invoice,"OUT_FOR_DELIVERY");
        var action=()=>fixture.CancellationService().DecideAsync(fixture.TenantId,invoice,actor,true,null,default);
        await action.Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.BusinessRuleException>();
        var status=await fixture.CancellationSnapshotAsync(invoice);
        status.ReversalCount.Should().Be(0);
        status.RefundCount.Should().Be(0);
        status.ApprovalStatus.Should().Be("REQUESTED");
    }

    [Fact]
    public async Task ConcurrentPaidApprovalHasOneReversalAndOneRefundObligation()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,"BANK",440m);
        await fixture.SeedCollectionForRefundAsync(invoice,"DIRECT_UPI",440m);
        var actor=await fixture.CreateRetailerActorAsync();
        await fixture.SeedCancellationRequestAsync(invoice);
        var service=fixture.CancellationService();
        var decisions=await Task.WhenAll(
            service.DecideAsync(fixture.TenantId,invoice,actor,true,null,default),
            service.DecideAsync(fixture.TenantId,invoice,actor,true,null,default));
        decisions.Should().OnlyContain(x=>x.RequestStatus=="APPROVED");
        var status=await fixture.CancellationSnapshotAsync(invoice);
        status.Should().Be((1,1,1,0,0,0,440m,"APPROVED","REFUND_REQUIRED"));
        (await fixture.SnapshotAsync(invoice)).StockRestorationCount.Should().Be(1);
    }

    [Fact]
    public async Task UncollectedHeldCodUsesExistingVoidWithoutReversalOrRefund()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateHeldCodAsync(440m);
        var actor=await fixture.CreateRetailerActorAsync();
        var service=fixture.CancellationService();
        (await service.RequestAsync(fixture.StoreKey,"synthetic-session",invoice,
            "Ordered by mistake",default))!.RequestStatus.Should().Be("REQUESTED");
        var result=await service.DecideAsync(
            fixture.TenantId,invoice,actor,true,null,default);
        result.RequestStatus.Should().Be("APPROVED");
        result.RefundStatus.Should().Be("NONE");
        var status=await fixture.CancellationSnapshotAsync(invoice);
        status.Should().Be((1,0,0,0,0,0,0m,"APPROVED","NONE"));
        output.WriteLine("HELD_COD_CANCELLATION {0}",status);
    }

    [Fact]
    public async Task ApprovedRazorpayCancellationStaysPendingThroughUnknownThenSettlesOnce()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(1000m,50m,40m,0m,"BANK",990m);
        await fixture.SeedCollectionForRefundAsync(invoice,"RAZORPAY",990m);
        var actor=await fixture.CreateRetailerActorAsync();
        await fixture.SeedCancellationRequestAsync(invoice);
        (await fixture.CancellationService().DecideAsync(fixture.TenantId,invoice,actor,true,null,default))
            .RefundStatus.Should().Be("REFUND_REQUIRED");
        var refund=await fixture.RefundIdAsync(invoice);
        var gateway=new RefundGateway(refund,990m);
        var service=await fixture.RazorpayRefundServiceAsync(gateway);
        (await service.StartRazorpayAsync(fixture.TenantId,refund,default)).RefundStatus.Should().Be("REFUND_PENDING");
        var pending=await fixture.RefundSnapshotAsync(invoice,refund);
        pending.RefundJournalCount.Should().Be(0);
        pending.BankOutCount.Should().Be(0);
        (await fixture.CancellationSnapshotAsync(invoice)).ApprovalStatus.Should().Be("APPROVED");
        gateway.ProviderPaymentId=await fixture.ProviderPaymentIdAsync(refund);
        var body=Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            @event="refund.processed",
            payload=new { refund=new { entity=new { id=gateway.RefundProviderId,
                payment_id=gateway.ProviderPaymentId, notes=new { refund_id=refund.ToString("N") } } } }
        }));
        await service.ProcessRazorpayRefundWebhookAsync(body,"test-valid-signature",default);
        await service.ProcessRazorpayRefundWebhookAsync(body,"test-valid-signature",default);
        var settled=await fixture.RefundSnapshotAsync(invoice,refund);
        settled.RefundJournalCount.Should().Be(1);
        settled.BankOutAmount.Should().Be(990m);
        settled.CustomerDebit.Should().Be(990m);
        settled.RefundStatus.Should().Be("REFUNDED");
        (await fixture.CancellationSnapshotAsync(invoice)).ApprovalStatus.Should().Be("APPROVED");
        gateway.CreateCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("DIRECT_UPI","UPI",false)]
    [InlineData("COD","CASH",true)]
    public async Task ApprovedManualCancellationSettlesOnlyAfterExternalConfirmation(
        string provider,string settlementMode,bool cash)
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,cash?"CASH":"UPI",440m);
        await fixture.SeedCollectionForRefundAsync(invoice,provider,440m);
        var actor=await fixture.CreateRetailerActorAsync();
        await fixture.SeedCancellationRequestAsync(invoice);
        (await fixture.CancellationService().DecideAsync(fixture.TenantId,invoice,actor,true,null,default))
            .RefundStatus.Should().Be("REFUND_REQUIRED");
        var refund=await fixture.RefundIdAsync(invoice);
        var before=await fixture.RefundSnapshotAsync(invoice,refund);
        before.CustomerDebit.Should().Be(0m);
        before.BankOutCount.Should().Be(0);
        before.CashOutCount.Should().Be(0);
        var input=new ConfirmManualRefundInput(settlementMode,$"TEST-{refund:N}",DateTimeOffset.UtcNow);
        (await fixture.RefundService().ConfirmManualAsync(fixture.TenantId,refund,input,"synthetic-retailer",default))
            .RefundStatus.Should().Be("REFUNDED");
        var after=await fixture.RefundSnapshotAsync(invoice,refund);
        after.CustomerDebit.Should().Be(440m);
        after.RefundJournalCount.Should().Be(1);
        after.CashOutCount.Should().Be(cash?1:0);
        after.BankOutCount.Should().Be(cash?0:1);
    }

    [Fact]
    public async Task ConfirmedBankRefundSettlesExactlyOnceAfterFullReversal()
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateCompletedSaleAsync(1000m, 50m, 40m, 0m, "BANK", 990m);
        await fixture.ReverseAsync(invoice);
        var refund = await fixture.SeedConfirmedRefundAsync(invoice, "RAZORPAY", 990m);
        var first = await fixture.SettleRefundAsync(refund, "RAZORPAY");
        first.AlreadySettled.Should().BeFalse();
        first.Amount.Should().Be(990m);
        var replay = await fixture.SettleRefundAsync(refund, "RAZORPAY");
        replay.AlreadySettled.Should().BeTrue();
        replay.JournalEntryId.Should().Be(first.JournalEntryId);
        var result = await fixture.RefundSnapshotAsync(invoice, refund);
        result.Should().Be((1, 1, 1, 990m, 1, 990m, 0, 1, 1, "REFUNDED", "REFUNDED"));
        var original=await fixture.SnapshotAsync(invoice);
        original.GrandTotal.Should().Be(990m);
        original.PaidAmount.Should().Be(990m);
        original.SaleJournals.Should().Be(1);
        original.PaymentJournals.Should().Be(1);
        original.ReversalJournals.Should().Be(1);
        original.BankInCount.Should().Be(1);
        original.BankInAmount.Should().Be(990m);
        original.StockSaleCount.Should().Be(1);
        original.StockRestorationCount.Should().Be(1);
        output.WriteLine("PHASE3_DETAILED {0}",await fixture.AssertDetailedRefundAuditAsync(invoice,refund));
        output.WriteLine("PHASE3_BANK {0}", result);
    }

    [Fact]
    public async Task ConfirmedCollectedCodCashRefundUsesCashBook()
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateCompletedSaleAsync(440m, 0m, 0m, 0m, "CASH", 440m);
        await fixture.ReverseAsync(invoice);
        var refund = await fixture.SeedConfirmedRefundAsync(invoice, "COD", 440m);
        (await fixture.SettleRefundAsync(refund, "CASH")).Amount.Should().Be(440m);
        var result = await fixture.RefundSnapshotAsync(invoice, refund);
        result.BankOutCount.Should().Be(0);
        result.CashOutCount.Should().Be(1);
        result.CustomerDebit.Should().Be(440m);
        result.RefundStatus.Should().Be("REFUNDED");
        output.WriteLine("PHASE3_COD {0}", result);
    }

    [Fact]
    public async Task ManualDirectUpiConfirmationPostsOneBankRefund()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,"UPI",440m);
        await fixture.ReverseAsync(invoice);
        var refund=await fixture.SeedConfirmedRefundAsync(invoice,"DIRECT_UPI",440m,false);
        var service=fixture.RefundService();
        var input=new ConfirmManualRefundInput("UPI",$"TEST-{refund:N}",DateTimeOffset.UtcNow);
        var first=await service.ConfirmManualAsync(fixture.TenantId,refund,input,"synthetic-retailer",default);
        first.RefundStatus.Should().Be("REFUNDED");
        var replay=await service.ConfirmManualAsync(fixture.TenantId,refund,input,"synthetic-retailer",default);
        replay.RefundStatus.Should().Be("REFUNDED");
        var result=await fixture.RefundSnapshotAsync(invoice,refund);
        result.RefundJournalCount.Should().Be(1);
        result.CustomerDebit.Should().Be(440m);
        result.BankOutCount.Should().Be(1);
        result.BankOutAmount.Should().Be(440m);
        result.CashOutCount.Should().Be(0);
        output.WriteLine("PHASE3_UPI {0}",result);
    }

    [Fact]
    public async Task UnknownRazorpayOutcomeCannotDispatchTwiceAndReconcilesBeforeSettlement()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(1000m,50m,40m,0m,"BANK",990m);
        await fixture.ReverseAsync(invoice);
        var refund=await fixture.SeedConfirmedRefundAsync(invoice,"RAZORPAY",990m,false);
        var fake=new RefundGateway(refund,990m);
        var service=await fixture.RazorpayRefundServiceAsync(fake);
        var first=await service.StartRazorpayAsync(fixture.TenantId,refund,default);
        first.RefundStatus.Should().Be("REFUND_PENDING");
        fake.CreateCalls.Should().Be(1);
        var before=await fixture.RefundSnapshotAsync(invoice,refund);
        before.RefundJournalCount.Should().Be(0);
        before.BankOutCount.Should().Be(0);
        var replay=()=>service.StartRazorpayAsync(fixture.TenantId,refund,default);
        await replay.Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.BusinessRuleException>();
        fake.CreateCalls.Should().Be(1);
        fake.ProviderPaymentId=await fixture.ProviderPaymentIdAsync(refund);
        var body=Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        {
            @event="refund.processed",
            payload=new { refund=new { entity=new { id=fake.RefundProviderId,
                payment_id=fake.ProviderPaymentId, notes=new { refund_id=refund.ToString("N") } } } }
        }));
        var invalid=()=>service.ProcessRazorpayRefundWebhookAsync(body,"invalid",default);
        await invalid.Should().ThrowAsync<System.UnauthorizedAccessException>();
        (await fixture.RefundSnapshotAsync(invoice,refund)).RefundJournalCount.Should().Be(0);
        await service.ProcessRazorpayRefundWebhookAsync(body,"test-valid-signature",default);
        await service.ProcessRazorpayRefundWebhookAsync(body,"test-valid-signature",default);
        var reconciled=await service.GetAsync(fixture.TenantId,refund,default);
        reconciled.RefundStatus.Should().Be("REFUNDED");
        var after=await fixture.RefundSnapshotAsync(invoice,refund);
        after.RefundJournalCount.Should().Be(1);
        after.CustomerDebit.Should().Be(990m);
        after.BankOutCount.Should().Be(1);
        after.BankOutAmount.Should().Be(990m);
        fake.CreateCalls.Should().Be(1);
        output.WriteLine("PHASE3_RAZORPAY_UNKNOWN {0}",after);
    }

    [Fact]
    public async Task SettlementRejectsWrongTenantAndExcessBeforeMoneyMoves()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(1000m,50m,40m,0m,"BANK",990m);
        await fixture.ReverseAsync(invoice);
        var refund=await fixture.SeedConfirmedRefundAsync(invoice,"RAZORPAY",1000m);
        var commerceRead=()=>fixture.RefundService().GetAsync(Guid.NewGuid(),refund,default);
        await commerceRead.Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.BusinessRuleException>();
        var wrongTenant=()=>fixture.SettleRefundAsync(refund,"RAZORPAY",Guid.NewGuid());
        await wrongTenant.Should().ThrowAsync<SqlException>();
        var excessive=()=>fixture.SettleRefundAsync(refund,"RAZORPAY");
        await excessive.Should().ThrowAsync<SqlException>();
        var snapshot=await fixture.RefundSnapshotAsync(invoice,refund);
        snapshot.RefundJournalCount.Should().Be(0);
        snapshot.CustomerDebit.Should().Be(0m);
        snapshot.BankOutCount.Should().Be(0);
        snapshot.RefundStatus.Should().Be("REFUND_PENDING");
    }

    [Fact]
    public async Task ConcurrentConfirmedSettlementPostsOnlyOneFinancialEffect()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,"BANK",440m);
        await fixture.ReverseAsync(invoice);
        var refund=await fixture.SeedConfirmedRefundAsync(invoice,"RAZORPAY",440m);
        var results=await Task.WhenAll(fixture.SettleRefundAsync(refund,"RAZORPAY"),
            fixture.SettleRefundAsync(refund,"RAZORPAY"));
        results.Count(x=>x.AlreadySettled).Should().Be(1);
        var snapshot=await fixture.RefundSnapshotAsync(invoice,refund);
        snapshot.RefundJournalCount.Should().Be(1);
        snapshot.BankOutCount.Should().Be(1);
        snapshot.BankOutAmount.Should().Be(440m);
        snapshot.CustomerDebit.Should().Be(440m);
    }

    [Fact]
    public async Task CommercePreparationUsesPhase2ObligationAndIsIdempotent()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(1000m,50m,40m,0m,"BANK",990m);
        await fixture.ReverseAsync(invoice);
        await fixture.SeedCollectionForRefundAsync(invoice,"RAZORPAY",990m);
        var actor=await fixture.CreateRetailerActorAsync();
        var service=fixture.RefundService();
        var first=await service.PrepareFullCancellationAsync(fixture.TenantId,invoice,actor,default);
        first.RefundAmount.Should().Be(990m);
        first.RefundStatus.Should().Be("REFUND_REQUIRED");
        var replay=await service.PrepareFullCancellationAsync(fixture.TenantId,invoice,actor,default);
        replay.RefundId.Should().Be(first.RefundId);
        var snapshot=await fixture.RefundSnapshotAsync(invoice,first.RefundId);
        snapshot.RefundJournalCount.Should().Be(0);
        snapshot.BankOutCount.Should().Be(0);
        output.WriteLine("PHASE3_PREPARED {0}",first);
    }

    [Fact]
    public async Task FailureAfterRefundJournalPostingRollsBackMoneyOutAndStatus()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,"BANK",440m);
        await fixture.ReverseAsync(invoice);
        var refund=await fixture.SeedConfirmedRefundAsync(invoice,"RAZORPAY",440m);
        await fixture.SetRefundFailureTriggerAsync(true);
        try
        {
            var action=()=>fixture.SettleRefundAsync(refund,"RAZORPAY");
            await action.Should().ThrowAsync<SqlException>();
            var rolledBack=await fixture.RefundSnapshotAsync(invoice,refund);
            rolledBack.RefundJournalCount.Should().Be(0);
            rolledBack.CustomerDebit.Should().Be(0m);
            rolledBack.BankOutCount.Should().Be(0);
            rolledBack.RefundStatus.Should().Be("REFUND_PENDING");
            rolledBack.ObligationStatus.Should().Be("REFUND_REQUIRED");
        }
        finally { await fixture.SetRefundFailureTriggerAsync(false); }
        (await fixture.SettleRefundAsync(refund,"RAZORPAY")).Amount.Should().Be(440m);
        (await fixture.RefundSnapshotAsync(invoice,refund)).RefundJournalCount.Should().Be(1);
    }

    [Fact]
    public async Task AcceptedRazorpayRefundIsNotFinanciallySettledUntilProviderProcessesIt()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(990m,0m,0m,0m,"BANK",990m);
        await fixture.ReverseAsync(invoice);
        var refund=await fixture.SeedConfirmedRefundAsync(invoice,"RAZORPAY",990m,false);
        var gateway=new RefundGateway(refund,990m,false);
        var service=await fixture.RazorpayRefundServiceAsync(gateway);
        var accepted=await service.StartRazorpayAsync(fixture.TenantId,refund,default);
        accepted.RefundStatus.Should().Be("REFUND_PENDING");
        accepted.ProviderRefundId.Should().Be(gateway.RefundProviderId);
        (await fixture.RefundSnapshotAsync(invoice,refund)).RefundJournalCount.Should().Be(0);
        var confirmed=await service.ReconcileRazorpayAsync(fixture.TenantId,refund,default);
        confirmed.RefundStatus.Should().Be("REFUNDED");
        (await fixture.RefundSnapshotAsync(invoice,refund)).RefundJournalCount.Should().Be(1);
        gateway.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task DefinitivelyFailedRazorpayAttemptCanRetryWithNewAttemptIdentity()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(440m,0m,0m,0m,"BANK",440m);
        await fixture.ReverseAsync(invoice);
        var refund=await fixture.SeedConfirmedRefundAsync(invoice,"RAZORPAY",440m,false);
        var gateway=new RefundGateway(refund,440m,false,"FAILED");
        var service=await fixture.RazorpayRefundServiceAsync(gateway);
        var failed=await service.StartRazorpayAsync(fixture.TenantId,refund,default);
        failed.RefundStatus.Should().Be("REFUND_FAILED");
        (await fixture.RefundSnapshotAsync(invoice,refund)).RefundJournalCount.Should().Be(0);
        var retried=await service.StartRazorpayAsync(fixture.TenantId,refund,default);
        retried.RefundStatus.Should().Be("REFUNDED");
        gateway.CreateCalls.Should().Be(2);
        var snapshot=await fixture.RefundSnapshotAsync(invoice,refund);
        snapshot.RefundJournalCount.Should().Be(1);
        snapshot.BankOutAmount.Should().Be(440m);
    }
    [Fact]
    public async Task PaidPromotionDeliverySaleReversesOnceWithoutRefundOrReceiptReversal()
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateCompletedSaleAsync(1000m, 50m, 40m, 0m, "BANK", 990m);
        var before = await fixture.SnapshotAsync(invoice);
        before.Status.Should().Be("COMPLETED");
        before.GrandTotal.Should().Be(990m);
        before.SaleJournals.Should().Be(1);
        before.PaymentJournals.Should().Be(1);
        before.SalePaymentAmount.Should().Be(990m);
        before.BankInCount.Should().Be(1);
        before.BankInAmount.Should().Be(990m);
        before.StockSaleCount.Should().Be(1);
        before.StockQuantity.Should().Be(9m);

        var result = await fixture.ReverseAsync(invoice);
        result.AlreadyReversed.Should().BeFalse();
        result.OriginalGrandTotal.Should().Be(990m);
        result.CollectedAmount.Should().Be(990m);
        result.RefundRequiredAmount.Should().Be(990m);
        result.RefundRequired.Should().BeTrue();
        var after = await fixture.SnapshotAsync(invoice);
        AssertReversed(after, 990m, 990m);
        after.SalesDebit.Should().Be(950m);
        after.DeliveryDebit.Should().Be(40m);
        after.CustomerCredit.Should().Be(990m);
        var counts = await fixture.DetailCountsAsync(invoice);
        counts.OriginalSaleLines.Should().Be(3);
        counts.ReversalLines.Should().Be(3);
        counts.OriginalStockLines.Should().Be(1);
        counts.RestorationLines.Should().Be(1);
        counts.ReversalCustomerLedgerCount.Should().Be(1);
        counts.ReversalCustomerLedgerCredit.Should().Be(990m);
        counts.SalePaymentAmount.Should().Be(990m);
        counts.RefundStatus.Should().Be("REFUND_REQUIRED");
        output.WriteLine("PAID_BEFORE {0}", before);
        output.WriteLine("PAID_AFTER {0}", after);
        output.WriteLine("PAID_DETAILS {0}", counts);

        var replay = await fixture.ReverseAsync(invoice);
        replay.AlreadyReversed.Should().BeTrue();
        replay.ReversalId.Should().Be(result.ReversalId);
        (await fixture.SnapshotAsync(invoice)).Should().Be(after);
    }

    [Fact]
    public async Task SavedGstIsInvertedAndUncollectedCompletedSaleHasNoRefund()
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateCompletedSaleAsync(118m, 0m, 0m, 18m, null, 0m);
        var before = await fixture.SnapshotAsync(invoice);
        before.SaleGstCredit.Should().BeGreaterThan(0m);
        var result = await fixture.ReverseAsync(invoice);
        result.CollectedAmount.Should().Be(0m);
        result.RefundRequired.Should().BeFalse();
        (await fixture.DetailCountsAsync(invoice)).RefundStatus.Should().Be("NONE");
        var after = await fixture.SnapshotAsync(invoice);
        AssertReversed(after, before.GrandTotal, 0m);
        after.ReversalGstDebit.Should().Be(before.SaleGstCredit);
        output.WriteLine("GST_BEFORE {0}", before);
        output.WriteLine("GST_AFTER {0}", after);
    }

    [Fact]
    public async Task CollectedCodRequiresRefundButDoesNotCreateCashOutflow()
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateCompletedSaleAsync(400m, 0m, 40m, 0m, "CASH", 440m);
        var before = await fixture.SnapshotAsync(invoice);
        before.CashInCount.Should().Be(1);
        before.CashInAmount.Should().Be(440m);
        var result = await fixture.ReverseAsync(invoice);
        result.CollectedAmount.Should().Be(440m);
        result.RefundRequiredAmount.Should().Be(440m);
        var after = await fixture.SnapshotAsync(invoice);
        AssertReversed(after, 440m, 440m);
        after.CashInCount.Should().Be(1);
        after.CashInAmount.Should().Be(440m);
        after.CashOutCount.Should().Be(0);
        output.WriteLine("COD_AFTER {0}", after);
    }

    [Fact]
    public async Task ImmediatePosInlineReceiptRemainsInsideOriginalSaleJournal()
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateImmediateSaleAsync();
        var before = await fixture.SnapshotAsync(invoice);
        before.SaleJournals.Should().Be(1);
        before.PaymentJournals.Should().Be(0);
        before.CustomerReceiptCount.Should().Be(1);
        before.CashInAmount.Should().Be(500m);
        var result = await fixture.ReverseAsync(invoice);
        result.CollectedAmount.Should().Be(500m);
        var after = await fixture.SnapshotAsync(invoice);
        after.Status.Should().Be("CANCELLED");
        after.ReversalJournals.Should().Be(1);
        after.PaymentJournals.Should().Be(0);
        after.CustomerReceiptCount.Should().Be(1);
        after.CustomerReceiptAmount.Should().Be(500m);
        after.CashInCount.Should().Be(1);
        after.CashInAmount.Should().Be(500m);
        after.CashOutCount.Should().Be(0);
        after.SalesDebit.Should().Be(500m);
        after.CustomerCredit.Should().Be(500m);
        (await fixture.DetailCountsAsync(invoice)).OriginalSaleLines.Should().Be(4);
        (await fixture.DetailCountsAsync(invoice)).ReversalLines.Should().Be(2);
        output.WriteLine("INLINE_POS_AFTER {0}", after);
    }

    [Fact]
    public async Task ConcurrentReversalCallsProduceOneJournalAndOneStockRestoration()
    {
        var fixture=await Fixture.CreateAsync();
        var invoice=await fixture.CreateCompletedSaleAsync(300m,0m,0m,0m,null,0m);
        var results=await Task.WhenAll(fixture.ReverseAsync(invoice),fixture.ReverseAsync(invoice));
        results.Count(result=>result.AlreadyReversed).Should().Be(1);
        results[0].ReversalId.Should().Be(results[1].ReversalId);
        var after=await fixture.SnapshotAsync(invoice);
        after.ReversalJournals.Should().Be(1);
        after.ReversalRows.Should().Be(1);
        after.StockRestorationCount.Should().Be(1);
        after.StockQuantity.Should().Be(10m);
    }

    [Fact]
    public async Task PriorReturnAdvancedDeliveryAndWrongTenantFailClosed()
    {
        var fixture = await Fixture.CreateAsync();
        var returned = await fixture.CreateCompletedSaleAsync(100m, 0m, 0m, 0m, null, 0m);
        await fixture.MarkReturnActivityAsync(returned);
        var returnBefore = await fixture.SnapshotAsync(returned);
        await FluentActions.Invoking(() => fixture.ReverseAsync(returned)).Should().ThrowAsync<SqlException>();
        (await fixture.SnapshotAsync(returned)).Should().Be(returnBefore);

        var delivered = await fixture.CreateCompletedSaleAsync(100m, 0m, 0m, 0m, null, 0m);
        await fixture.AddDeliveryAsync(delivered, "DELIVERED");
        var deliveryBefore = await fixture.SnapshotAsync(delivered);
        await FluentActions.Invoking(() => fixture.ReverseAsync(delivered)).Should().ThrowAsync<SqlException>();
        (await fixture.SnapshotAsync(delivered)).Should().Be(deliveryBefore);

        var foreign = await fixture.CreateCompletedSaleAsync(100m, 0m, 0m, 0m, null, 0m);
        var foreignBefore = await fixture.SnapshotAsync(foreign);
        var otherTenant = await Fixture.CreateAsync();
        await FluentActions.Invoking(() => fixture.ReverseAsync(foreign, otherTenant.TenantId)).Should().ThrowAsync<SqlException>();
        (await fixture.SnapshotAsync(foreign)).Should().Be(foreignBefore);
        (await fixture.ReverseAsync(foreign)).AlreadyReversed.Should().BeFalse();
        (await fixture.SnapshotAsync(foreign)).ReversalRows.Should().Be(1);
    }

    [Fact]
    public async Task FailureAfterJournalWriteRollsBackFinanceStockAndBusinessState()
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateCompletedSaleAsync(200m, 0m, 0m, 0m, null, 0m);
        await fixture.RemoveStockBalanceAsync();
        var before = await fixture.SnapshotAsync(invoice);
        await FluentActions.Invoking(() => fixture.ReverseAsync(invoice)).Should().ThrowAsync<SqlException>();
        var after = await fixture.SnapshotAsync(invoice);
        after.Should().Be(before);
        after.Status.Should().Be("COMPLETED");
        after.ReversalRows.Should().Be(0);
        after.ReversalJournals.Should().Be(0);
        after.StockRestorationCount.Should().Be(0);
        output.WriteLine("ROLLED_BACK {0}", after);
    }

    [Theory]
    [InlineData("UNASSIGNED")]
    [InlineData("ASSIGNED")]
    [InlineData("READY_FOR_PICKUP")]
    public async Task EarlyDeliveryStateIsCancelledAtomically(string deliveryStatus)
    {
        var fixture = await Fixture.CreateAsync();
        var invoice = await fixture.CreateCompletedSaleAsync(100m,0m,0m,0m,null,0m);
        await fixture.AddDeliveryAsync(invoice,deliveryStatus);
        await fixture.ReverseAsync(invoice);
        var delivery = await fixture.DeliverySnapshotAsync(invoice);
        delivery.Status.Should().Be("CANCELLED");
        delivery.ReversalEvents.Should().Be(1);
        (await fixture.SnapshotAsync(invoice)).StockRestorationCount.Should().Be(1);
    }

    private static void AssertReversed(Snapshot row, decimal original, decimal collected)
    {
        row.Status.Should().Be("CANCELLED");
        row.GrandTotal.Should().Be(original);
        row.PaidAmount.Should().Be(collected);
        row.SaleJournals.Should().Be(1);
        row.ReversalJournals.Should().Be(1);
        row.ReversalRows.Should().Be(1);
        row.PaymentJournals.Should().Be(collected > 0 ? 1 : 0);
        row.SalePaymentAmount.Should().Be(collected);
        row.CustomerReceiptCount.Should().Be(collected > 0 ? 1 : 0);
        row.CustomerReceiptAmount.Should().Be(collected);
        row.BankOutCount.Should().Be(0);
        row.CashOutCount.Should().Be(0);
        row.StockSaleCount.Should().Be(1);
        row.StockRestorationCount.Should().Be(1);
        row.StockQuantity.Should().Be(10m);
        row.ReversalDetailCount.Should().BeGreaterThan(1);
        row.ReversalDebit.Should().Be(original);
        row.ReversalCredit.Should().Be(original);
        row.RefundRequiredAmount.Should().Be(collected);
        row.RefundRows.Should().Be(0);
    }

    private sealed class Fixture(string connectionString, Guid tenant, Guid customer, Guid warehouse,
        Guid product, string tag)
    {
        public Guid TenantId => tenant;
        public string StoreKey => tag;
        public static async Task<Fixture> CreateAsync()
        {
            var connectionString = SqlIntegrationDatabase.ConnectionString;
            var tenant = Guid.NewGuid(); var customer = Guid.NewGuid(); var warehouse = Guid.NewGuid();
            var product = Guid.NewGuid(); var category = Guid.NewGuid(); var brand = Guid.NewGuid();
            var unit = Guid.NewGuid(); var tag = $"P2R{Guid.NewGuid():N}"[..19];
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
            await using var command = new SqlCommand("""
                SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
                SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
                IF DB_NAME() NOT IN(N'WhatsBizERP_Phase2ReversalTest',N'WhatsBizERP_Phase3RefundTest',N'WhatsBizERP_PaidCancellationTest') THROW 51000,'Wrong disposable database',1;
                INSERT core.Tenants(TenantId,TenantKey,Name) VALUES(@tenant,@tag,@tag);
                INSERT master.ProductCategories(ProductCategoryId,CategoryCode,CategoryName,DisplayOrder) VALUES(@category,@tag,@tag,1);
                INSERT master.Brands(BrandId,BrandCode,BrandName) VALUES(@brand,@tag,@tag);
                INSERT master.UnitsOfMeasure(UnitId,UnitCode,UnitName,ShortName,DecimalPlaces) VALUES(@unit,@tag,@tag,N'ea',0);
                EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;
                INSERT sales.Customers(CustomerId,CustomerCode,CustomerName,CustomerType,TenantId)
                    VALUES(@customer,@tag,@tag,N'RETAIL',@tenant);
                INSERT inventory.Warehouses(WarehouseId,TenantId,WarehouseCode,WarehouseName,WarehouseTypeId,IsDefault)
                    SELECT @warehouse,@tenant,@tag,@tag,WarehouseTypeId,0 FROM inventory.WarehouseTypes WHERE TypeCode=N'GENERAL';
                INSERT master.Products(ProductId,ProductCode,ProductName,CategoryId,BrandId,UnitId,GSTPercentage,
                    PurchasePrice,SellingPrice,MRP,MinimumStock,MaximumStock,ReorderLevel,IsBatchManaged,IsSerialManaged,TenantId)
                    VALUES(@product,@tag,@tag,@category,@brand,@unit,0,10,1000,1000,0,1000,0,0,0,@tenant);
                INSERT inventory.InventoryBalances(ProductId,WarehouseId,TenantId,QuantityOnHand,AverageCost,LastPurchaseCost)
                    VALUES(@product,@warehouse,@tenant,10,10,10);
                """, connection);
            Add(command,"@tenant",tenant); Add(command,"@customer",customer); Add(command,"@warehouse",warehouse);
            Add(command,"@product",product); Add(command,"@category",category); Add(command,"@brand",brand);
            Add(command,"@unit",unit); Add(command,"@tag",tag);
            await command.ExecuteNonQueryAsync();
            return new(connectionString,tenant,customer,warehouse,product,tag);
        }

        public async Task<Guid> CreateCompletedSaleAsync(decimal price, decimal discount, decimal delivery,
            decimal taxPercent, string? paymentMethod, decimal payment)
        {
            await using var connection = await OpenAsync();
            var items = $"[{{\"ProductId\":\"{product}\",\"Quantity\":1,\"UnitPrice\":{price},\"DiscountPercentage\":0,\"DiscountAmount\":0,\"TaxPercentage\":{taxPercent}}}]";
            await using var post = new SqlCommand("sales.POS_PostInvoice",connection){ CommandType=CommandType.StoredProcedure };
            Add(post,"@WarehouseId",warehouse); Add(post,"@CustomerId",customer);
            Add(post,"@ItemsJson",items); Add(post,"@PaymentsJson","[]");
            Add(post,"@BillDiscount",discount); Add(post,"@PromotionDiscountAmount",discount);
            Add(post,"@DeliveryCharge",delivery); Add(post,"@Status","HELD");
            if (delivery>0) Add(post,"@ServicePincode","226001");
            Add(post,"@TenantId",tenant); Add(post,"@CreatedBy",tag);
            Guid invoice;
            await using (var reader = await post.ExecuteReaderAsync())
            { (await reader.ReadAsync()).Should().BeTrue(); invoice=reader.GetGuid(reader.GetOrdinal("InvoiceId")); }
            if (paymentMethod is not null)
            {
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                await new ErpPaymentPosting().ApplyAsync(connection,transaction,
                    new ErpPaymentPostRequest(tenant,invoice,paymentMethod,payment,$"P2-{Guid.NewGuid():N}",tag),default);
                await transaction.CommitAsync();
            }
            await using var complete = new SqlCommand("sales.POS_TransitionHeldInvoice",connection)
            { CommandType=CommandType.StoredProcedure };
            Add(complete,"@InvoiceId",invoice); Add(complete,"@Action","COMPLETE"); Add(complete,"@ModifiedBy",tag);
            await complete.ExecuteNonQueryAsync();
            return invoice;
        }

        public async Task<Guid> CreateHeldCodAsync(decimal amount)
        {
            await using var connection=await OpenAsync();
            var items=$"[{{\"ProductId\":\"{product}\",\"Quantity\":1,\"UnitPrice\":{amount},\"DiscountPercentage\":0,\"DiscountAmount\":0,\"TaxPercentage\":0}}]";
            await using var post=new SqlCommand("sales.POS_PostInvoice",connection){CommandType=CommandType.StoredProcedure};
            Add(post,"@WarehouseId",warehouse);Add(post,"@CustomerId",customer);
            Add(post,"@ItemsJson",items);Add(post,"@PaymentsJson","[]");
            Add(post,"@Status","HELD");Add(post,"@TenantId",tenant);Add(post,"@CreatedBy",tag);
            Guid invoice;
            await using(var reader=await post.ExecuteReaderAsync())
            { (await reader.ReadAsync()).Should().BeTrue();invoice=reader.GetGuid(reader.GetOrdinal("InvoiceId")); }
            await using var commerce=new SqlCommand("""
                INSERT integration.WhatsAppCommerceOrders(WhatsAppCommerceOrderId,TenantId,InvoiceId,
                    SourceChannel,ProviderMode) VALUES(NEWID(),@tenant,@invoice,N'STOREFRONT',N'MOCK');
                INSERT commerce.CommercePayments(PaymentId,TenantId,InvoiceId,Provider,PaymentMethod,
                    Amount,Currency,Status,AttemptNumber,TransactionReference)
                VALUES(NEWID(),@tenant,@invoice,N'COD',N'COD',@amount,N'INR',N'COD_PENDING',1,@tag+N'-COD');
                """,connection);
            Add(commerce,"@tenant",tenant);Add(commerce,"@invoice",invoice);Add(commerce,"@amount",amount);
            Add(commerce,"@tag",tag);
            await commerce.ExecuteNonQueryAsync();
            return invoice;
        }

        public async Task<Guid> CreateImmediateSaleAsync()
        {
            await using var connection=await OpenAsync();
            var items=$"[{{\"ProductId\":\"{product}\",\"Quantity\":1,\"UnitPrice\":500,\"DiscountPercentage\":0,\"DiscountAmount\":0,\"TaxPercentage\":0}}]";
            await using var post=new SqlCommand("sales.POS_PostInvoice",connection){CommandType=CommandType.StoredProcedure};
            Add(post,"@WarehouseId",warehouse); Add(post,"@CustomerId",customer);
            Add(post,"@ItemsJson",items); Add(post,"@PaymentsJson","[{\"MethodCode\":\"CASH\",\"Amount\":500}]");
            Add(post,"@Status","COMPLETED"); Add(post,"@TenantId",tenant); Add(post,"@CreatedBy",tag);
            await using var reader=await post.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return reader.GetGuid(reader.GetOrdinal("InvoiceId"));
        }

        public async Task<ErpFullSaleReversalResult> ReverseAsync(Guid invoice,Guid? actingTenant=null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                { ["ConnectionStrings:DefaultConnection"]=connectionString }).Build();
            return await new ErpSaleReversal(configuration).ReverseFullSaleAsync(
                new ErpFullSaleReversalRequest(actingTenant??tenant,invoice,"Synthetic Phase 2 full reversal",tag),default);
        }

        public async Task<Guid> SeedConfirmedRefundAsync(Guid invoice,string provider,decimal amount,bool confirmed=true)
        {
            var refund=Guid.NewGuid(); var payment=Guid.NewGuid();
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                INSERT commerce.CommercePayments(PaymentId,TenantId,InvoiceId,Provider,PaymentMethod,
                    Amount,Currency,Status,AttemptNumber,ProviderPaymentId,TransactionReference)
                VALUES(@payment,@tenant,@invoice,@provider,@provider,@amount,N'INR',N'PAID',1,
                    CASE WHEN @provider=N'RAZORPAY' THEN @tag+N'-PAY' ELSE NULL END,@tag+N'-PAYMENT');
                INSERT commerce.PaymentApplications(PaymentApplicationId,TenantId,PaymentId,InvoiceId,Amount,Currency)
                VALUES(NEWID(),@tenant,@payment,@invoice,@amount,N'INR');
                INSERT integration.WhatsAppCommerceOrders(WhatsAppCommerceOrderId,TenantId,InvoiceId,
                    SourceChannel,ProviderMode) VALUES(NEWID(),@tenant,@invoice,N'STOREFRONT',N'MOCK');
                INSERT commerce.StorefrontRefunds(RefundId,TenantId,InvoiceId,PaymentId,Provider,
                    RefundType,RefundAmount,RefundStatus,Reason)
                VALUES(@refund,@tenant,@invoice,@payment,@provider,N'FULL_CANCEL',@amount,
                    CASE WHEN @confirmed=1 THEN N'REFUND_PENDING' ELSE N'REFUND_REQUIRED' END,
                    N'Synthetic confirmed refund');
                IF @confirmed=1 INSERT commerce.StorefrontRefundAttempts(RefundAttemptId,TenantId,
                    RefundId,AttemptNumber,Status,ProviderRefundId,FinishedAt)
                VALUES(NEWID(),@tenant,@refund,1,N'CONFIRMED',
                    CASE WHEN @provider=N'RAZORPAY' THEN @tag+N'-REFUND' ELSE NULL END,SYSUTCDATETIME());
                """,connection);
            Add(command,"@payment",payment); Add(command,"@refund",refund); Add(command,"@tenant",tenant);
            Add(command,"@invoice",invoice); Add(command,"@provider",provider);
            Add(command,"@amount",amount); Add(command,"@tag",tag); Add(command,"@confirmed",confirmed);
            await command.ExecuteNonQueryAsync();
            return refund;
        }

        public async Task SeedCollectionForRefundAsync(Guid invoice,string provider,decimal amount)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                DECLARE @payment uniqueidentifier=NEWID();
                INSERT commerce.CommercePayments(PaymentId,TenantId,InvoiceId,Provider,PaymentMethod,
                    Amount,Currency,Status,AttemptNumber,ProviderPaymentId,TransactionReference)
                VALUES(@payment,@tenant,@invoice,@provider,@provider,@amount,N'INR',N'PAID',1,
                    @tag+N'-PAY',@tag+N'-PAYMENT');
                INSERT commerce.PaymentApplications(PaymentApplicationId,TenantId,PaymentId,InvoiceId,Amount,Currency)
                VALUES(NEWID(),@tenant,@payment,@invoice,@amount,N'INR');
                INSERT integration.WhatsAppCommerceOrders(WhatsAppCommerceOrderId,TenantId,InvoiceId,
                    SourceChannel,ProviderMode) VALUES(NEWID(),@tenant,@invoice,N'STOREFRONT',N'MOCK');
                """,connection);
            Add(command,"@tenant",tenant); Add(command,"@invoice",invoice);
            Add(command,"@provider",provider); Add(command,"@amount",amount); Add(command,"@tag",tag);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<Guid> CreateRetailerActorAsync()
        {
            var actor=Guid.NewGuid();
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                INSERT core.Users(Id,TenantId,UserName,NormalizedUserName)
                VALUES(@actor,@tenant,@tag,UPPER(@tag));
                """,connection);
            Add(command,"@actor",actor); Add(command,"@tenant",tenant); Add(command,"@tag",tag);
            await command.ExecuteNonQueryAsync();
            return actor;
        }

        public StorefrontCancellationService CancellationService()
        {
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                { ["ConnectionStrings:DefaultConnection"]=connectionString }).Build();
            return new StorefrontCancellationService(configuration,new TestCustomerSession(customer),new ErpSaleReversal(configuration),
                RefundService(),NullLogger<StorefrontCancellationService>.Instance);
        }

        public async Task SeedCancellationRequestAsync(Guid invoice)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                INSERT commerce.StorefrontCancellationRequests(CancellationRequestId,TenantId,InvoiceId,
                    CustomerId,Reason,Status)
                VALUES(NEWID(),@tenant,@invoice,@customer,N'Customer changed mind',N'REQUESTED');
                """,connection);
            Add(command,"@tenant",tenant);Add(command,"@invoice",invoice);Add(command,"@customer",customer);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<Guid> RefundIdAsync(Guid invoice)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT RefundId FROM commerce.StorefrontRefunds
                WHERE TenantId=@tenant AND InvoiceId=@invoice AND RefundType=N'FULL_CANCEL';
                """,connection);
            Add(command,"@tenant",tenant);Add(command,"@invoice",invoice);
            return (Guid)(await command.ExecuteScalarAsync()??throw new InvalidOperationException("Prepared refund missing."));
        }

        public async Task<(int RequestCount,int ReversalCount,int RefundCount,int RefundJournalCount,
            int BankOutCount,int AttemptCount,decimal RefundAmount,string ApprovalStatus,string RefundStatus)>
            CancellationSnapshotAsync(Guid invoice)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM commerce.StorefrontCancellationRequests WHERE TenantId=@tenant AND InvoiceId=@invoice),
                  (SELECT COUNT(*) FROM sales.FullSaleReversals WHERE TenantId=@tenant AND InvoiceId=@invoice),
                  (SELECT COUNT(*) FROM commerce.StorefrontRefunds WHERE TenantId=@tenant AND InvoiceId=@invoice),
                  (SELECT COUNT(*) FROM finance.JournalEntries j JOIN commerce.StorefrontRefunds r
                    ON r.RefundId=j.ReferenceId WHERE r.TenantId=@tenant AND r.InvoiceId=@invoice
                    AND j.ReferenceType=N'CUSTOMER' AND j.TransactionType=N'PAYMENT'),
                  (SELECT COUNT(*) FROM finance.BankBook b JOIN commerce.StorefrontRefunds r
                    ON r.RefundId=b.ReferenceId WHERE r.TenantId=@tenant AND r.InvoiceId=@invoice AND b.AmountOut>0),
                  (SELECT COUNT(*) FROM commerce.StorefrontRefundAttempts a JOIN commerce.StorefrontRefunds r
                    ON r.RefundId=a.RefundId WHERE r.TenantId=@tenant AND r.InvoiceId=@invoice),
                  ISNULL((SELECT SUM(RefundAmount) FROM commerce.StorefrontRefunds
                    WHERE TenantId=@tenant AND InvoiceId=@invoice),0),
                  (SELECT TOP(1) Status FROM commerce.StorefrontCancellationRequests
                    WHERE TenantId=@tenant AND InvoiceId=@invoice ORDER BY RequestedAt DESC),
                  ISNULL((SELECT TOP(1) RefundStatus FROM commerce.StorefrontRefunds
                    WHERE TenantId=@tenant AND InvoiceId=@invoice),N'NONE');
                """,connection);
            Add(command,"@tenant",tenant);Add(command,"@invoice",invoice);
            await using var reader=await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return (reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),
                reader.GetInt32(4),reader.GetInt32(5),reader.GetDecimal(6),reader.GetString(7),reader.GetString(8));
        }

        public async Task SetRefundFailureTriggerAsync(bool enabled)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand(enabled ? """
                EXEC(N'CREATE OR ALTER TRIGGER commerce.TR_Phase3RefundRollbackProbe
                  ON commerce.StorefrontRefunds AFTER UPDATE AS
                  BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE RefundStatus=N''REFUNDED'')
                    THROW 51990,N''Synthetic rollback probe'',1; END;');
                """ : "DROP TRIGGER IF EXISTS commerce.TR_Phase3RefundRollbackProbe;",connection);
            await command.ExecuteNonQueryAsync();
        }

        public CommerceRefundService RefundService()
        {
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                { ["ConnectionStrings:DefaultConnection"]=connectionString }).Build();
            return new CommerceRefundService(configuration,new EphemeralDataProtectionProvider(),
                new PaymentGatewayResolver(Array.Empty<IPaymentGateway>()),
                new ErpRefundSettlement(configuration));
        }

        public async Task<CommerceRefundService> RazorpayRefundServiceAsync(RefundGateway gateway)
        {
            var protection=new EphemeralDataProtectionProvider();
            var protectedSecret=protection.CreateProtector("WhatsBiz.Payments.Razorpay.Secrets.v1")
                .Protect("synthetic-test-only-secret");
            await using var connection=await OpenAsync();
            await using var insert=new SqlCommand("""
                INSERT commerce.TenantPaymentProviders(PaymentProviderId,TenantId,Provider,IsEnabled,
                    KeyId,KeySecretProtected,IsTestMode)
                VALUES(NEWID(),@tenant,N'RAZORPAY',1,N'synthetic-test-only-key',@secret,1);
                """,connection);
            Add(insert,"@tenant",tenant); Add(insert,"@secret",protectedSecret);
            await insert.ExecuteNonQueryAsync();
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                { ["ConnectionStrings:DefaultConnection"]=connectionString }).Build();
            return new CommerceRefundService(configuration,protection,
                new PaymentGatewayResolver([gateway]),new ErpRefundSettlement(configuration));
        }

        public async Task<string> ProviderPaymentIdAsync(Guid refund)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT p.ProviderPaymentId FROM commerce.StorefrontRefunds r
                JOIN commerce.CommercePayments p ON p.PaymentId=r.PaymentId
                WHERE r.TenantId=@tenant AND r.RefundId=@refund;
                """,connection);
            Add(command,"@tenant",tenant); Add(command,"@refund",refund);
            return (string)(await command.ExecuteScalarAsync()??throw new InvalidOperationException());
        }

        public async Task<ErpRefundSettlementResult> SettleRefundAsync(Guid refund,string mode,Guid? actingTenant=null)
        {
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                { ["ConnectionStrings:DefaultConnection"]=connectionString }).Build();
            return await new ErpRefundSettlement(configuration).SettleAsync(
                new(actingTenant??tenant,refund,mode,tag+"-OUT",DateTimeOffset.UtcNow,tag),default);
        }

        public async Task<string> AssertDetailedRefundAuditAsync(Guid invoice,Guid refund)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT N'SALE' Metric,COUNT(*) AS [RowCount],ISNULL(SUM(d.DebitTotal),0) Amount
                  FROM finance.JournalEntries j JOIN finance.DayBook d ON d.JournalEntryId=j.JournalEntryId
                  WHERE j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice
                UNION ALL SELECT N'SALE_PAYMENT',COUNT(*),ISNULL(SUM(d.DebitTotal),0)
                  FROM finance.JournalEntries j JOIN finance.DayBook d ON d.JournalEntryId=j.JournalEntryId
                  WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN
                    (SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                UNION ALL SELECT N'SALE_REVERSAL',COUNT(*),ISNULL(SUM(d.DebitTotal),0)
                  FROM finance.JournalEntries j JOIN finance.DayBook d ON d.JournalEntryId=j.JournalEntryId
                  JOIN sales.FullSaleReversals v ON v.ReversalId=j.ReferenceId
                  WHERE j.ReferenceType=N'SALE_REVERSAL' AND v.InvoiceId=@invoice
                UNION ALL SELECT N'REFUND_JOURNAL',COUNT(*),ISNULL(SUM(d.DebitTotal),0)
                  FROM finance.JournalEntries j JOIN finance.DayBook d ON d.JournalEntryId=j.JournalEntryId
                  WHERE j.ReferenceType=N'CUSTOMER' AND j.ReferenceId=@refund AND j.TransactionType=N'PAYMENT'
                UNION ALL SELECT N'SALES_PAYMENT_ROWS',COUNT(*),ISNULL(SUM(Amount),0)
                  FROM sales.SalesPayments WHERE InvoiceId=@invoice
                UNION ALL SELECT N'PAYMENT_APPLICATIONS',COUNT(*),ISNULL(SUM(Amount),0)
                  FROM commerce.PaymentApplications WHERE InvoiceId=@invoice
                UNION ALL SELECT N'COMMERCE_PAID',COUNT(*),ISNULL(SUM(Amount),0)
                  FROM commerce.CommercePayments WHERE InvoiceId=@invoice AND Status=N'PAID'
                UNION ALL SELECT N'CUSTOMER_RECEIPT',COUNT(*),ISNULL(SUM(l.CreditAmount),0)
                  FROM finance.CustomerLedger l JOIN finance.JournalEntries j ON j.JournalEntryId=l.JournalEntryId
                  WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN
                    (SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                UNION ALL SELECT N'CUSTOMER_REFUND',COUNT(*),ISNULL(SUM(DebitAmount),0)
                  FROM finance.CustomerLedger WHERE ReferenceId=@refund AND DebitAmount>0
                UNION ALL SELECT N'BANK_IN',COUNT(*),ISNULL(SUM(b.AmountIn),0)
                  FROM finance.BankBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId
                  WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN
                    (SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice) AND b.AmountIn>0
                UNION ALL SELECT N'BANK_OUT',COUNT(*),ISNULL(SUM(AmountOut),0)
                  FROM finance.BankBook WHERE ReferenceId=@refund AND AmountOut>0
                UNION ALL SELECT N'CASH_OUT',COUNT(*),ISNULL(SUM(AmountOut),0)
                  FROM finance.CashBook WHERE ReferenceId=@refund AND AmountOut>0
                UNION ALL SELECT N'INVENTORY_SALE',COUNT(*),ISNULL(SUM(d.Quantity),0)
                  FROM inventory.InventoryTransactions t JOIN inventory.InventoryTransactionDetails d
                    ON d.TransactionId=t.TransactionId
                  WHERE t.ReferenceId=@invoice AND t.TransactionType=N'SALE'
                UNION ALL SELECT N'INVENTORY_RESTORE',COUNT(*),ISNULL(SUM(d.Quantity),0)
                  FROM inventory.InventoryTransactions t JOIN inventory.InventoryTransactionDetails d
                    ON d.TransactionId=t.TransactionId JOIN sales.FullSaleReversals v ON v.ReversalId=t.ReferenceId
                  WHERE v.InvoiceId=@invoice AND t.TransactionType=N'SALE_REVERSAL'
                UNION ALL SELECT N'REFUND_ROW',COUNT(*),ISNULL(SUM(RefundAmount),0)
                  FROM commerce.StorefrontRefunds WHERE RefundId=@refund AND RefundStatus=N'REFUNDED'
                UNION ALL SELECT N'CONFIRMED_ATTEMPT',COUNT(*),CAST(0 AS decimal(18,2))
                  FROM commerce.StorefrontRefundAttempts WHERE RefundId=@refund AND Status=N'CONFIRMED'
                    AND ProviderRefundId IS NOT NULL
                UNION ALL SELECT N'REFUND_DEBIT_LINE',COUNT(*),ISNULL(SUM(d.DebitAmount),0)
                  FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j
                    ON j.JournalEntryId=d.JournalEntryId
                  WHERE j.ReferenceId=@refund AND j.ReferenceType=N'CUSTOMER' AND d.DebitAmount>0
                UNION ALL SELECT N'REFUND_CREDIT_LINE',COUNT(*),ISNULL(SUM(d.CreditAmount),0)
                  FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j
                    ON j.JournalEntryId=d.JournalEntryId
                  WHERE j.ReferenceId=@refund AND j.ReferenceType=N'CUSTOMER' AND d.CreditAmount>0
                UNION ALL SELECT N'INVOICE_GRAND',COUNT(*),ISNULL(SUM(GrandTotal),0)
                  FROM sales.SalesInvoices WHERE InvoiceId=@invoice AND Status=N'CANCELLED'
                UNION ALL SELECT N'INVOICE_PAID',COUNT(*),ISNULL(SUM(PaidAmount),0)
                  FROM sales.SalesInvoices WHERE InvoiceId=@invoice
                UNION ALL SELECT N'INVOICE_BALANCE',COUNT(*),ISNULL(SUM(BalanceAmount),0)
                  FROM sales.SalesInvoices WHERE InvoiceId=@invoice;
                """,connection);
            Add(command,"@invoice",invoice);Add(command,"@refund",refund);
            var values=new Dictionary<string,(int Count,decimal Amount)>(StringComparer.Ordinal);
            await using var reader=await command.ExecuteReaderAsync();
            while(await reader.ReadAsync()) values[reader.GetString(0)]=(reader.GetInt32(1),reader.GetDecimal(2));
            foreach(var metric in new[]{"SALE","SALE_PAYMENT","SALE_REVERSAL","REFUND_JOURNAL",
                "SALES_PAYMENT_ROWS","PAYMENT_APPLICATIONS","COMMERCE_PAID","CUSTOMER_RECEIPT",
                "CUSTOMER_REFUND","BANK_IN","BANK_OUT","REFUND_ROW","REFUND_DEBIT_LINE",
                "REFUND_CREDIT_LINE","INVOICE_GRAND","INVOICE_PAID"})
                values[metric].Should().Be((1,990m),metric);
            values["CASH_OUT"].Should().Be((0,0m));
            values["CONFIRMED_ATTEMPT"].Should().Be((1,0m));
            values["INVENTORY_SALE"].Should().Be((1,1m));
            values["INVENTORY_RESTORE"].Should().Be((1,1m));
            values["INVOICE_BALANCE"].Should().Be((1,0m));
            return string.Join("; ",values.Select(x=>$"{x.Key}={x.Value.Count}x/{x.Value.Amount}"));
        }

        public async Task<(int SaleCount,int PaymentCount,int RefundJournalCount,decimal CustomerDebit,
            int BankOutCount,decimal BankOutAmount,int CashOutCount,int StockSaleCount,int StockRestoreCount,
            string RefundStatus,string ObligationStatus)> RefundSnapshotAsync(Guid invoice,Guid refund)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT
                (SELECT COUNT(*) FROM finance.JournalEntries WHERE ReferenceType=N'SALE' AND ReferenceId=@invoice),
                (SELECT COUNT(*) FROM finance.JournalEntries WHERE ReferenceType=N'SALE_PAYMENT'
                    AND ReferenceId IN(SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)),
                (SELECT COUNT(*) FROM finance.JournalEntries WHERE ReferenceType=N'CUSTOMER'
                    AND ReferenceId=@refund AND TransactionType=N'PAYMENT'),
                (SELECT ISNULL(SUM(DebitAmount),0) FROM finance.CustomerLedger WHERE ReferenceId=@refund),
                (SELECT COUNT(*) FROM finance.BankBook WHERE ReferenceId=@refund AND AmountOut>0),
                (SELECT ISNULL(SUM(AmountOut),0) FROM finance.BankBook WHERE ReferenceId=@refund),
                (SELECT COUNT(*) FROM finance.CashBook WHERE ReferenceId=@refund AND AmountOut>0),
                (SELECT COUNT(*) FROM inventory.InventoryTransactions WHERE ReferenceId=@invoice AND TransactionType=N'SALE'),
                (SELECT COUNT(*) FROM inventory.InventoryTransactions t JOIN sales.FullSaleReversals r
                    ON r.ReversalId=t.ReferenceId WHERE r.InvoiceId=@invoice AND t.TransactionType=N'SALE_REVERSAL'),
                (SELECT RefundStatus FROM commerce.StorefrontRefunds WHERE RefundId=@refund),
                (SELECT RefundStatus FROM sales.FullSaleReversals WHERE InvoiceId=@invoice)
                """,connection);
            Add(command,"@invoice",invoice); Add(command,"@refund",refund);
            await using var reader=await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return (reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetDecimal(3),
                reader.GetInt32(4),reader.GetDecimal(5),reader.GetInt32(6),reader.GetInt32(7),
                reader.GetInt32(8),reader.GetString(9),reader.GetString(10));
        }

        public async Task MarkReturnActivityAsync(Guid invoice)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                INSERT sales.SalesInvoiceReturns(ReturnId,ReturnNumber,InvoiceId,InvoiceItemId,
                    Quantity,RefundAmount,Reason,Status,CreatedBy)
                SELECT NEWID(),@tag+N'-RETURN',@invoice,InvoiceItemId,1,1,N'Synthetic prior return',N'COMPLETED',@tag
                FROM sales.SalesInvoiceItems WHERE InvoiceId=@invoice
                """,connection);
            Add(command,"@invoice",invoice); Add(command,"@tag",tag); await command.ExecuteNonQueryAsync();
        }

        public async Task RemoveStockBalanceAsync()
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("DELETE inventory.InventoryBalances WHERE WarehouseId=@warehouse AND ProductId=@product",connection);
            Add(command,"@warehouse",warehouse); Add(command,"@product",product);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<DetailCounts> DetailCountsAsync(Guid invoice)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT
                (SELECT COUNT(*) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    WHERE j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice),
                (SELECT COUNT(*) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    JOIN sales.FullSaleReversals r ON r.ReversalId=j.ReferenceId WHERE j.ReferenceType=N'SALE_REVERSAL' AND r.InvoiceId=@invoice),
                (SELECT COUNT(*) FROM inventory.InventoryTransactionDetails d JOIN inventory.InventoryTransactions t ON t.TransactionId=d.TransactionId
                    WHERE t.TransactionType=N'SALE' AND t.ReferenceId=@invoice),
                (SELECT COUNT(*) FROM inventory.InventoryTransactionDetails d JOIN inventory.InventoryTransactions t ON t.TransactionId=d.TransactionId
                    JOIN sales.FullSaleReversals r ON r.ReversalId=t.ReferenceId WHERE t.TransactionType=N'SALE_REVERSAL' AND r.InvoiceId=@invoice),
                (SELECT COUNT(*) FROM finance.CustomerLedger l JOIN sales.FullSaleReversals r ON r.ReversalJournalId=l.JournalEntryId WHERE r.InvoiceId=@invoice),
                (SELECT ISNULL(SUM(l.CreditAmount),0) FROM finance.CustomerLedger l JOIN sales.FullSaleReversals r ON r.ReversalJournalId=l.JournalEntryId WHERE r.InvoiceId=@invoice),
                (SELECT ISNULL(SUM(Amount),0) FROM sales.SalesPayments WHERE InvoiceId=@invoice),
                (SELECT RefundStatus FROM sales.FullSaleReversals WHERE InvoiceId=@invoice)
                """,connection);
            Add(command,"@invoice",invoice);
            await using var reader=await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return new(reader.GetInt32(0),reader.GetInt32(1),reader.GetInt32(2),reader.GetInt32(3),
                reader.GetInt32(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetString(7));
        }

        public async Task AddDeliveryAsync(Guid invoice,string status)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                INSERT commerce.OrderDeliveries(OrderDeliveryId,TenantId,OrderId,DeliveryStatus,
                    CustomerNameSnapshot,DeliveryAddressSnapshot)
                VALUES(NEWID(),@tenant,@invoice,@status,N'Synthetic customer',N'Test-only address')
                """,connection);
            Add(command,"@tenant",tenant); Add(command,"@invoice",invoice); Add(command,"@status",status);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<(string Status,int ReversalEvents)> DeliverySnapshotAsync(Guid invoice)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT d.DeliveryStatus,(SELECT COUNT(*) FROM commerce.OrderDeliveryEvents e
                    WHERE e.OrderDeliveryId=d.OrderDeliveryId AND e.EventType=N'SALE_REVERSAL')
                FROM commerce.OrderDeliveries d WHERE d.OrderId=@invoice AND d.TenantId=@tenant
                """,connection);
            Add(command,"@invoice",invoice); Add(command,"@tenant",tenant);
            await using var reader=await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return (reader.GetString(0),reader.GetInt32(1));
        }

        public async Task<Snapshot> SnapshotAsync(Guid invoice)
        {
            await using var connection=await OpenAsync();
            await using var command=new SqlCommand("""
                SELECT i.Status,i.GrandTotal,i.PaidAmount,
                (SELECT COUNT(*) FROM finance.JournalEntries j WHERE j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice),
                (SELECT COUNT(*) FROM finance.JournalEntries j WHERE j.ReferenceType=N'SALE_PAYMENT' AND j.ReferenceId IN
                    (SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)),
                (SELECT COUNT(*) FROM finance.JournalEntries j JOIN sales.FullSaleReversals r ON r.ReversalId=j.ReferenceId
                    WHERE r.InvoiceId=@invoice AND j.ReferenceType=N'SALE_REVERSAL'),
                (SELECT COUNT(*) FROM sales.FullSaleReversals WHERE InvoiceId=@invoice),
                (SELECT ISNULL(SUM(Amount),0) FROM sales.SalesPayments WHERE InvoiceId=@invoice),
                (SELECT COUNT(*) FROM finance.CustomerLedger l JOIN finance.JournalEntries j ON j.JournalEntryId=l.JournalEntryId
                    WHERE l.EntryType=N'RECEIPT' AND (j.ReferenceId IN (SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                        OR j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice)),
                (SELECT ISNULL(SUM(l.CreditAmount),0) FROM finance.CustomerLedger l JOIN finance.JournalEntries j ON j.JournalEntryId=l.JournalEntryId
                    WHERE l.EntryType=N'RECEIPT' AND (j.ReferenceId IN (SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                        OR j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice)),
                (SELECT COUNT(*) FROM finance.BankBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId
                    WHERE b.AmountIn>0 AND (j.ReferenceId IN(SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                        OR j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice)),
                (SELECT ISNULL(SUM(b.AmountIn),0) FROM finance.BankBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId
                    WHERE j.ReferenceId IN(SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                        OR j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice),
                (SELECT COUNT(*) FROM finance.BankBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId
                    WHERE b.AmountOut>0 AND j.ReferenceId IN(SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice)),
                (SELECT COUNT(*) FROM finance.CashBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId
                    WHERE b.AmountIn>0 AND (j.ReferenceId IN(SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                        OR j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice)),
                (SELECT ISNULL(SUM(b.AmountIn),0) FROM finance.CashBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId
                    WHERE j.ReferenceId IN(SELECT PaymentId FROM sales.SalesPayments WHERE InvoiceId=@invoice)
                        OR j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice),
                (SELECT COUNT(*) FROM finance.CashBook b JOIN finance.JournalEntries j ON j.JournalEntryId=b.JournalEntryId
                    WHERE b.AmountOut>0 AND j.ReferenceId IN(SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice)),
                (SELECT COUNT(*) FROM inventory.InventoryTransactions WHERE TransactionType=N'SALE' AND ReferenceId=@invoice),
                (SELECT COUNT(*) FROM inventory.InventoryTransactions t JOIN sales.FullSaleReversals r ON r.ReversalId=t.ReferenceId
                    WHERE t.TransactionType=N'SALE_REVERSAL' AND r.InvoiceId=@invoice),
                ISNULL((SELECT QuantityOnHand FROM inventory.InventoryBalances WHERE ProductId=@product AND WarehouseId=@warehouse),0),
                (SELECT ISNULL(SUM(d.DebitAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE_REVERSAL' AND j.ReferenceId IN
                    (SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice) AND a.AccountCode=N'SALES'),
                (SELECT ISNULL(SUM(d.DebitAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE_REVERSAL' AND j.ReferenceId IN
                    (SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice) AND a.AccountCode=N'DELIVERY_CLEARING'),
                (SELECT ISNULL(SUM(d.CreditAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE_REVERSAL' AND j.ReferenceId IN
                    (SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice) AND a.AccountCode=N'CUSTOMER'),
                (SELECT ISNULL(SUM(d.CreditAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE' AND j.ReferenceId=@invoice AND a.AccountCode=N'OUTPUT_GST'),
                (SELECT ISNULL(SUM(d.DebitAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    JOIN finance.Accounts a ON a.AccountId=d.AccountId WHERE j.ReferenceType=N'SALE_REVERSAL' AND j.ReferenceId IN
                    (SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice) AND a.AccountCode=N'OUTPUT_GST'),
                (SELECT COUNT(*) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    WHERE j.ReferenceType=N'SALE_REVERSAL' AND j.ReferenceId IN(SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice)),
                (SELECT ISNULL(SUM(d.DebitAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    WHERE j.ReferenceType=N'SALE_REVERSAL' AND j.ReferenceId IN(SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice)),
                (SELECT ISNULL(SUM(d.CreditAmount),0) FROM finance.JournalEntryDetails d JOIN finance.JournalEntries j ON j.JournalEntryId=d.JournalEntryId
                    WHERE j.ReferenceType=N'SALE_REVERSAL' AND j.ReferenceId IN(SELECT ReversalId FROM sales.FullSaleReversals WHERE InvoiceId=@invoice)),
                (SELECT ISNULL(SUM(RefundRequiredAmount),0) FROM sales.FullSaleReversals WHERE InvoiceId=@invoice),
                (SELECT COUNT(*) FROM commerce.StorefrontRefunds WHERE InvoiceId=@invoice)
                FROM sales.SalesInvoices i WHERE i.InvoiceId=@invoice
                """,connection);
            Add(command,"@invoice",invoice); Add(command,"@product",product); Add(command,"@warehouse",warehouse);
            await using var reader=await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return new(reader.GetString(0),reader.GetDecimal(1),reader.GetDecimal(2),
                reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5),reader.GetInt32(6),reader.GetDecimal(7),
                reader.GetInt32(8),reader.GetDecimal(9),reader.GetInt32(10),reader.GetDecimal(11),reader.GetInt32(12),
                reader.GetInt32(13),reader.GetDecimal(14),reader.GetInt32(15),reader.GetInt32(16),reader.GetInt32(17),
                reader.GetDecimal(18),reader.GetDecimal(19),reader.GetDecimal(20),reader.GetDecimal(21),
                reader.GetDecimal(22),reader.GetDecimal(23),reader.GetInt32(24),reader.GetDecimal(25),
                reader.GetDecimal(26),reader.GetDecimal(27),reader.GetInt32(28));
        }

        private async Task<SqlConnection> OpenAsync()
        {
            var connection=new SqlConnection(connectionString);
            await connection.OpenAsync();
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
            await using var context=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant",connection);
            Add(context,"@tenant",tenant); await context.ExecuteNonQueryAsync();
            return connection;
        }
    }

    private static void Add(SqlCommand command,string name,object value)=>command.Parameters.AddWithValue(name,value);

    private sealed class TestCustomerSession(Guid customerId) : IStorefrontCustomerService
    {
        public Task<StorefrontCustomerDto?> GetSessionAsync(string storeKey,string sessionToken,CancellationToken token)
            => Task.FromResult<StorefrontCustomerDto?>(sessionToken=="synthetic-session"
                ? new StorefrontCustomerDto(customerId,"Synthetic customer",null,null) : null);
        public Task<string> IssueSessionAsync(string storeKey,Guid tenantId,Guid id,CancellationToken token)
            => throw new NotSupportedException();
        public Task<IReadOnlyCollection<StorefrontCustomerOrderDto>?> GetOrdersAsync(string storeKey,string sessionToken,CancellationToken token)
            => throw new NotSupportedException();
        public Task<StorefrontCustomerOrderDto?> GetOrderAsync(string storeKey,string sessionToken,Guid orderId,CancellationToken token)
            => throw new NotSupportedException();
        public Task<IReadOnlyCollection<StorefrontProductDto>?> GetWishlistAsync(string storeKey,string sessionToken,CancellationToken token)
            => throw new NotSupportedException();
        public Task<bool> AddWishlistAsync(string storeKey,string sessionToken,Guid productId,CancellationToken token)
            => throw new NotSupportedException();
        public Task<bool> RemoveWishlistAsync(string storeKey,string sessionToken,Guid productId,CancellationToken token)
            => throw new NotSupportedException();
        public Task<bool> MergeWishlistAsync(string storeKey,string sessionToken,IReadOnlyCollection<Guid> productIds,CancellationToken token)
            => throw new NotSupportedException();
        public Task<IReadOnlyCollection<StorefrontCustomerAddressDto>?> GetAddressesAsync(string storeKey,string sessionToken,CancellationToken token)
            => throw new NotSupportedException();
        public Task<StorefrontCustomerAddressDto?> SaveAddressAsync(string storeKey,string sessionToken,Guid? addressId,StorefrontCustomerAddressInput input,CancellationToken token)
            => throw new NotSupportedException();
        public Task<bool> DeleteAddressAsync(string storeKey,string sessionToken,Guid addressId,CancellationToken token)
            => throw new NotSupportedException();
        public Task<bool> SetDefaultAddressAsync(string storeKey,string sessionToken,Guid addressId,CancellationToken token)
            => throw new NotSupportedException();
        public Task<StorefrontCustomerDto?> UploadProfileImageAsync(string storeKey,string sessionToken,string fileName,Stream content,CancellationToken token)
            => throw new NotSupportedException();
        public Task<StorefrontCustomerDto?> RemoveProfileImageAsync(string storeKey,string sessionToken,CancellationToken token)
            => throw new NotSupportedException();
        public Task<StorefrontImage?> GetProfileImageAsync(string storeKey,string sessionToken,CancellationToken token)
            => throw new NotSupportedException();
    }

    private sealed record Snapshot(string Status,decimal GrandTotal,decimal PaidAmount,
        int SaleJournals,int PaymentJournals,int ReversalJournals,int ReversalRows,decimal SalePaymentAmount,
        int CustomerReceiptCount,decimal CustomerReceiptAmount,int BankInCount,decimal BankInAmount,int BankOutCount,
        int CashInCount,decimal CashInAmount,int CashOutCount,int StockSaleCount,int StockRestorationCount,
        decimal StockQuantity,decimal SalesDebit,decimal DeliveryDebit,decimal CustomerCredit,
        decimal SaleGstCredit,decimal ReversalGstDebit,int ReversalDetailCount,decimal ReversalDebit,
        decimal ReversalCredit,decimal RefundRequiredAmount,int RefundRows);
    private sealed record DetailCounts(int OriginalSaleLines,int ReversalLines,int OriginalStockLines,
        int RestorationLines,int ReversalCustomerLedgerCount,decimal ReversalCustomerLedgerCredit,
        decimal SalePaymentAmount,string RefundStatus);

    private sealed class RefundGateway(Guid refundId,decimal amount,bool unknownOnCreate=true,
        string firstStatus="CREATED") : IPaymentGateway
    {
        public string Provider=>PaymentProviders.Razorpay;
        public string RefundProviderId=>"synthetic-"+refundId.ToString("N");
        public int CreateCalls {get;private set;}
        public string ProviderPaymentId {get;set;}="";
        public Task<GatewayCreateResult> CreatePaymentAsync(PaymentGatewayConfiguration config,
            GatewayCreateRequest request,CancellationToken token) => throw new NotSupportedException();
        public Task<GatewayStatusResult> GetPaymentStatusAsync(PaymentGatewayConfiguration config,
            string reference,CancellationToken token) => throw new NotSupportedException();
        public GatewayWebhookResult VerifyWebhook(PaymentGatewayConfiguration config,
            ReadOnlyMemory<byte> body,string signature,string? eventId)
            => new(signature=="test-valid-signature",eventId,"refund.processed",null,null,null,
                null,null,false,false);
        public Task<GatewayRefundResult> CreateRefundAsync(PaymentGatewayConfiguration config,
            string paymentId,decimal value,string currency,Guid id,Guid attemptId,CancellationToken token)
        {
            CreateCalls++;
            id.Should().Be(refundId); attemptId.Should().NotBeEmpty(); value.Should().Be(amount);
            ProviderPaymentId=paymentId;
            if(unknownOnCreate) throw new HttpRequestException("Synthetic unknown provider outcome");
            var status=firstStatus=="FAILED" ? (CreateCalls==1?"FAILED":"PROCESSED") : firstStatus;
            var providerId=firstStatus=="FAILED" ? RefundProviderId+"-"+CreateCalls : RefundProviderId;
            return Task.FromResult(new GatewayRefundResult(providerId,status,amount,"INR",paymentId));
        }
        public Task<GatewayRefundResult?> FindRefundAsync(PaymentGatewayConfiguration config,
            string paymentId,string? providerRefundId,Guid id,Guid attemptId,CancellationToken token)
        {
            paymentId.Should().Be(ProviderPaymentId);
            id.Should().Be(refundId); attemptId.Should().NotBeEmpty();
            return Task.FromResult<GatewayRefundResult?>(new GatewayRefundResult(
                RefundProviderId,"PROCESSED",amount,"INR",paymentId));
        }
    }
}

[CollectionDefinition("SQL phase 2 full sale reversal", DisableParallelization = true)]
public sealed class Phase2FullSaleReversalCollectionDefinition;
