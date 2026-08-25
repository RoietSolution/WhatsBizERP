using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WhatsBiz.SharedKernel;
namespace WhatsBiz.Infrastructure.Identity;
public sealed class IdentitySeeder(IServiceScopeFactory scopeFactory, ILogger<IdentitySeeder> logger) : IHostedService
{
    private static readonly Action<ILogger, Exception?> SeedCompleted = LoggerMessage.Define(LogLevel.Information, new EventId(2001, nameof(SeedCompleted)), "Identity seed completed.");
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
        var user = await users.FindByNameAsync("admin");
        if (user is null) { user = new ApplicationUser { UserName = "admin", Email = "admin@whatsbiz.local", EmailConfirmed = true }; var result = await users.CreateAsync(user, "Admin@123456"); if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description))); }
        if (!await users.IsInRoleAsync(user, roleName)) EnsureSucceeded(await users.AddToRoleAsync(user, roleName)); SeedCompleted(logger, null);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    private static void EnsureSucceeded(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description))); }
}
