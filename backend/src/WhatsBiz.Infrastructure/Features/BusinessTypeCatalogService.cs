using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Tenants;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Features;

public sealed record BusinessTypeInput(string Code,string Name,bool IsActive,string? Version);
public static class BusinessTypePolicy
{
    public static string Name(string value)
    {
        var result=Regex.Replace(value.Trim(),@"\s+"," ");
        if(result.Length is 0 or >80||result.Contains('<')||result.Contains('>'))throw new BusinessRuleException("Business type name must be plain text, up to 80 characters.");
        return result;
    }
    public static string Code(string value)
    {
        var result=value.Trim().ToUpperInvariant();
        if(!Regex.IsMatch(result,"^[A-Z][A-Z0-9_]{0,39}$",RegexOptions.CultureInvariant))throw new BusinessRuleException("Business type code must contain 1–40 letters, numbers or underscores and start with a letter.");
        return result;
    }
    public static string Description(string? value)
    {
        var result=Regex.Replace((value??"").Trim(),@"\s+"," ");
        if(result.Length>160||result.Contains('<')||result.Contains('>'))throw new BusinessRuleException("Business description must be plain text, up to 160 characters.");
        return result;
    }
}

public sealed class BusinessTypeCatalogService(ApplicationDbContext db,ICurrentUserService user)
{
    public async Task<object> List(bool includeInactive,CancellationToken ct)
    {
        if(includeInactive)RequireOwner();
        return await db.Set<BusinessType>().AsNoTracking().Where(x=>includeInactive||x.IsActive).OrderBy(x=>x.Name)
            .Select(x=>new{x.Code,x.Name,x.IsActive,x.Version}).ToArrayAsync(ct);
    }
    public Task<BusinessType?> Resolve(string? value,bool required,CancellationToken ct)=>ResolveAsync(db,value,required,ct);
    internal static async Task<BusinessType?> ResolveAsync(ApplicationDbContext db,string? value,bool required,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(value)){if(required)throw new BusinessRuleException("Choose a Business Type from the current owner-managed catalog.");return null;}
        var normalized=BusinessTypePolicy.Name(value).ToUpperInvariant();
        var found=await db.Set<BusinessType>().AsNoTracking().SingleOrDefaultAsync(x=>x.Code==normalized,ct)
            ?? await db.Set<BusinessType>().AsNoTracking().SingleOrDefaultAsync(x=>x.NormalizedName==normalized,ct);
        if(found is null||!found.IsActive)throw new BusinessRuleException("Choose an active Business Type from the current catalog. Legacy business labels remain saved until explicitly updated.");
        return found;
    }
    public async Task Save(BusinessTypeInput input,CancellationToken ct)
    {
        RequireOwner();var code=BusinessTypePolicy.Code(input.Code);var name=BusinessTypePolicy.Name(input.Name);var normalized=name.ToUpperInvariant();
        var existing=await db.Set<BusinessType>().SingleOrDefaultAsync(x=>x.Code==code,ct);
        if((existing is null?null:Convert.ToBase64String(existing.Version))!=input.Version)throw new BusinessRuleException("Business type changed. Reload the catalog before saving.");
        if(await db.Set<BusinessType>().AnyAsync(x=>x.Code!=code&&(x.NormalizedName==normalized||x.Code==normalized||x.NormalizedName==code),ct))
            throw new BusinessRuleException("This business type name or code is already used. Reuse the existing choice.");
        if(existing is null){existing=new(){Code=code,CreatedBy=user.UserId?.ToString()};db.Add(existing);}
        existing.Name=name;existing.IsActive=input.IsActive;existing.ModifiedBy=user.UserId?.ToString();
        try{await db.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){throw new BusinessRuleException("Business type changed. Reload before saving.");}
        catch(DbUpdateException exception) when(exception.InnerException is SqlException {Number:2601 or 2627})
        {throw new BusinessRuleException("This business type code or normalized name already exists.");}
    }
    private void RequireOwner(){if(!user.Roles.Contains("ApplicationOwner",StringComparer.Ordinal))throw new System.UnauthorizedAccessException("Application Owner access is required.");}
}
