using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class EntityCodeService(IConfiguration configuration, ICurrentUserService currentUser) : IEntityCodeService
{
    public Task<string> NextAsync(EntityCodeKind kind, CancellationToken cancellationToken = default)
        => GetAsync(kind, true, cancellationToken);

    public Task<string> PreviewAsync(EntityCodeKind kind, CancellationToken cancellationToken = default)
        => GetAsync(kind, false, cancellationToken);

    private async Task<string> GetAsync(EntityCodeKind kind, bool advance, CancellationToken token)
    {
        var tenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required to generate codes.");
        await using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(token);
        await using var transaction = advance
            ? (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token)
            : null;
        var stem = kind.ToString().ToUpperInvariant();
        await using var command = new SqlCommand(BuildSql(kind, advance), connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@prefixKey", $"{stem}_CODE_PREFIX");
        command.Parameters.AddWithValue("@separatorKey", $"{stem}_CODE_SEPARATOR");
        command.Parameters.AddWithValue("@paddingKey", $"{stem}_CODE_PADDING");
        command.Parameters.AddWithValue("@nextKey", $"{stem}_CODE_NEXT_NUMBER");
        command.Parameters.AddWithValue("@defaultPrefix", kind switch { EntityCodeKind.Customer => "CUS", EntityCodeKind.Supplier => "SUP", _ => "PRD" });
        var result = Convert.ToString(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The entity code could not be generated.");
        if (transaction is not null) await transaction.CommitAsync(token);
        return result;
    }

    private static string BuildSql(EntityCodeKind kind, bool advance)
    {
        var source = kind switch
        {
            EntityCodeKind.Customer => "sales.Customers|CustomerCode",
            EntityCodeKind.Supplier => "purchase.Suppliers|SupplierCode",
            _ => "master.Products|ProductCode"
        };
        var parts = source.Split('|');
        return $"""
        DECLARE @company uniqueidentifier=(SELECT TOP(1) CompanyId FROM admin.Companies WHERE TenantId=@tenant AND IsActive=1 ORDER BY CreatedOn);
        IF @company IS NULL THROW 51600,N'Company is not configured for this retailer.',1;
        DECLARE @prefix nvarchar(20)=COALESCE((SELECT SettingValue FROM admin.ApplicationSettings{(advance ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty)} WHERE CompanyId=@company AND SettingKey=@prefixKey),@defaultPrefix);
        DECLARE @separator nvarchar(5)=COALESCE((SELECT SettingValue FROM admin.ApplicationSettings{(advance ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty)} WHERE CompanyId=@company AND SettingKey=@separatorKey),N'-');
        DECLARE @padding int=TRY_CONVERT(int,(SELECT SettingValue FROM admin.ApplicationSettings{(advance ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty)} WHERE CompanyId=@company AND SettingKey=@paddingKey));
        DECLARE @next bigint=TRY_CONVERT(bigint,(SELECT SettingValue FROM admin.ApplicationSettings{(advance ? " WITH(UPDLOCK,HOLDLOCK)" : string.Empty)} WHERE CompanyId=@company AND SettingKey=@nextKey));
        SET @padding=CASE WHEN @padding BETWEEN 1 AND 12 THEN @padding ELSE 6 END;
        IF @next IS NULL OR @next<1
            SELECT @next=COALESCE(MAX(TRY_CONVERT(bigint,SUBSTRING({parts[1]},LEN(@prefix)+LEN(@separator)+1,50))),0)+1
            FROM {parts[0]} WHERE TenantId=@tenant AND {parts[1]} LIKE @prefix+@separator+N'%' AND IsDeleted=0;
        {(advance ? "UPDATE admin.ApplicationSettings SET SettingValue=CONVERT(nvarchar(30),@next+1),ModifiedOn=SYSDATETIMEOFFSET(),ModifiedBy=@user WHERE CompanyId=@company AND SettingKey=@nextKey; IF @@ROWCOUNT=0 INSERT admin.ApplicationSettings(CompanyId,SettingKey,SettingValue,DataType,Category,ModifiedBy) VALUES(@company,@nextKey,CONVERT(nvarchar(30),@next+1),N'NUMBER',N'Internal',@user);" : string.Empty)}
        DECLARE @digits nvarchar(30)=CONVERT(nvarchar(30),@next);
        SELECT CONCAT(@prefix,@separator,CASE WHEN LEN(@digits)<@padding THEN REPLICATE(N'0',@padding-LEN(@digits))+@digits ELSE @digits END);
        """.Replace("@user", "COALESCE(" + (advance ? "ORIGINAL_LOGIN()" : "NULL") + ",N'system')", StringComparison.Ordinal);
    }
}
