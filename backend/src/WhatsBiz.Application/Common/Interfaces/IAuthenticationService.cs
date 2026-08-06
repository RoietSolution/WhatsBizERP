using WhatsBiz.Application.Features.Authentication.DTOs;
namespace WhatsBiz.Application.Common.Interfaces;
public interface IAuthenticationService { Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken token); Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken token); Task LogoutAsync(string refreshToken, CancellationToken token); }
