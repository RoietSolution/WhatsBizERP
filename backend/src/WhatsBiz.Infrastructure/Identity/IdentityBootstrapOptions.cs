namespace WhatsBiz.Infrastructure.Identity;

public sealed class IdentityBootstrapOptions
{
    public const string SectionName = "IdentityBootstrap";

    public BootstrapAdministratorOptions Administrator { get; init; } = new();
}

public sealed class BootstrapAdministratorOptions
{
    public bool Enabled { get; init; }
    public string TenantKey { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool IncludeSystemAdministratorRole { get; init; } = true;
    public bool ResetPasswordOnStart { get; init; }
}
