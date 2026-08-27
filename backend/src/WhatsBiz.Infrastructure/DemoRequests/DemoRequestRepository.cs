using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Data;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.DemoRequests;

namespace WhatsBiz.Infrastructure.DemoRequests;

public sealed class DemoRequestRepository(IConfiguration configuration, IOptions<DemoRequestOptions> options) : IDemoRequestRepository
{
    private SqlConnection Connection() => new(configuration.GetConnectionString("DefaultConnection"));
    private readonly int duplicateWindowMinutes = Math.Clamp(options.Value.DuplicateWindowMinutes, 1, 60);

    public async Task<DemoRequestCreateResult> CreateAsync(DemoRequestInput input, string source, string? ipAddress, string? userAgent, CancellationToken token)
    {
        await using var connection = Connection();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);

        await using (var appLock = new SqlCommand("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @result;", connection, transaction))
        {
            Parameter(appLock, "@resource", SqlDbType.NVarChar, 255, "DemoRequest:" + input.Mobile);
            var lockResult = Convert.ToInt32(await appLock.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture);
            if (lockResult < 0) throw new BusinessRuleException("Your request is already being processed. Please wait a moment and try again.");
        }

        await using (var duplicate = new SqlCommand(@"
SELECT TOP(1) Id,ReferenceNo
FROM marketing.DemoRequests WITH(UPDLOCK,HOLDLOCK)
WHERE Mobile=@mobile AND CreatedOn>=DATEADD(minute,-@window,SYSUTCDATETIME())
  AND (@ip IS NULL OR IpAddress=@ip)
ORDER BY CreatedOn DESC;", connection, transaction))
        {
            Parameter(duplicate, "@mobile", SqlDbType.NVarChar, 24, input.Mobile);
            Parameter(duplicate, "@ip", SqlDbType.NVarChar, 64, ipAddress);
            Parameter(duplicate, "@window", SqlDbType.Int, null, duplicateWindowMinutes);
            await using var reader = await duplicate.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var existing = new DemoRequestCreateResult(reader.GetInt64(0), reader.GetString(1), true);
                await reader.DisposeAsync();
                await transaction.CommitAsync(token);
                return existing;
            }
        }

        await using var insert = new SqlCommand(@"
INSERT marketing.DemoRequests
    (Name,Mobile,Email,BusinessName,City,BusinessType,Message,Source,UtmSource,UtmMedium,UtmCampaign,UtmContent,LandingPage,Referrer,IpAddress,UserAgent)
OUTPUT inserted.Id,inserted.ReferenceNo
VALUES
    (@name,@mobile,@email,@business,@city,@type,@message,@source,@utmSource,@utmMedium,@utmCampaign,@utmContent,@landing,@referrer,@ip,@agent);", connection, transaction);
        Parameter(insert, "@name", SqlDbType.NVarChar, 100, input.Name);
        Parameter(insert, "@mobile", SqlDbType.NVarChar, 24, input.Mobile);
        Parameter(insert, "@email", SqlDbType.NVarChar, 254, input.Email);
        Parameter(insert, "@business", SqlDbType.NVarChar, 150, input.BusinessName);
        Parameter(insert, "@city", SqlDbType.NVarChar, 100, input.City);
        Parameter(insert, "@type", SqlDbType.NVarChar, 100, input.BusinessType);
        Parameter(insert, "@message", SqlDbType.NVarChar, 2000, input.Message);
        Parameter(insert, "@source", SqlDbType.NVarChar, 100, source);
        Parameter(insert, "@utmSource", SqlDbType.NVarChar, 100, input.UtmSource);
        Parameter(insert, "@utmMedium", SqlDbType.NVarChar, 100, input.UtmMedium);
        Parameter(insert, "@utmCampaign", SqlDbType.NVarChar, 150, input.UtmCampaign);
        Parameter(insert, "@utmContent", SqlDbType.NVarChar, 150, input.UtmContent);
        Parameter(insert, "@landing", SqlDbType.NVarChar, 2048, input.LandingPage);
        Parameter(insert, "@referrer", SqlDbType.NVarChar, 2048, input.Referrer);
        Parameter(insert, "@ip", SqlDbType.NVarChar, 64, ipAddress);
        Parameter(insert, "@agent", SqlDbType.NVarChar, 512, userAgent);
        await using var inserted = await insert.ExecuteReaderAsync(token);
        await inserted.ReadAsync(token);
        var result = new DemoRequestCreateResult(inserted.GetInt64(0), inserted.GetString(1), false);
        await inserted.DisposeAsync();
        await transaction.CommitAsync(token);
        return result;
    }

    public async Task<PagedDemoRequests> SearchAsync(string? search, string? status, DateTimeOffset? fromDate, DateTimeOffset? toDate, int pageNumber, int pageSize, CancellationToken token)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);
        search = NullIfWhiteSpace(search);
        status = NullIfWhiteSpace(status)?.ToUpperInvariant();
        var rows = new List<DemoRequestSummary>();
        await using var connection = Connection();
        await connection.OpenAsync(token);
        const string where = @" WHERE (@search IS NULL OR ReferenceNo=@search OR Name LIKE '%' + @search + '%' OR Mobile LIKE '%' + @search + '%') AND (@status IS NULL OR Status=@status) AND (@from IS NULL OR CreatedOn>=@from) AND (@to IS NULL OR CreatedOn<DATEADD(day,1,@to))";
        await using var command = new SqlCommand("SELECT Id,ReferenceNo,Name,Mobile,BusinessName,BusinessType,City,Source,CreatedOn,Status FROM marketing.DemoRequests" + where + " ORDER BY CreatedOn DESC OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY; SELECT COUNT(*) FROM marketing.DemoRequests" + where + ";", connection);
        AddFilters(command, search, status, fromDate, toDate);
        Parameter(command, "@offset", SqlDbType.Int, null, (pageNumber - 1) * pageSize);
        Parameter(command, "@take", SqlDbType.Int, null, pageSize);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), Text(reader, 4), Text(reader, 5), Text(reader, 6), reader.GetString(7), reader.GetDateTimeOffset(8), reader.GetString(9)));
        await reader.NextResultAsync(token);
        await reader.ReadAsync(token);
        return new(rows, reader.GetInt32(0), pageNumber, pageSize);
    }

    public async Task<DemoRequestDetail> GetAsync(long id, CancellationToken token)
    {
        await using var connection = Connection();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT Id,ReferenceNo,Name,Mobile,Email,BusinessName,City,BusinessType,Message,Source,UtmSource,UtmMedium,UtmCampaign,UtmContent,LandingPage,Referrer,Status,NotificationStatus,CreatedOn,ModifiedOn FROM marketing.DemoRequests WHERE Id=@id", connection);
        Parameter(command, "@id", SqlDbType.BigInt, null, id);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new EntityNotFoundException("Demo request was not found.");
        return Map(reader);
    }

    public async Task<DemoRequestDetail> UpdateStatusAsync(long id, string status, string? user, CancellationToken token)
    {
        await using var connection = Connection();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("UPDATE marketing.DemoRequests SET Status=@status,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=@user WHERE Id=@id", connection);
        Parameter(command, "@status", SqlDbType.NVarChar, 30, status);
        Parameter(command, "@user", SqlDbType.NVarChar, 256, user);
        Parameter(command, "@id", SqlDbType.BigInt, null, id);
        if (await command.ExecuteNonQueryAsync(token) == 0) throw new EntityNotFoundException("Demo request was not found.");
        return await GetAsync(id, token);
    }

    public async Task SetNotificationStatusAsync(long id, string status, CancellationToken token)
    {
        await using var connection = Connection();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("UPDATE marketing.DemoRequests SET NotificationStatus=@status,NotificationAttemptedOn=SYSUTCDATETIME() WHERE Id=@id", connection);
        Parameter(command, "@status", SqlDbType.NVarChar, 20, status);
        Parameter(command, "@id", SqlDbType.BigInt, null, id);
        await command.ExecuteNonQueryAsync(token);
    }

    private static DemoRequestDetail Map(SqlDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), Text(r, 4), Text(r, 5), Text(r, 6), Text(r, 7), Text(r, 8), r.GetString(9), Text(r, 10), Text(r, 11), Text(r, 12), Text(r, 13), Text(r, 14), Text(r, 15), r.GetString(16), r.GetString(17), r.GetDateTimeOffset(18), r.IsDBNull(19) ? null : r.GetDateTimeOffset(19));
    private static string? Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void AddFilters(SqlCommand command, string? search, string? status, DateTimeOffset? from, DateTimeOffset? to)
    {
        Parameter(command, "@search", SqlDbType.NVarChar, 100, search);
        Parameter(command, "@status", SqlDbType.NVarChar, 30, status);
        Parameter(command, "@from", SqlDbType.DateTimeOffset, null, from);
        Parameter(command, "@to", SqlDbType.DateTimeOffset, null, to);
    }

    private static void Parameter(SqlCommand command, string name, SqlDbType type, int? size, object? value)
    {
        var parameter = size.HasValue ? command.Parameters.Add(name, type, size.Value) : command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
    }
}
