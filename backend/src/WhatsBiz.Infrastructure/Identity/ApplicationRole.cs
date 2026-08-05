using Microsoft.AspNetCore.Identity;
namespace WhatsBiz.Infrastructure.Identity;
public sealed class ApplicationRole : IdentityRole<Guid> { public ApplicationRole(string name) : base(name) { } public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow; }
