using FluentAssertions;
using WhatsBiz.Application.Features.Inventory;
namespace WhatsBiz.Tests.Inventory;
public sealed class InventoryValidatorTests
{
    [Fact] public async Task IncreaseAdjustmentWithPositiveQuantityIsValid() { var result = await new AdjustmentValidator().ValidateAsync(new AdjustStock(new(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, 10, 25, "INCREASE", "OPENING_STOCK", null))); result.IsValid.Should().BeTrue(); }
    [Fact] public async Task AdjustmentRejectsNonPositiveQuantityAndUnknownType() { var result = await new AdjustmentValidator().ValidateAsync(new AdjustStock(new(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, 0, 0, "UNKNOWN", "", null))); result.IsValid.Should().BeFalse(); result.Errors.Should().Contain(x => x.PropertyName.EndsWith(nameof(AdjustmentInput.Quantity), StringComparison.Ordinal)); result.Errors.Should().Contain(x => x.PropertyName.EndsWith(nameof(AdjustmentInput.AdjustmentType), StringComparison.Ordinal)); }
    [Fact] public async Task TransferRejectsSameWarehouse() { var warehouse = Guid.NewGuid(); var result = await new TransferValidator().ValidateAsync(new TransferStock(new(Guid.NewGuid(), warehouse, warehouse, 1, DateTimeOffset.UtcNow, null))); result.IsValid.Should().BeFalse(); }
    [Fact] public async Task ReserveRequiresProductAndWarehouse() { var result = await new ReservationValidator().ValidateAsync(new ReserveStock(new("RESERVE", null, null, null, 1, "Order", null, null))); result.IsValid.Should().BeFalse(); }
    [Fact] public async Task ReleaseRequiresReservationId() { var result = await new ReservationValidator().ValidateAsync(new ReserveStock(new("RELEASE", null, null, null, 1, null, null, null))); result.IsValid.Should().BeFalse(); }
}
