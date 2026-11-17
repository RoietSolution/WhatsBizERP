using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Api.Controllers;

[ApiController]
public sealed class StorefrontPolicyController(IConfiguration configuration) : ControllerBase
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    [PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    [HttpGet("api/platform/storefront-policies")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontPolicyAdminDto>>> ListForOwner(Guid? tenantId, CancellationToken token)
    {
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);
        if(tenantId is not null){await using var tenant=new SqlCommand("SELECT 1 FROM core.Tenants WHERE TenantId=@id AND IsActive=1;",connection);tenant.Parameters.AddWithValue("@id",tenantId.Value);if(await tenant.ExecuteScalarAsync(token) is null)return NotFound();}
        await using var command=new SqlCommand("""
            SELECT PolicyId,PolicyType,TenantId,Version,LanguageCode,Content,ContentHash,EffectiveAtUtc,IsPublished,IsActive,AcceptanceRequired,ApprovalReference,CreatedAtUtc,PublishedAtUtc
            FROM commerce.StorefrontPolicies WHERE (TenantId=@tenant OR (TenantId IS NULL AND @tenant IS NULL) OR (@tenant IS NOT NULL AND TenantId IS NULL))
            ORDER BY CASE WHEN TenantId=@tenant THEN 0 ELSE 1 END,PolicyType,CreatedAtUtc DESC;
            """,connection);
        command.Parameters.AddWithValue("@tenant",tenantId??(object)DBNull.Value);
        var rows=new List<StorefrontPolicyAdminDto>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.IsDBNull(2)?null:reader.GetGuid(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),Convert.ToHexString((byte[])reader[6]),reader.GetFieldValue<DateTimeOffset>(7),reader.GetBoolean(8),reader.GetBoolean(9),reader.GetBoolean(10),reader.IsDBNull(11)?null:reader.GetString(11),reader.GetFieldValue<DateTimeOffset>(12),reader.IsDBNull(13)?null:reader.GetFieldValue<DateTimeOffset>(13)));
        return Ok(rows);
    }

    [PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    [HttpPost("api/platform/storefront-policies/drafts")]
    public async Task<ActionResult<Guid>> CreateDraft(StorefrontPolicyCreateInput input, CancellationToken token)
    {
        var type=input.PolicyType?.Trim().ToUpperInvariant();var version=input.Version?.Trim();var language=input.LanguageCode?.Trim().ToLowerInvariant();
        if(type is not ("CUSTOMER_TERMS" or "PRIVACY_NOTICE")||!input.AcceptanceRequired||string.IsNullOrWhiteSpace(version)||version.Length>32||string.IsNullOrWhiteSpace(language)||language.Length>12||string.IsNullOrWhiteSpace(input.Content)||input.Content.Length>200_000||input.EffectiveAtUtc<=DateTimeOffset.UtcNow)return BadRequest(new{message="Provide a supported acceptance-required policy type, version, language, content and future effective date."});
        var id=Guid.NewGuid();var hash=SHA256.HashData(Encoding.Unicode.GetBytes(input.Content));
        await using var connection=new SqlConnection(ConnectionString);await connection.OpenAsync(token);
        if(input.TenantId is not null){await using var tenant=new SqlCommand("SELECT 1 FROM core.Tenants WHERE TenantId=@id AND IsActive=1;",connection);tenant.Parameters.AddWithValue("@id",input.TenantId.Value);if(await tenant.ExecuteScalarAsync(token) is null)return BadRequest(new{message="The policy tenant is not active."});}
        await using var command=new SqlCommand("INSERT commerce.StorefrontPolicies(PolicyId,PolicyType,TenantId,Version,LanguageCode,Content,ContentHash,EffectiveAtUtc,IsPublished,IsActive,AcceptanceRequired,CreatedBy) VALUES(@id,@type,@tenant,@version,@language,@content,@hash,@effective,0,0,@required,@actor);",connection);
        command.Parameters.AddWithValue("@id",id);command.Parameters.AddWithValue("@type",type);command.Parameters.AddWithValue("@tenant",input.TenantId??(object)DBNull.Value);command.Parameters.AddWithValue("@version",version);command.Parameters.AddWithValue("@language",language);command.Parameters.AddWithValue("@content",input.Content);command.Parameters.AddWithValue("@hash",hash);command.Parameters.AddWithValue("@effective",input.EffectiveAtUtc);command.Parameters.AddWithValue("@required",input.AcceptanceRequired);command.Parameters.AddWithValue("@actor",User.Identity?.Name??"ApplicationOwner");
        try{await command.ExecuteNonQueryAsync(token);return Ok(new{policyId=id});}catch(SqlException ex)when(ex.Number is 2601 or 2627){return Conflict(new{message="That policy version already exists in this scope."});}
    }

    [AllowAnonymous]
    [HttpGet("api/store/{storeKey}/customer-policies/current")]
    public async Task<ActionResult<IReadOnlyCollection<StorefrontCustomerPolicyDto>>> Current(string storeKey, CancellationToken token)
    {
        var key = storeKey.Trim().ToUpperInvariant();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT TOP(1) TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@key", key);
        if (await command.ExecuteScalarAsync(token) is not Guid tenantId) return NotFound();
        await using var policies = new SqlCommand("""
            WITH Candidates AS (
              SELECT p.PolicyId,p.PolicyType,p.Version,p.LanguageCode,p.Content,p.ContentHash,p.EffectiveAtUtc,p.IsPublished,p.AcceptanceRequired,
                ROW_NUMBER() OVER(PARTITION BY p.PolicyType ORDER BY CASE WHEN p.IsPublished=1 AND p.EffectiveAtUtc<=SYSUTCDATETIME() THEN 0 ELSE 1 END, CASE WHEN p.TenantId=@tenant THEN 0 ELSE 1 END,p.EffectiveAtUtc DESC,p.CreatedAtUtc DESC) rn
              FROM commerce.StorefrontPolicies p
              WHERE p.PolicyType IN(N'CUSTOMER_TERMS',N'PRIVACY_NOTICE') AND (p.TenantId=@tenant OR p.TenantId IS NULL)
                AND p.LanguageCode=N'en' AND p.IsPublished=1 AND p.IsActive=1 AND p.AcceptanceRequired=1 AND p.EffectiveAtUtc<=SYSUTCDATETIME()
            )
            SELECT PolicyId,PolicyType,Version,LanguageCode,Content,ContentHash,EffectiveAtUtc,IsPublished,AcceptanceRequired
            FROM Candidates WHERE rn=1;
            """, connection);
        policies.Parameters.AddWithValue("@tenant", tenantId);
        var result = new List<StorefrontCustomerPolicyDto>();
        await using var reader = await policies.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),Convert.ToHexString((byte[])reader[5]),reader.GetFieldValue<DateTimeOffset>(6),reader.GetBoolean(7),reader.GetBoolean(8)));
        return Ok(result);
    }

    [PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    [HttpPut("api/platform/storefront-policies/{policyId:guid}/draft")]
    public async Task<IActionResult> UpdateDraft(Guid policyId, StorefrontPolicyDraftInput input, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.Content) || input.Content.Length > 200_000 || string.IsNullOrWhiteSpace(input.Version) || input.Version.Length > 32) return BadRequest(new { message = "Provide a policy version and content within the supported size." });
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(input.Content));
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("UPDATE commerce.StorefrontPolicies SET Version=@version,Content=@content,ContentHash=@hash,EffectiveAtUtc=@effective,CreatedAtUtc=SYSUTCDATETIME(),CreatedBy=@actor WHERE PolicyId=@id AND IsPublished=0 AND IsActive=0;",connection);
        command.Parameters.AddWithValue("@version",input.Version.Trim());command.Parameters.AddWithValue("@content",input.Content);command.Parameters.AddWithValue("@hash",hash);command.Parameters.AddWithValue("@effective",input.EffectiveAtUtc);command.Parameters.AddWithValue("@actor",User.Identity?.Name??"ApplicationOwner");command.Parameters.AddWithValue("@id",policyId);
        try { return await command.ExecuteNonQueryAsync(token)==1?NoContent():NotFound(); }
        catch(SqlException ex) when(ex.Number is 2601 or 2627) { return Conflict(new { message="That policy version already exists in this scope." }); }
    }

    [PlatformAuthorize, HasPermission(Permissions.Features.Manage)]
    [HttpPost("api/platform/storefront-policies/{policyId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid policyId, StorefrontPolicyPublishInput input, CancellationToken token)
    {
        var approval = input.ApprovalReference?.Trim();
        if (string.IsNullOrWhiteSpace(approval) || approval.Length > 200 || approval.Contains('[') || input.EffectiveAtUtc > DateTimeOffset.UtcNow) return BadRequest(new { message="A documented approval reference and effective date no later than publication are required." });
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        await using(var check=new SqlCommand("SELECT PolicyType,TenantId,LanguageCode,Content,AcceptanceRequired FROM commerce.StorefrontPolicies WITH(UPDLOCK,HOLDLOCK) WHERE PolicyId=@id AND IsPublished=0 AND IsActive=0;",connection,transaction))
        {
            check.Parameters.AddWithValue("@id",policyId);await using var reader=await check.ExecuteReaderAsync(token);
            if(!await reader.ReadAsync(token)){await transaction.RollbackAsync(token);return NotFound();}
            var type=reader.GetString(0);var tenant=reader.IsDBNull(1)?(Guid?)null:reader.GetGuid(1);var language=reader.GetString(2);var content=reader.GetString(3);var acceptanceRequired=reader.GetBoolean(4);
            if(!acceptanceRequired){await reader.DisposeAsync();await transaction.RollbackAsync(token);return Conflict(new { message="Customer Terms and the acknowledged Privacy Policy must be acceptance-required to publish." });}
            if(HasUnresolvedPlaceholders(content)) { await reader.DisposeAsync();await transaction.RollbackAsync(token);return Conflict(new { message="Resolve all bracketed fields and placeholder markers before publishing this customer policy." }); }
            await reader.DisposeAsync();
            await using var deactivate=new SqlCommand("UPDATE commerce.StorefrontPolicies SET IsActive=0 WHERE PolicyType=@type AND ((TenantId=@tenant) OR (TenantId IS NULL AND @tenant IS NULL)) AND LanguageCode=@language AND IsActive=1;",connection,transaction);
            deactivate.Parameters.AddWithValue("@type",type);deactivate.Parameters.AddWithValue("@tenant",tenant??(object)DBNull.Value);deactivate.Parameters.AddWithValue("@language",language);await deactivate.ExecuteNonQueryAsync(token);
        }
        await using(var publish=new SqlCommand("UPDATE commerce.StorefrontPolicies SET IsPublished=1,IsActive=1,ApprovalReference=@approval,EffectiveAtUtc=@effective,PublishedAtUtc=SYSUTCDATETIME(),PublishedBy=@actor WHERE PolicyId=@id AND IsPublished=0;",connection,transaction))
        {publish.Parameters.AddWithValue("@approval",approval);publish.Parameters.AddWithValue("@effective",input.EffectiveAtUtc);publish.Parameters.AddWithValue("@actor",User.Identity?.Name??"ApplicationOwner");publish.Parameters.AddWithValue("@id",policyId);if(await publish.ExecuteNonQueryAsync(token)!=1){await transaction.RollbackAsync(token);return Conflict();}}
        await transaction.CommitAsync(token);return NoContent();
    }

    private static bool HasUnresolvedPlaceholders(string content) => content.Contains('[')
        || content.Contains("PLACEHOLDER",StringComparison.OrdinalIgnoreCase)
        || content.Contains("TO BE COMPLETED",StringComparison.OrdinalIgnoreCase)
        || content.Contains("TO BE SET",StringComparison.OrdinalIgnoreCase)
        || content.Contains("LEGAL REVIEW REQUIRED",StringComparison.OrdinalIgnoreCase);
}

public sealed record StorefrontPolicyAdminDto(Guid PolicyId,string PolicyType,Guid? TenantId,string Version,string LanguageCode,string Content,string ContentHash,DateTimeOffset EffectiveAtUtc,bool IsPublished,bool IsActive,bool AcceptanceRequired,string? ApprovalReference,DateTimeOffset CreatedAtUtc,DateTimeOffset? PublishedAtUtc);
public sealed record StorefrontPolicyCreateInput(string PolicyType,string Version,string LanguageCode,string Content,DateTimeOffset EffectiveAtUtc,Guid? TenantId,bool AcceptanceRequired=true);
public sealed record StorefrontPolicyDraftInput(string Version,string Content,DateTimeOffset EffectiveAtUtc);
public sealed record StorefrontPolicyPublishInput(string ApprovalReference,DateTimeOffset EffectiveAtUtc);
