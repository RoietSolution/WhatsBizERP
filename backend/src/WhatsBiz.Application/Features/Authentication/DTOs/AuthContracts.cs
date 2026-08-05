namespace WhatsBiz.Application.Features.Authentication.DTOs;
public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresOnUtc, CurrentUserDto User);
public sealed record CurrentUserDto(Guid UserId, string Username, string Email, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions);
