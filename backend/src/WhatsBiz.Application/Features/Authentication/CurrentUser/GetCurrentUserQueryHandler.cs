using MediatR;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Features.Authentication.CurrentUser;
public sealed class GetCurrentUserQueryHandler(ICurrentUserService currentUser) : IRequestHandler<GetCurrentUserQuery, CurrentUserDto> { public Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken) => currentUser.UserId is Guid id ? Task.FromResult(new CurrentUserDto(id, currentUser.Username ?? string.Empty, currentUser.Email ?? string.Empty, currentUser.Roles, currentUser.Permissions)) : throw new UnauthorizedAccessException(); }
