using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/admin")]
public sealed class IdentityAdministrationController(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles) : ControllerBase
{
    [HttpGet("users"), HasPermission(Permissions.Users.Manage)]
    public async Task<IReadOnlyCollection<AdminUserDto>> Users(CancellationToken token)
    {
        var values = await users.Users
            .OrderBy(x => x.UserName)
            .Select(x => new AdminUserDto(x.Id, x.UserName ?? string.Empty, x.Email ?? string.Empty, x.IsActive, x.IsDeleted))
            .ToArrayAsync(token);
        return values;
    }

    [HttpGet("roles"), HasPermission(Permissions.Roles.Manage)]
    public async Task<IReadOnlyCollection<AdminRoleDto>> Roles(CancellationToken token)
    {
        var values = await roles.Roles.OrderBy(x => x.Name).ToArrayAsync(token);
        var result = new List<AdminRoleDto>(values.Length);
        foreach (var role in values)
        {
            var permissions = (await roles.GetClaimsAsync(role))
                .Where(x => x.Type == CustomClaimTypes.Permission)
                .Select(x => x.Value)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            result.Add(new AdminRoleDto(role.Id, role.Name ?? string.Empty, permissions));
        }
        return result;
    }
}

public sealed record AdminUserDto(Guid UserId, string UserName, string Email, bool IsActive, bool IsDeleted);
public sealed record AdminRoleDto(Guid RoleId, string RoleName, IReadOnlyCollection<string> Permissions);
