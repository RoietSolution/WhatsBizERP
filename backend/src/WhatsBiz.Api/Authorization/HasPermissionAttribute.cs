using Microsoft.AspNetCore.Authorization;
namespace WhatsBiz.Api.Authorization;
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicyProvider.Prefix + permission) { }
