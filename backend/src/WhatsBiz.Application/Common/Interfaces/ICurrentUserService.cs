namespace WhatsBiz.Application.Common.Interfaces;
public interface ICurrentUserService { Guid? UserId { get; } string? Username { get; } string? Email { get; } IReadOnlyCollection<string> Roles { get; } IReadOnlyCollection<string> Permissions { get; } }
