using FluentAssertions;
using WhatsBiz.Infrastructure.Purchases;

namespace WhatsBiz.Tests.Purchases;

public sealed class PurchaseErrorMessageTests
{
    [Fact]
    public void NormalPurchaseIntegrityFailureIsNotDescribedAsReturn()
    {
        PurchaseErrorMessages.PostIntegrity.Should().Contain("purchase could not be posted");
        PurchaseErrorMessages.PostIntegrity.Should().NotContain("purchase return");
    }

    [Fact]
    public void PurchaseReturnAndPaymentKeepOperationSpecificMessages()
    {
        PurchaseErrorMessages.ReturnIntegrity.Should().Contain("purchase return");
        PurchaseErrorMessages.PaymentIntegrity.Should().Contain("purchase payment");
        PurchaseErrorMessages.ReturnIntegrity.Should().NotBe(PurchaseErrorMessages.PostIntegrity);
    }
}
