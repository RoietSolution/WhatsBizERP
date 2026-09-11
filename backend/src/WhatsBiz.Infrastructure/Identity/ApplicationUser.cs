using Microsoft.AspNetCore.Identity;
namespace WhatsBiz.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public string AccountType { get; set; } = AccountTypes.Retailer;
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public bool MustChangePassword { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public static class AccountTypes
{
    public const string Retailer = "RETAILER";
    public const string ApplicationOwner = "APPLICATION_OWNER";
}
