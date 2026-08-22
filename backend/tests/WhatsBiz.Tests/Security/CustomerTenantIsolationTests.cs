using System.Reflection;
using FluentAssertions;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.POS;

namespace WhatsBiz.Tests.Security;

public sealed class CustomerTenantIsolationTests
{
    [Fact]
    public void CustomerHasExplicitOptionalTenantOwnershipForSafeLegacyRemediation()
    {
        typeof(Customer).GetProperty(nameof(Customer.TenantId))!.PropertyType.Should().Be<Guid?>();
        typeof(Customer).GetProperty(nameof(Customer.TenantId))!.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void CustomerRepositoryAndPosPostingRequireResolvedTenantContext()
    {
        typeof(CustomerRepository).GetConstructors().Single().GetParameters().Select(x => x.ParameterType).Should().Contain(typeof(ICurrentUserService));
        typeof(POSEngine).GetConstructors().Single().GetParameters().Select(x => x.ParameterType).Should().Contain(typeof(ICurrentUserService));
    }

    [Fact]
    public void CollectionSendAcceptsCustomerIdOnlyAndRemainsPermissionProtected()
    {
        typeof(SendCollectionInput).GetProperties().Select(x => x.Name).Should().Equal(nameof(SendCollectionInput.CustomerId));
        typeof(CommerceCollectionsController).GetMethod(nameof(CommerceCollectionsController.Send))!.GetCustomAttributes<WhatsBiz.Api.Authorization.HasPermissionAttribute>().Single().Policy.Should().Contain(WhatsBiz.SharedKernel.Permissions.Product.Edit);
    }

    [Fact]
    public void MigrationOnlyBackfillsWhenExactlyOneActiveTenantExists()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database", "WhatsBiz.Database", "Scripts", "V7-CustomerTenantIsolation.sql"))) root = root.Parent;
        root.Should().NotBeNull();
        var sql = File.ReadAllText(Path.Combine(root!.FullName, "database", "WhatsBiz.Database", "Scripts", "V7-CustomerTenantIsolation.sql"));
        sql.Should().Contain("@TenantCount=1").And.Contain("FK_Customers_Tenant").And.Contain("IX_Customers_TenantMobile");
    }
}
