using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController, Route("api/admin")]
public sealed class IdentityAdministrationController(
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    ApplicationDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    private static readonly HashSet<string> NonDelegablePermissions = new(StringComparer.Ordinal)
    {
        Permissions.Users.Manage,
        Permissions.Roles.Manage,
        Permissions.PermissionsManagement.Manage,
        Permissions.Features.Manage
    };

    [HttpGet("users"), HasPermission(Permissions.Users.Manage)]
    public async Task<IReadOnlyCollection<AdminUserDto>> Users(CancellationToken token)
    {
        var tenantId = RequireTenant();
        var values = await users.Users
            .Where(x => x.TenantId == tenantId && !x.IsDeleted)
            .OrderBy(x => x.UserName)
            .ToArrayAsync(token);
        var result = new List<AdminUserDto>(values.Length);
        foreach (var user in values)
        {
            var permissions = (await users.GetClaimsAsync(user))
                .Where(x => x.Type == CustomClaimTypes.Permission)
                .Select(x => x.Value)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            result.Add(ToDto(user, permissions));
        }
        return result;
    }

    [HttpGet("users/permissions"), HasPermission(Permissions.Users.Manage)]
    public IReadOnlyCollection<string> AssignablePermissions() => GetAssignablePermissions().OrderBy(x => x, StringComparer.Ordinal).ToArray();

    [HttpPost("users"), HasPermission(Permissions.Users.Manage)]
    public async Task<ActionResult<AdminUserDto>> CreateUser(CreateEmployeeInput input)
    {
        var tenantId = RequireTenant();
        ValidateCreate(input);
        var permissions = ValidatePermissions(input.Permissions);
        var employee = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = input.UserName.Trim(),
            Email = input.Email.Trim(),
            PhoneNumber = Clean(input.PhoneNumber),
            IsActive = input.IsActive,
            IsDeleted = false,
            CreatedBy = currentUser.Username
        };
        EnsureSucceeded(await users.CreateAsync(employee, input.TemporaryPassword));
        var claimsResult = await users.AddClaimsAsync(employee, ToClaims(permissions));
        if (!claimsResult.Succeeded)
        {
            await users.DeleteAsync(employee);
            EnsureSucceeded(claimsResult);
        }
        return CreatedAtAction(nameof(Users), ToDto(employee, permissions));
    }

    [HttpPut("users/{id:guid}"), HasPermission(Permissions.Users.Manage)]
    public async Task<AdminUserDto> UpdateUser(Guid id, UpdateEmployeeInput input, CancellationToken token)
    {
        EnsureNotSelf(id);
        var employee = await FindEmployee(id, token);
        if (string.IsNullOrWhiteSpace(input.Email)) throw new BusinessRuleException("Email is required.");
        var permissions = ValidatePermissions(input.Permissions);
        employee.Email = input.Email.Trim();
        employee.PhoneNumber = Clean(input.PhoneNumber);
        employee.IsActive = input.IsActive;
        employee.ModifiedOn = DateTimeOffset.UtcNow;
        employee.ModifiedBy = currentUser.Username;
        EnsureSucceeded(await users.UpdateAsync(employee));
        await ReplacePermissions(employee, permissions);
        if (!employee.IsActive) await RevokeRefreshTokens(employee.Id, token);
        return ToDto(employee, permissions);
    }

    [HttpPost("users/{id:guid}/reset-password"), HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetEmployeePasswordInput input, CancellationToken token)
    {
        EnsureNotSelf(id);
        if (string.IsNullOrWhiteSpace(input.TemporaryPassword)) throw new BusinessRuleException("Temporary password is required.");
        var employee = await FindEmployee(id, token);
        var resetToken = await users.GeneratePasswordResetTokenAsync(employee);
        EnsureSucceeded(await users.ResetPasswordAsync(employee, resetToken, input.TemporaryPassword));
        await RevokeRefreshTokens(employee.Id, token);
        return NoContent();
    }

    [HttpDelete("users/{id:guid}"), HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> DeactivateUser(Guid id, CancellationToken token)
    {
        EnsureNotSelf(id);
        var employee = await FindEmployee(id, token);
        employee.IsActive = false;
        employee.IsDeleted = true;
        employee.ModifiedOn = DateTimeOffset.UtcNow;
        employee.ModifiedBy = currentUser.Username;
        EnsureSucceeded(await users.UpdateAsync(employee));
        await RevokeRefreshTokens(employee.Id, token);
        return NoContent();
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

    private Guid RequireTenant() => currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");

    private async Task<ApplicationUser> FindEmployee(Guid id, CancellationToken token)
    {
        var tenantId = RequireTenant();
        return await users.Users.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted, token)
            ?? throw new EntityNotFoundException("Employee not found.");
    }

    private void EnsureNotSelf(Guid id)
    {
        if (currentUser.UserId == id) throw new BusinessRuleException("You cannot manage your own account from Employee Management.");
    }

    private HashSet<string> GetAssignablePermissions() => currentUser.Permissions
        .Where(Permissions.All.Contains)
        .Where(x => !NonDelegablePermissions.Contains(x))
        .ToHashSet(StringComparer.Ordinal);

    private string[] ValidatePermissions(IReadOnlyCollection<string>? requested)
    {
        var values = (requested ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Except(GetAssignablePermissions(), StringComparer.Ordinal).Any())
            throw new BusinessRuleException("One or more selected permissions cannot be assigned by your account.");
        return values;
    }

    private async Task ReplacePermissions(ApplicationUser employee, string[] permissions)
    {
        var existing = (await users.GetClaimsAsync(employee)).Where(x => x.Type == CustomClaimTypes.Permission).ToArray();
        if (existing.Length > 0) EnsureSucceeded(await users.RemoveClaimsAsync(employee, existing));
        if (permissions.Length == 0) return;
        var result = await users.AddClaimsAsync(employee, ToClaims(permissions));
        if (!result.Succeeded)
        {
            if (existing.Length > 0) await users.AddClaimsAsync(employee, existing);
            EnsureSucceeded(result);
        }
    }

    private async Task RevokeRefreshTokens(Guid userId, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(x => x.UserId == userId && x.IsActive && x.RevokedOn == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.IsActive, false).SetProperty(t => t.RevokedOn, now), token);
    }

    private static Claim[] ToClaims(IEnumerable<string> permissions) => permissions.Select(x => new Claim(CustomClaimTypes.Permission, x)).ToArray();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AdminUserDto ToDto(ApplicationUser user, IReadOnlyCollection<string> permissions) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email ?? string.Empty, user.PhoneNumber, user.IsActive, user.IsDeleted, permissions);

    private static void ValidateCreate(CreateEmployeeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.UserName)) throw new BusinessRuleException("Username is required.");
        if (string.IsNullOrWhiteSpace(input.Email)) throw new BusinessRuleException("Email is required.");
        if (string.IsNullOrWhiteSpace(input.TemporaryPassword)) throw new BusinessRuleException("Temporary password is required.");
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded) throw new BusinessRuleException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }
}

public sealed record AdminUserDto(Guid UserId, string UserName, string Email, string? PhoneNumber, bool IsActive, bool IsDeleted, IReadOnlyCollection<string> Permissions);
public sealed record AdminRoleDto(Guid RoleId, string RoleName, IReadOnlyCollection<string> Permissions);
public sealed record CreateEmployeeInput(string UserName, string Email, string? PhoneNumber, string TemporaryPassword, bool IsActive, IReadOnlyCollection<string> Permissions);
public sealed record UpdateEmployeeInput(string Email, string? PhoneNumber, bool IsActive, IReadOnlyCollection<string> Permissions);
public sealed record ResetEmployeePasswordInput(string TemporaryPassword);
