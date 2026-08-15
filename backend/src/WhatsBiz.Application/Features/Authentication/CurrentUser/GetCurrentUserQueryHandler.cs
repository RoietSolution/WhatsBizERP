using MediatR;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Features.Authentication.CurrentUser;
public sealed class GetCurrentUserQueryHandler(ICurrentUserService currentUser, IFeatureService? features = null) : IRequestHandler<GetCurrentUserQuery, CurrentUserDto> { public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken) { if (currentUser.UserId is not Guid id) throw new UnauthorizedAccessException(); var tenantId = currentUser.TenantId ?? Guid.Empty; var effective = features is null || tenantId == Guid.Empty ? new Dictionary<string, bool>() : await features.GetEffectiveFeaturesAsync(tenantId, cancellationToken); return new(id, tenantId, currentUser.Username ?? string.Empty, currentUser.Email ?? string.Empty, currentUser.Roles, currentUser.Permissions, effective); } }
