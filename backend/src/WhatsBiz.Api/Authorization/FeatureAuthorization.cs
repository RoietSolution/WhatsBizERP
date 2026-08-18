using Microsoft.AspNetCore.Authorization;
using WhatsBiz.Application.Common.Interfaces;
namespace WhatsBiz.Api.Authorization;
public sealed record FeatureRequirement(string FeatureKey) : IAuthorizationRequirement;
public sealed class RequireFeatureAttribute(string featureKey) : AuthorizeAttribute(PermissionPolicyProvider.FeaturePrefix + featureKey);
public sealed class FeatureAuthorizationHandler(ICurrentUserService currentUser, IFeatureService features) : AuthorizationHandler<FeatureRequirement> { protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FeatureRequirement requirement) { if (currentUser.TenantId is Guid tenantId && await features.IsEnabledAsync(tenantId, requirement.FeatureKey)) context.Succeed(requirement); } }
