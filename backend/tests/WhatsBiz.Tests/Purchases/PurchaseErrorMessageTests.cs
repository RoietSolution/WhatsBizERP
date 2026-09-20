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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingSupplierInvoiceIsNormalizedToNull(string? value)
    {
        PurchaseSupplierInvoiceRules.Normalize(value).Should().BeNull();
    }

    [Fact]
    public void MeaningfulSupplierInvoiceIsTrimmedAndDuplicateIndexIsIdentified()
    {
        PurchaseSupplierInvoiceRules.Normalize("  INV-1001  ").Should().Be("INV-1001");
        PurchaseSupplierInvoiceRules.IsDuplicateSupplierInvoiceError(2601, "UX_PurchaseInvoices_SupplierInvoice").Should().BeTrue();
        PurchaseSupplierInvoiceRules.IsDuplicateSupplierInvoiceError(2601, "another unique index").Should().BeFalse();
        PurchaseSupplierInvoiceRules.IsDuplicateSupplierInvoiceError(547, "UX_PurchaseInvoices_SupplierInvoice").Should().BeFalse();
    }
}
