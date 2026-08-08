using FluentAssertions;
using WhatsBiz.Application.Features.Inventory;
namespace WhatsBiz.Tests.Inventory;
public sealed class InventoryOperationsValidatorTests
{
    [Fact] public async Task OperationalAdjustmentAcceptsDamageReason() { var item = new StockOperationItem(Guid.NewGuid(), null, null, null, null, null, null, 2, 5); var result = await new CreateAdjustmentValidator().ValidateAsync(new CreateAdjustment(new("DECREASE", "DAMAGE", "APPROVED", Guid.NewGuid(), null, [item]))); result.IsValid.Should().BeTrue(); }
    [Fact] public async Task LocationTransferRequiresDifferentLocations() { var warehouse = Guid.NewGuid(); var item = new StockOperationItem(Guid.NewGuid(), null, null, null, null, null, null, 1, 0); var result = await new CreateTransferValidator().ValidateAsync(new CreateTransfer(new(warehouse, warehouse, DateTimeOffset.UtcNow, "APPROVED", null, [item]))); result.IsValid.Should().BeFalse(); }
    [Fact] public async Task PhysicalCountCannotBeNegative() { var item = new VerificationItem(Guid.NewGuid(), null, null, null, null, -1, 0); var result = await new CreateVerificationValidator().ValidateAsync(new CreateVerification(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "APPROVED", null, [item]))); result.IsValid.Should().BeFalse(); }
}
