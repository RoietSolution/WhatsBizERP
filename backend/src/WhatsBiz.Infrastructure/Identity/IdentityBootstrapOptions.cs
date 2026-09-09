namespace WhatsBiz.Infrastructure.Identity;

public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "IdentityBootstrap";

    public BootstrapAdministratorOptions Administrator { get; init; } = new();
    public BootstrapApplicationOwnerOptions ApplicationOwner { get; init; } = new();
}

public sealed class BootstrapAdministratorOptions
{
    public bool Enabled { get; init; }
    public string TenantKey { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool ResetPasswordOnStart { get; init; }
}

public sealed class BootstrapApplicationOwnerOptions
{
    public bool Enabled { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool ResetPasswordOnStart { get; init; }
}
