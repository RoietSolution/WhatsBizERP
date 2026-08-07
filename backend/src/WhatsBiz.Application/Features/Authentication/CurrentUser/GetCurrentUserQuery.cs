using MediatR;
using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Features.Authentication.CurrentUser;
public sealed record GetCurrentUserQuery : IRequest<CurrentUserDto>;
