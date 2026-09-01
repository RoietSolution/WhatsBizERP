using FluentAssertions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.POS;
using WhatsBiz.Domain.POS;

namespace WhatsBiz.Tests.POS;

public sealed class POSSettlementPolicyTests
{
    private static readonly IReadOnlyCollection<PaymentMethod> Methods =
    [
        Method("CASH", false), Method("UPI", true), Method("CARD", true),
        Method("WALLET", true), Method("CREDIT", false)
    ];

    [Theory]
    [InlineData("UPI")]
    [InlineData("CARD")]
    [InlineData("WALLET")]
    public void ElectronicPaymentsRequireTransactionReference(string method)
    {
        var action = () => POSSettlementPolicy.ValidateInvoice(Input([new(method, 118, null)]), "COMPLETED", Methods);

        action.Should().Throw<BusinessRuleException>().WithMessage("*reference is required*");
    }

    [Theory]
    [InlineData("UPI")]
    [InlineData("CARD")]
    [InlineData("WALLET")]
    public void ElectronicPaymentsAreAcceptedWithReference(string method)
    {
        var action = () => POSSettlementPolicy.ValidateInvoice(Input([new(method, 118, "TXN-123")]), "COMPLETED", Methods);

        action.Should().NotThrow();
    }

    [Fact]
    public void CreditSaleRequiresCustomerAndIsNotRecordedAsPayment()
    {
        var noCustomer = () => POSSettlementPolicy.ValidateInvoice(Input([], true), "COMPLETED", Methods);
        var creditPayment = () => POSSettlementPolicy.ValidateInvoice(Input([new("CREDIT", 118, null)]), "COMPLETED", Methods);

        noCustomer.Should().Throw<BusinessRuleException>().WithMessage("*Select a customer*");
        creditPayment.Should().Throw<BusinessRuleException>().WithMessage("*not a received payment*");
        POSSettlementPolicy.ValidateInvoice(Input([], true, Guid.NewGuid()), "COMPLETED", Methods);
    }

    [Fact]
    public void NonCreditSaleMustBePaidInFull()
    {
        var action = () => POSSettlementPolicy.ValidateInvoice(Input([new("CASH", 100, null)]), "COMPLETED", Methods);

        action.Should().Throw<BusinessRuleException>().WithMessage("*full invoice total*");
    }

    [Fact]
    public void HeldBillCannotContainPayment()
    {
        var action = () => POSSettlementPolicy.ValidateInvoice(Input([new("CASH", 118, null)]), "HELD", Methods);

        action.Should().Throw<BusinessRuleException>().WithMessage("*held bill cannot contain payments*");
    }

    private static POSInvoiceInput Input(IReadOnlyCollection<POSPaymentInput> payments, bool credit = false, Guid? customerId = null) =>
        new(null, null, customerId, Guid.NewGuid(), null,
            [new(Guid.NewGuid(), null, 1, 100, 0, 0, 18)], payments, 0, 0, null, false, null, credit);

    private static PaymentMethod Method(string code, bool reference) =>
        new() { PaymentMethodId = Guid.NewGuid(), MethodCode = code, MethodName = code, RequiresReference = reference, IsActive = true };
}
