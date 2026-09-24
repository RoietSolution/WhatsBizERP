using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Capacity;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Features;

public sealed class TenantResourceLimitService(IConfiguration configuration) : ITenantResourceLimitService
{
    private SqlConnection Connection() => new(configuration.GetConnectionString("DefaultConnection"));

    public async Task<TenantCapacitySummary> GetTenantCapacitySummaryAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        return await ReadSummary(connection, null, tenantId, cancellationToken);
    }

    public async Task<TenantResourceCapacity> GetCurrentUsageAsync(Guid tenantId, string resourceType, CancellationToken cancellationToken = default)
    {
        var summary = await GetTenantCapacitySummaryAsync(tenantId, cancellationToken);
        return Normalize(resourceType) == TenantResourceTypes.Users ? summary.Users : summary.Branches;
    }

    public async Task<bool> CanCreateAsync(Guid tenantId, string resourceType, CancellationToken cancellationToken = default)
        => (await GetCurrentUsageAsync(tenantId, resourceType, cancellationToken)).CanCreate;

    public async Task EnsureCanCreateAsync(Guid tenantId, string resourceType, CancellationToken cancellationToken = default)
    {
        var capacity = await GetCurrentUsageAsync(tenantId, resourceType, cancellationToken);
        if (!capacity.CanCreate)
            throw new BusinessRuleException(capacity.ResourceType == TenantResourceTypes.Users
                ? "Your user limit has been reached. Please contact KhataDhari to add more users."
                : "Your branch limit has been reached. Please contact KhataDhari to add more branches.");
    }

    public async Task<TenantCapacitySummary> UpdateTenantCapacityAsync(Guid tenantId, UpdateTenantCapacityInput input, string? changedBy, Guid? changedByUserId, CancellationToken cancellationToken = default)
    {
        Validate(input.Users, TenantResourceTypes.Users);
        Validate(input.Branches, TenantResourceTypes.Branches);
        var actor = string.IsNullOrWhiteSpace(changedBy) ? "application-owner" : changedBy.Trim();
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await SetPlatformContext(connection, transaction, 1, cancellationToken);
            if (!await TenantExists(connection, transaction, tenantId, cancellationToken)) throw new KeyNotFoundException("Tenant was not found.");
            await Save(connection, transaction, tenantId, TenantResourceTypes.Users, input.Users, actor, changedByUserId, cancellationToken);
            await Save(connection, transaction, tenantId, TenantResourceTypes.Branches, input.Branches, actor, changedByUserId, cancellationToken);
            await SetPlatformContext(connection, transaction, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await SetPlatformContext(connection, null, null, CancellationToken.None);
            throw;
        }
        return await GetTenantCapacitySummaryAsync(tenantId, cancellationToken);
    }

    private static async Task Save(SqlConnection connection, SqlTransaction transaction, Guid tenantId, string type, TenantResourceLimitInput input, string actor, Guid? actorId, CancellationToken token)
    {
        await using var oldCommand = new SqlCommand("SELECT LimitValue FROM core.TenantResourceLimits WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant AND ResourceType=@type", connection, transaction);
        Add(oldCommand, "@tenant", tenantId); Add(oldCommand, "@type", type);
        var oldValue = await oldCommand.ExecuteScalarAsync(token);
        int? oldLimit = oldValue is null or DBNull ? null : Convert.ToInt32(oldValue, System.Globalization.CultureInfo.InvariantCulture);
        var existed = oldValue is not null;
        if (!input.Configured)
        {
            await using var delete = new SqlCommand("DELETE core.TenantResourceLimits WHERE TenantId=@tenant AND ResourceType=@type", connection, transaction);
            Add(delete, "@tenant", tenantId); Add(delete, "@type", type); await delete.ExecuteNonQueryAsync(token);
        }
        else
        {
            var limit = input.Unlimited ? null : input.Limit;
            await using var merge = new SqlCommand("""
                MERGE core.TenantResourceLimits WITH(HOLDLOCK) target
                USING(VALUES(@tenant,@type,@limit)) source(TenantId,ResourceType,LimitValue)
                ON target.TenantId=source.TenantId AND target.ResourceType=source.ResourceType
                WHEN MATCHED THEN UPDATE SET LimitValue=source.LimitValue,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@actor
                WHEN NOT MATCHED THEN INSERT(TenantId,ResourceType,LimitValue,CreatedAt,CreatedBy) VALUES(source.TenantId,source.ResourceType,source.LimitValue,SYSUTCDATETIME(),@actor);
                """, connection, transaction);
            Add(merge, "@tenant", tenantId); Add(merge, "@type", type); Add(merge, "@limit", limit); Add(merge, "@actor", actor);
            await merge.ExecuteNonQueryAsync(token);
        }
        var newLimit = input.Configured && !input.Unlimited ? input.Limit : null;
        if (existed != input.Configured || oldLimit != newLimit)
        {
            var detail = JsonSerializer.Serialize(new { resourceType = type, oldConfigured = existed, oldLimit, newConfigured = input.Configured, newLimit, unlimited = input.Configured && input.Unlimited });
            await using var audit = new SqlCommand("INSERT admin.AuditLogs(UserId,UserName,TargetTenantId,Action,EntityType,EntityId,Details,Succeeded,OccurredOn) VALUES(@user,@actor,@tenant,N'CAPACITY_UPDATE',N'TenantResourceLimit',@type,@details,1,SYSUTCDATETIME())", connection, transaction);
            Add(audit, "@user", actorId); Add(audit, "@actor", actor); Add(audit, "@tenant", tenantId); Add(audit, "@type", type); Add(audit, "@details", detail);
            await audit.ExecuteNonQueryAsync(token);
        }
    }

    private static async Task<TenantCapacitySummary> ReadSummary(SqlConnection connection, SqlTransaction? transaction, Guid tenantId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT t.Name,
              (SELECT COUNT(*) FROM core.Users u WHERE u.TenantId=t.TenantId AND u.AccountType=N'RETAILER' AND u.IsActive=1 AND u.IsDeleted=0) UserCount,
              (SELECT COUNT(*) FROM admin.Branches b JOIN admin.Companies c ON c.CompanyId=b.CompanyId WHERE c.TenantId=t.TenantId AND b.IsActive=1) BranchCount,
              ul.LimitValue UserLimit,CASE WHEN ul.TenantId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END UserConfigured,
              bl.LimitValue BranchLimit,CASE WHEN bl.TenantId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END BranchConfigured
            FROM core.Tenants t
            LEFT JOIN core.TenantResourceLimits ul ON ul.TenantId=t.TenantId AND ul.ResourceType=N'USERS'
            LEFT JOIN core.TenantResourceLimits bl ON bl.TenantId=t.TenantId AND bl.ResourceType=N'BRANCHES'
            WHERE t.TenantId=@tenant
            """, connection, transaction);
        Add(command, "@tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new KeyNotFoundException("Tenant was not found.");
        var name=reader.GetString(0); var users=reader.GetInt32(1); var branches=reader.GetInt32(2);
        int? userLimit=reader.IsDBNull(3)?null:reader.GetInt32(3); var userConfigured=reader.GetBoolean(4);
        int? branchLimit=reader.IsDBNull(5)?null:reader.GetInt32(5); var branchConfigured=reader.GetBoolean(6);
        return new(tenantId,name,Capacity(TenantResourceTypes.Users,users,userLimit,userConfigured),Capacity(TenantResourceTypes.Branches,branches,branchLimit,branchConfigured));
    }

    private static TenantResourceCapacity Capacity(string type,int current,int? limit,bool configured)
    {
        var unlimited = configured && limit is null;
        var over = limit.HasValue && current > limit.Value;
        return new(type,current,limit,configured,unlimited,over,!limit.HasValue || current < limit.Value,configured ? "TENANT_OVERRIDE" : "NOT_CONFIGURED");
    }
    private static void Validate(TenantResourceLimitInput input,string type)
    {
        if (input.Configured && !input.Unlimited && (!input.Limit.HasValue || input.Limit.Value < 1)) throw new ArgumentException($"{type} limit must be at least 1.");
        if (input.Unlimited && input.Limit.HasValue) throw new ArgumentException($"{type} cannot specify both unlimited and a finite limit.");
        if (!input.Configured && (input.Unlimited || input.Limit.HasValue)) throw new ArgumentException($"{type} cannot specify a value when it is not configured.");
    }
    private static string Normalize(string type) { var normalized=type.Trim().ToUpperInvariant(); return TenantResourceTypes.All.Contains(normalized) ? normalized : throw new ArgumentException("Unknown tenant resource type."); }
    private static async Task<bool> TenantExists(SqlConnection c,SqlTransaction tx,Guid tenant,CancellationToken token){await using var q=new SqlCommand("SELECT COUNT(1) FROM core.Tenants WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@tenant",c,tx);Add(q,"@tenant",tenant);return Convert.ToInt32(await q.ExecuteScalarAsync(token),System.Globalization.CultureInfo.InvariantCulture)==1;}
    private static async Task SetPlatformContext(SqlConnection c,SqlTransaction? tx,int? value,CancellationToken token){await using var q=new SqlCommand("EXEC sys.sp_set_session_context @key=N'PlatformOperation',@value=@value",c,tx);Add(q,"@value",value);await q.ExecuteNonQueryAsync(token);}
    private static void Add(SqlCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
