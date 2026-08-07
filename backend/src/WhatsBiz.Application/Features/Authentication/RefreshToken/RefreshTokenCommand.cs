using MediatR;
using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Features.Authentication.RefreshToken;
public sealed record RefreshTokenCommand(string Token) : IRequest<AuthResponse>;
