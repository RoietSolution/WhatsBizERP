using FluentAssertions;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.POS;

namespace WhatsBiz.Tests.POS;

public sealed class CashierDiscountPolicyTests
{
    [Theory]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    public void DiscountAtOrBelowConfiguredLimitPasses(decimal discountPercent, decimal limit)
    {
        var action = () => CashierDiscountPolicy.Enforce(Input(discountPercent), limit);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(5.01, 5)]
    [InlineData(10, 5)]
    [InlineData(10.01, 10)]
    public void DiscountAboveConfiguredLimitIsRejected(decimal discountPercent, decimal limit)
    {
        var action = () => CashierDiscountPolicy.Enforce(Input(discountPercent), limit);

        action.Should().Throw<BusinessRuleException>();
    }

    private static POSInvoiceInput Input(decimal discountPercent) =>
        new(
            null,
            null,
            null,
            Guid.NewGuid(),
            null,
            [new POSItemInput(Guid.NewGuid(), null, 1, 100, discountPercent, 0, 0)],
            [],
            0,
            0,
            null,
            false,
            null
        );
}
