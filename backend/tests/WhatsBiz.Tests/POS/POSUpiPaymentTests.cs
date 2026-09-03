using FluentAssertions;
using WhatsBiz.Application.Features.POS;

namespace WhatsBiz.Tests.POS;

public sealed class POSUpiPaymentTests
{
    [Fact]
    public void BuildsAmountBoundUpiPayUriUsingInvariantFormatting()
    {
        var uri = POSUpiPayment.BuildUri("retailer@upi", "Retail Shop & Sons", 1234.5m);

        uri.Should().Be("upi://pay?pa=retailer%40upi&pn=Retail%20Shop%20%26%20Sons&am=1234.50&cu=INR");
    }

    [Theory]
    [InlineData("retailer@upi", true)]
    [InlineData("shop.name-1@bank", true)]
    [InlineData("not-a-vpa", false)]
    [InlineData("https://example.com", false)]
    public void AcceptsOnlyUpiVirtualPaymentAddresses(string value, bool expected)
    {
        POSUpiPayment.IsValidUpiId(value).Should().Be(expected);
    }
}
