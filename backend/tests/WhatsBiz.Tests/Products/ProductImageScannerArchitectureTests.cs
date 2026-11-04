using FluentAssertions;

namespace WhatsBiz.Tests.Products;

public sealed class ProductImageScannerArchitectureTests
{
    private static string Root(string relative) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../..", relative));

    [Fact]
    public void ScannerBoundsConcurrencyAndCleansTimedOutProcessesAndFiles()
    {
        var source = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Storefront/ClamAvFeedbackAttachmentScanner.cs"));
        source.Should().Contain("MaxConcurrentScans")
            .And.Contain("WaitForExitAsync(timeout.Token)")
            .And.Contain("process.Kill(entireProcessTree: true)")
            .And.Contain("File.Delete(temporary)")
            .And.Contain("0 => StorefrontAttachmentScanResult.Clean");
    }

    [Fact]
    public void ProductHandlerScansBeforeOptimizationAndStorageAndRejectsNonCleanResults()
    {
        var source = File.ReadAllText(Root("backend/src/WhatsBiz.Application/Features/Products/Products/ProductHandlers.cs"));
        source.IndexOf("ScanOriginalAsync", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("storage.StoreAsync", StringComparison.Ordinal));
        source.Should().Contain("StorefrontProductImageScanGuard.RequireClean")
            .And.Contain("TenantId = tenantId")
            .And.Contain("ProductGates");
    }

    [Fact]
    public void CityMediaUploadScansBeforeOptimizationAndStorageAndRejectsNonCleanResults()
    {
        var source = File.ReadAllText(Root("backend/src/WhatsBiz.Infrastructure/Storefront/PublicMarketplaceService.cs"));
        var upload = source[source.IndexOf("public async Task UploadCityMedia", StringComparison.Ordinal)..source.IndexOf("public async Task RemoveCityMedia", StringComparison.Ordinal)];
        upload.IndexOf("scanner.ScanAsync", StringComparison.Ordinal).Should().BeLessThan(upload.IndexOf("optimizer.OptimizeAsync", StringComparison.Ordinal));
        upload.IndexOf("StorefrontProductImageScanGuard.RequireClean", StringComparison.Ordinal).Should().BeLessThan(upload.IndexOf("storage.StoreStorefrontAsync", StringComparison.Ordinal));
    }
}
