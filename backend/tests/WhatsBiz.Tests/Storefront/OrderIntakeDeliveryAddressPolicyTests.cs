using FluentAssertions;
using WhatsBiz.Application.Features.OrderIntake;

namespace WhatsBiz.Tests.Storefront;

public sealed class OrderIntakeDeliveryAddressPolicyTests
{
    private static OrderIntakeDeliveryAddressInput Valid(string pincode = "208001") =>
        new("HOME_DELIVERY", "A Customer", "9876543210", "House 12, Main Road", "Civil Lines", "Kanpur", "Uttar Pradesh", pincode);

    [Fact]
    public void ValidAddressIsTrimmedAndSnapshotted()
    {
        var result = OrderIntakeDeliveryAddressPolicy.Normalize(Valid() with { RecipientName = " A Customer ", DeliveryInstructions = " Call on arrival " });
        result.RecipientName.Should().Be("A Customer");
        result.Pincode.Should().Be("208001");
        result.DeliveryInstructions.Should().Be("Call on arrival");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingRequiredAddressFieldsAreRejected(string? value)
    {
        var act = () => OrderIntakeDeliveryAddressPolicy.Normalize(Valid() with { HouseStreet = value });
        act.Should().Throw<ArgumentException>().WithMessage("House / street*");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345A")]
    [InlineData("1234567")]
    public void InvalidPinCodeIsRejected(string pincode)
    {
        var act = () => OrderIntakeDeliveryAddressPolicy.Normalize(Valid(pincode));
        act.Should().Throw<ArgumentException>().WithMessage("*six-digit*");
    }

    [Fact]
    public void PickupDoesNotRequireAnAddress()
    {
        OrderIntakeDeliveryAddressPolicy.NormalizeFulfillment("STORE_PICKUP").Should().Be("PICKUP");
        OrderIntakeDeliveryAddressPolicy.NormalizeFulfillment("HOME_DELIVERY").Should().Be("DELIVERY");
    }

    [Fact]
    public void V81AddressChangesInvalidateQuoteAndConfirmationAndPersistSnapshots()
    {
        var migration = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../..", "database/migrations/V81-OrderIntakeDeliveryAddressSnapshots.sql")));
        migration.Should().Contain("DeliveryAddressDraftJson").And.Contain("DeliveryAddressSnapshotJson");
        var service = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../..", "backend/src/WhatsBiz.Infrastructure/OrderIntake/OrderIntakeService.cs")));
        service.Should().Contain("SET QuoteStatus='SUPERSEDED'").And.Contain("ConfirmedQuoteRevision=NULL")
            .And.Contain("DeliveryAddressSnapshotJson");
    }
}
