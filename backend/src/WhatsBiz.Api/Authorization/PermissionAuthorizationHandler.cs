using Microsoft.AspNetCore.Authorization; using WhatsBiz.Infrastructure.Identity;
namespace WhatsBiz.Api.Authorization;
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement> { protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement) { if (context.User.HasClaim(CustomClaimTypes.Permission, requirement.Permission)) context.Succeed(requirement); return Task.CompletedTask; } }
