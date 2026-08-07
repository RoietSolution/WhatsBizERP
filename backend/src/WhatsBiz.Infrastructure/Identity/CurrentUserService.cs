using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WhatsBiz.Application.Common.Interfaces;
namespace WhatsBiz.Infrastructure.Identity;
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService { private ClaimsPrincipal? Principal => accessor.HttpContext?.User; public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null; public string? Username => Principal?.Identity?.Name; public string? Email => Principal?.FindFirstValue(ClaimTypes.Email); public IReadOnlyCollection<string> Roles => Principal?.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray() ?? []; public IReadOnlyCollection<string> Permissions => Principal?.FindAll(CustomClaimTypes.Permission).Select(x => x.Value).ToArray() ?? []; }
