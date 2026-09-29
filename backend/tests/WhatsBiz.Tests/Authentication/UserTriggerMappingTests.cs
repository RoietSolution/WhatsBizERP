using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Authentication;

public sealed class UserTriggerMappingTests
{
    [Fact]
    public void TriggeredIdentityTablesAvoidBareSqlOutputAndUserKeepsRowVersionConcurrency()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var db = new ApplicationDbContext(options);

        var users = db.Model.FindEntityType(typeof(ApplicationUser))!;
        users.GetSchema().Should().Be("core");
        users.GetTableName().Should().Be("Users");
        users.IsSqlOutputClauseUsed().Should().BeFalse();

        var rowVersion = users.FindProperty(nameof(ApplicationUser.RowVersion))!;
        rowVersion.IsConcurrencyToken.Should().BeTrue();
        rowVersion.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);

        var userRoles = db.Model.FindEntityType(typeof(IdentityUserRole<Guid>))!;
        userRoles.GetSchema().Should().Be("core");
        userRoles.GetTableName().Should().Be("UserRoles");
        userRoles.IsSqlOutputClauseUsed().Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAndClearRequiredFlagUpdateApplicationUser()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        using var store = new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db);
        using var users = new UserManager<ApplicationUser>(
            store, Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()], [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), UserName = "password-user",
            Email = "password-user@example.test", MustChangePassword = true
        };
        (await users.CreateAsync(user, "Current@123456")).Succeeded.Should().BeTrue();
        (await users.ChangePasswordAsync(user, "Current@123456", "NewPassword@123456")).Succeeded.Should().BeTrue();

        user.MustChangePassword = false;
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
        db.ChangeTracker.Clear();

        var reloaded = await users.FindByIdAsync(user.Id.ToString());
        reloaded.Should().NotBeNull();
        reloaded!.TenantId.Should().Be(user.TenantId);
        reloaded.MustChangePassword.Should().BeFalse();
        (await users.CheckPasswordAsync(reloaded, "NewPassword@123456")).Should().BeTrue();
    }
}