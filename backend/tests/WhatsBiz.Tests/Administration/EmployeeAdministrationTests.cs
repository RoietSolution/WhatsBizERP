using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Administration;

public sealed class EmployeeAdministrationTests
{
    [Fact]
    public async Task UserListIsLimitedToCurrentRetailer()
    {
        await using var fixture = CreateFixture();
        var own = await fixture.AddUser(fixture.TenantId, "own-employee");
        await fixture.AddUser(Guid.NewGuid(), "other-retailer-employee");

        var result = await fixture.Controller.Users(default);

        result.Should().ContainSingle(x => x.UserId == own.Id);
        result.Should().NotContain(x => x.UserName == "other-retailer-employee");
    }

    [Fact]
    public async Task CreateEmployeeStoresOnlySelectedDirectPermissions()
    {
        await using var fixture = CreateFixture();

        var response = await fixture.Controller.CreateUser(new(
            "cashier-one", "cashier@example.test", null, "Cashier@123456", true,
            [Permissions.POS.View, Permissions.POS.Create]));

        var created = response.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value
            .Should().BeOfType<AdminUserDto>().Subject;
        created.Permissions.Should().BeEquivalentTo(Permissions.POS.View, Permissions.POS.Create);
        var employee = await fixture.Users.FindByIdAsync(created.UserId.ToString());
        employee!.TenantId.Should().Be(fixture.TenantId);
        (await fixture.Users.GetClaimsAsync(employee)).Select(x => x.Value)
            .Should().BeEquivalentTo(Permissions.POS.View, Permissions.POS.Create);
    }

    [Fact]
    public async Task AdministratorCannotDelegatePermissionTheyDoNotHold()
    {
        await using var fixture = CreateFixture([Permissions.Users.Manage, Permissions.POS.View]);

        var action = () => fixture.Controller.CreateUser(new(
            "overprivileged", "overprivileged@example.test", null, "Cashier@123456", true,
            [Permissions.POS.Discount]));

        await action.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*cannot be assigned*");
    }

    [Fact]
    public async Task EmployeeFromAnotherRetailerCannotBeUpdated()
    {
        await using var fixture = CreateFixture();
        var other = await fixture.AddUser(Guid.NewGuid(), "other-employee");

        var action = () => fixture.Controller.UpdateUser(other.Id,
            new("other@example.test", null, true, [Permissions.POS.View]), default);

        await action.Should().ThrowAsync<EntityNotFoundException>();
    }

    private static Fixture CreateFixture(IReadOnlyCollection<string>? permissions = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);
        var userStore = new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db);
        var roleStore = new RoleStore<ApplicationRole, ApplicationDbContext, Guid>(db);
        var identityOptions = Options.Create(new IdentityOptions());
        var users = new UserManager<ApplicationUser>(userStore, identityOptions, new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()], [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
        var roles = new RoleManager<ApplicationRole>(roleStore, [new RoleValidator<ApplicationRole>()],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            NullLogger<RoleManager<ApplicationRole>>.Instance);
        var tenantId = Guid.NewGuid();
        var current = new CurrentUser(tenantId, permissions ?? [Permissions.Users.Manage, Permissions.POS.View, Permissions.POS.Create, Permissions.POS.Discount]);
        return new(db, users, roles, current, tenantId);
    }

    private sealed class Fixture(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        CurrentUser current,
        Guid tenantId) : IAsyncDisposable
    {
        public Guid TenantId { get; } = tenantId;
        public UserManager<ApplicationUser> Users { get; } = users;
        public IdentityAdministrationController Controller { get; } = new(users, roles, db, current);

        public async Task<ApplicationUser> AddUser(Guid tenant, string name)
        {
            var user = new ApplicationUser { Id = Guid.NewGuid(), TenantId = tenant, UserName = name, Email = $"{name}@example.test" };
            (await Users.CreateAsync(user, "Employee@123456")).Succeeded.Should().BeTrue();
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            Users.Dispose();
            roles.Dispose();
            await db.DisposeAsync();
        }
    }

    private sealed class CurrentUser(Guid tenantId, IReadOnlyCollection<string> permissions) : ICurrentUserService
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? TenantId => tenantId;
        public string? Username => "retailer-admin";
        public string? Email => "admin@example.test";
        public IReadOnlyCollection<string> Roles => ["Administrator"];
        public IReadOnlyCollection<string> Permissions => permissions;
    }
}
