using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Infrastructure.Identity;
public sealed class IdentitySeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    ILogger<IdentitySeeder> logger) : IHostedService
{
    private static readonly Action<ILogger, Exception?> SeedCompleted = LoggerMessage.Define(LogLevel.Information, new EventId(2001, nameof(SeedCompleted)), "Identity seed completed.");
    private static readonly Action<ILogger, string, string, Exception?> AdministratorCreated = LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(2002, nameof(AdministratorCreated)), "Bootstrap administrator {Username} created for tenant {TenantKey}.");
    private static readonly Action<ILogger, string, string, Exception?> AdministratorPasswordReset = LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(2003, nameof(AdministratorPasswordReset)), "Bootstrap administrator {Username} password reset for tenant {TenantKey}. Disable ResetPasswordOnStart and remove the password from configuration now.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope(); var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); const string roleName = "Administrator";
        const string systemRoleName = "SystemAdministrator";
        var systemRole = await roles.FindByNameAsync(systemRoleName);
        if (systemRole is null) { systemRole = new ApplicationRole(systemRoleName); EnsureSucceeded(await roles.CreateAsync(systemRole)); }
        var systemClaims = await roles.GetClaimsAsync(systemRole);
        if (!systemClaims.Any(x => x.Type == CustomClaimTypes.Permission && x.Value == Permissions.Features.Manage))
            EnsureSucceeded(await roles.AddClaimAsync(systemRole, new Claim(CustomClaimTypes.Permission, Permissions.Features.Manage)));
        var role = await roles.FindByNameAsync(roleName);
        if (role is null) { role = new ApplicationRole(roleName); var result = await roles.CreateAsync(role); if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description))); }
        var roleClaims = await roles.GetClaimsAsync(role); foreach (var permission in Permissions.All) if (!roleClaims.Any(x => x.Type == CustomClaimTypes.Permission && x.Value == permission)) EnsureSucceeded(await roles.AddClaimAsync(role, new Claim(CustomClaimTypes.Permission, permission)));
        const string deliveryRoleName = "DeliveryAgent"; var deliveryRole=await roles.FindByNameAsync(deliveryRoleName);if(deliveryRole is null){deliveryRole=new ApplicationRole(deliveryRoleName);EnsureSucceeded(await roles.CreateAsync(deliveryRole));}
        var deliveryClaims=await roles.GetClaimsAsync(deliveryRole);foreach(var permission in new[]{Permissions.Delivery.View,Permissions.Delivery.UpdateStatus,Permissions.Delivery.Confirm,Permissions.Delivery.RecordCod})if(!deliveryClaims.Any(x=>x.Type==CustomClaimTypes.Permission&&x.Value==permission))EnsureSucceeded(await roles.AddClaimAsync(deliveryRole,new Claim(CustomClaimTypes.Permission,permission)));
        var bootstrap = bootstrapOptions.Value.Administrator;
        if (bootstrap.Enabled)
            await BootstrapAdministratorAsync(scope.ServiceProvider, users, bootstrap, roleName, systemRoleName, cancellationToken);

        SeedCompleted(logger, null);
    }

    private async Task BootstrapAdministratorAsync(
        IServiceProvider services,
        UserManager<ApplicationUser> users,
        BootstrapAdministratorOptions options,
        string administratorRole,
        string systemAdministratorRole,
        CancellationToken cancellationToken)
    {
        var tenantKey = Required(options.TenantKey, nameof(options.TenantKey)).ToUpperInvariant();
        var username = Required(options.Username, nameof(options.Username));
        var email = Required(options.Email, nameof(options.Email));
        var db = services.GetRequiredService<ApplicationDbContext>();
        var tenantId = await db.Database
            .SqlQuery<Guid>($"SELECT [TenantId] AS [Value] FROM [core].[Tenants] WHERE [TenantKey] = {tenantKey} AND [IsActive] = 1")
            .SingleOrDefaultAsync(cancellationToken);
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException($"Identity bootstrap tenant '{tenantKey}' does not exist or is inactive. Run the database onboarding script first.");

        var user = await users.FindByNameAsync(username);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(options.Password))
                throw new InvalidOperationException("Identity bootstrap administrator password is required only for initial creation. Configure IdentityBootstrap__Administrator__Password through a secret environment file.");
            var emailOwner = await users.FindByEmailAsync(email);
            if (emailOwner is not null)
                throw new InvalidOperationException($"Identity bootstrap email '{email}' is already assigned to a different user.");
            user = new ApplicationUser
            {
                TenantId = tenantId,
                UserName = username,
                Email = email,
                EmailConfirmed = true,
                IsActive = true,
                CreatedBy = "identity-bootstrap"
            };
            EnsureSucceeded(await users.CreateAsync(user, options.Password));
            AdministratorCreated(logger, username, tenantKey, null);
        }
        else if (user.TenantId != tenantId)
        {
            throw new InvalidOperationException($"Identity bootstrap user '{username}' belongs to a different tenant; ownership was not changed.");
        }

        if (!await users.IsInRoleAsync(user, administratorRole))
            EnsureSucceeded(await users.AddToRoleAsync(user, administratorRole));
        if (options.IncludeSystemAdministratorRole && !await users.IsInRoleAsync(user, systemAdministratorRole))
            EnsureSucceeded(await users.AddToRoleAsync(user, systemAdministratorRole));

        if (options.ResetPasswordOnStart)
        {
            if (string.IsNullOrWhiteSpace(options.Password))
                throw new InvalidOperationException("Identity bootstrap password is required when ResetPasswordOnStart is enabled.");
            var token = await users.GeneratePasswordResetTokenAsync(user);
            EnsureSucceeded(await users.ResetPasswordAsync(user, token, options.Password));
            AdministratorPasswordReset(logger, username, tenantKey, null);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"Identity bootstrap administrator {name} is required.") : value.Trim();
    private static void EnsureSucceeded(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description))); }
}
