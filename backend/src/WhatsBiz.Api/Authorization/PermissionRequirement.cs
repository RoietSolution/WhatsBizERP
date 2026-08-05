using Microsoft.AspNetCore.Authorization;
namespace WhatsBiz.Api.Authorization;
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
