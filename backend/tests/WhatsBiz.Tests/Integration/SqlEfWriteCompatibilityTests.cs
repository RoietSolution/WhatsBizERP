using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Domain.Products;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class SqlEfWriteCompatibilityTests
{
    [Fact]
    public async Task IdentityProductCustomerAndStorefrontWritesWorkWithPublishedTriggers()
    {
        var connectionString = SqlIntegrationDatabase.ConnectionString;
        var suffix = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new ApplicationDbContext(options);

        await db.Database.OpenConnectionAsync();
        try
        {
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync((SqlConnection)db.Database.GetDbConnection());
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sys.sp_set_session_context @key=N'TenantId', @value={tenantId}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                db.Tenants.Add(new Tenant { TenantId = tenantId, TenantKey = $"efcompat-{suffix}", Name = "EF compatibility test", IsActive = true });
                var roleName = $"EfCompatibility{suffix}";
                db.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
                await db.SaveChangesAsync();

                using var store = new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db);
                using var users = new UserManager<ApplicationUser>(
                    store, Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
                    [new UserValidator<ApplicationUser>()], [new PasswordValidator<ApplicationUser>()],
                    new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
                    new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);
                var user = new ApplicationUser
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, UserName = $"efcompat-{suffix}",
                    Email = $"efcompat-{suffix}@example.test", MustChangePassword = true
                };
                (await users.CreateAsync(user, "Initial@123456")).Succeeded.Should().BeTrue();
                var originalVersion = user.RowVersion.ToArray();
                originalVersion.Should().NotBeEmpty();

                (await users.ChangePasswordAsync(user, "Initial@123456", "Updated@123456")).Succeeded.Should().BeTrue();
                user.RowVersion.Should().NotEqual(originalVersion);
                user.MustChangePassword = false;
                (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
                (await users.CheckPasswordAsync(user, "Updated@123456")).Should().BeTrue();
                (await users.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();
                (await users.IsInRoleAsync(user, roleName)).Should().BeTrue();
                (await users.RemoveFromRoleAsync(user, roleName)).Succeeded.Should().BeTrue();
                (await users.IsInRoleAsync(user, roleName)).Should().BeFalse();

                var category = new ProductCategory { CategoryCode = $"EFC-{suffix}", CategoryName = "EF compatibility" };
                var brand = new Brand { BrandCode = $"EFB-{suffix}", BrandName = "EF compatibility" };
                var unit = new UnitOfMeasure { UnitCode = $"EFU-{suffix}", UnitName = "Each", ShortName = "ea" };
                db.ProductCategories.Add(category);
                db.Brands.Add(brand);
                db.UnitsOfMeasure.Add(unit);
                await db.SaveChangesAsync();

                var product = new Product
                {
                    TenantId = tenantId, ProductCode = $"EFP-{suffix}", ProductName = "EF compatibility product",
                    CategoryId = category.ProductCategoryId, BrandId = brand.BrandId, UnitId = unit.UnitId,
                    PurchasePrice = 10m, SellingPrice = 12m, MRP = 12m
                };
                var customer = new Customer
                {
                    TenantId = tenantId, CustomerCode = $"EFCU-{suffix}", CustomerName = "EF compatibility customer",
                    CustomerType = "Retail"
                };
                db.Products.Add(product);
                db.Customers.Add(customer);
                await db.SaveChangesAsync();
                product.ProductName = "Updated EF compatibility product";
                customer.CustomerName = "Updated EF compatibility customer";
                await db.SaveChangesAsync();

                var storefront = new StorefrontConfiguration
                {
                    TenantId = tenantId, Tagline = "Initial", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
                db.StorefrontConfigurations.Add(storefront);
                await db.SaveChangesAsync();
                storefront.Tagline = "Updated";
                storefront.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                await transaction.RollbackAsync();
            }
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key=N'TenantId', @value=NULL");
            await db.Database.CloseConnectionAsync();
        }
    }
}

[CollectionDefinition("SQL EF compatibility", DisableParallelization = true)]
public sealed class SqlEfWriteCompatibilityCollectionDefinition;