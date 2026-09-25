using FluentAssertions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.POS;

namespace WhatsBiz.Tests.POS;

public sealed class POSPrintBridgePolicyTests
{
    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("PARTIALLY_RETURNED")]
    [InlineData("RETURNED")]
    [InlineData("HELD")]
    [InlineData("SUSPENDED")]
    public void SupportedInvoiceStatusesCanUseThePrintBridge(string status)
    {
        var action = () => POSPrintBridgePolicy.EnsurePrintable(status);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("CANCELLED")]
    [InlineData("VOID")]
    public void NonPrintableInvoiceStatusesRemainRejected(string status)
    {
        var action = () => POSPrintBridgePolicy.EnsurePrintable(status);

        action.Should().Throw<BusinessRuleException>()
            .WithMessage("Only finalized, held, or suspended invoices can be printed from the bridge.");
    }
}
