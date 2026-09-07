using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Delivery;
using WhatsBiz.Infrastructure.Delivery;
using WhatsBiz.Infrastructure.WhatsAppCommerce;
#pragma warning disable CA1707

namespace WhatsBiz.Tests.Security;

public sealed class QaDeploymentBlockerRegressionTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Cod_rejects_a_supplied_tenant_that_is_not_the_authenticated_tenant_before_sql()
    {
        var service = new DeliveryService(Configuration(), DataProtectionProvider.Create("qa-blocker-test"), null!, null!, new CurrentUser(TenantA));

        var action = () => service.RecordCod(TenantB, Guid.NewGuid(), Guid.NewGuid(), new("CASH", 1m, null), "test", true, default);

        await action.Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.EntityNotFoundException>();
    }

    [Fact]
    public async Task Cod_rejects_missing_authenticated_tenant_before_sql()
    {
        var service = new DeliveryService(Configuration(), DataProtectionProvider.Create("qa-blocker-test"), null!, null!, new CurrentUser(null));

        var action = () => service.RecordCod(TenantA, Guid.NewGuid(), Guid.NewGuid(), new("CASH", 1m, null), "test", true, default);

        await action.Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.UnauthorizedAccessException>();
    }

    [Fact]
    public async Task WhatsApp_commerce_rejects_cross_tenant_context_before_sql()
    {
        var service = new WhatsAppCommerceService(Configuration(), null!, null!, null!, DataProtectionProvider.Create("qa-blocker-test"), new CurrentUser(TenantA));

        var action = () => service.GetSetupAsync(TenantB, null, default);

        await action.Should().ThrowAsync<WhatsBiz.Application.Common.Exceptions.EntityNotFoundException>();
    }

    [Fact]
    public void Cod_and_commerce_sql_keep_explicit_tenant_contracts_and_atomic_ordering()
    {
        var delivery = File.ReadAllText(Find("backend", "src", "WhatsBiz.Infrastructure", "Delivery", "DeliveryService.cs"));
        var commerce = File.ReadAllText(Find("backend", "src", "WhatsBiz.Infrastructure", "WhatsAppCommerce", "WhatsAppCommerceService.cs"));

        delivery.Should().Contain("P(pay,\"@TenantId\",tenantId)")
            .And.Contain("i.InvoiceId=d.OrderId AND i.TenantId=d.TenantId")
            .And.Contain("SetTenantContext(c,tx,tenantId,token)");
        delivery.IndexOf("await pay.ExecuteNonQueryAsync(token)", StringComparison.Ordinal)
            .Should().BeLessThan(delivery.IndexOf("CodCollected=1", StringComparison.Ordinal));
        delivery.IndexOf("CodCollected=1", StringComparison.Ordinal)
            .Should().BeLessThan(delivery.IndexOf("await tx.CommitAsync(token)", delivery.IndexOf("CodCollected=1", StringComparison.Ordinal), StringComparison.Ordinal));

        commerce.Should().Contain("WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0 ORDER BY IsDefault")
            .And.Contain("b.TenantId=@tenant AND w.TenantId=@tenant")
            .And.Contain("w.WarehouseId=@warehouse AND w.TenantId=@tenant")
            .And.Contain("b.WarehouseId=w.WarehouseId AND b.TenantId=@tenant")
            .And.Contain("WarehouseId=@warehouse AND TenantId=@tenant AND IsActive=1 AND IsDeleted=0");
        commerce.Should().NotContain("FROM inventory.Warehouses WHERE IsActive=1 AND IsDeleted=0");
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Server=unused" })
        .Build();

    private static string Find(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root was not found."), Path.Combine(parts));
    }

    private sealed class CurrentUser(Guid? tenantId) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid? TenantId => tenantId;
        public string? Username => "qa-blocker-test";
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
    }
}
