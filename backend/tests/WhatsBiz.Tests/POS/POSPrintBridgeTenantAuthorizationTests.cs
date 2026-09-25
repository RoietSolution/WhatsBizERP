using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Domain.POS;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.POS;

public sealed class POSPrintBridgeTenantAuthorizationTests
{
    [Fact]
    public async Task ExplicitTenantLookupCannotRedeemAnotherTenantsInvoice()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var invoice = new SalesInvoice
        {
            InvoiceId = Guid.NewGuid(),
            InvoiceNumber = "TENANT-B-INVOICE",
            InvoiceDate = DateTimeOffset.UtcNow,
            Status = "COMPLETED"
        };
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        db.SalesInvoices.Add(invoice);
        db.Entry(invoice).Property("TenantId").CurrentValue = tenantB;
        db.SaveChanges();
        var repository = new POSRepository(db, null!);

        db.SalesInvoices.Find(invoice.InvoiceId).Should().NotBeNull();
        db.Entry(invoice).Property<Guid?>("TenantId").CurrentValue.Should().Be(tenantB);
        (await repository.InvoiceForTenant(invoice.InvoiceId, tenantA, CancellationToken.None)).Should().BeNull();
    }
}
