using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Common.Interfaces;
public interface IAuthenticationService { Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken); Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken); Task LogoutAsync(string refreshToken, CancellationToken cancellationToken); }
