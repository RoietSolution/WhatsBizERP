using MediatR;
using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Features.Authentication.Login;
public sealed record LoginCommand(string Username, string Password) : IRequest<AuthResponse>;
