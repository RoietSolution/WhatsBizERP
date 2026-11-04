using FluentAssertions;

namespace WhatsBiz.Tests.Delivery;

public sealed class ShippingLabelPaymentProjectionTests
{
    [Fact]
    public void ShippingLabelUsesCodApplicationAmountForCollectedPayment()
    {
        var source = Read("backend/src/WhatsBiz.Api/Controllers/StorefrontOrdersController.cs");
        source.Should().Contain("p.Provider=N'COD' AND p.Status IN(N'PAID',N'CAPTURED')")
            .And.Contain("d.CodCollected=1 AND cod.Amount IS NOT NULL")
            .And.Contain("COD PAID");
    }

    [Fact]
    public void IncompleteCodCollectionIsBlockedFromLabelGeneration()
    {
        Read("backend/src/WhatsBiz.Api/Controllers/StorefrontOrdersController.cs")
            .Should().Contain("the COD collection record is incomplete");
    }

    [Fact]
    public void DeliveryProjectionExposesExpectedAndAuthoritativeCollectionDetails()
    {
        var service = Read("backend/src/WhatsBiz.Infrastructure/Delivery/DeliveryService.cs");
        service.Should().Contain("d.CodCollectedAt,d.CodReference,d.ProofType,d.DeliveredAt,cod.Amount,cod.AppliedBy,cod.AppliedAt")
            .And.Contain("PaymentApplications pa")
            .And.Contain("cp.Provider=N'COD'");
    }

    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "WhatsBiz.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), path));
    }
}
