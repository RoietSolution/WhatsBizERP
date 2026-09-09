using Microsoft.AspNetCore.Authorization;

namespace WhatsBiz.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class PlatformAuthorizeAttribute : AuthorizeAttribute
{
    public PlatformAuthorizeAttribute() => Roles = "ApplicationOwner";
}
