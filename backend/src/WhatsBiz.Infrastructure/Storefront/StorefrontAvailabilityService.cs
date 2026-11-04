using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontAvailabilityService(IConfiguration configuration, TimeProvider? timeProvider = null, IFeatureService? features = null) : IStorefrontAvailabilityService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private async Task<bool> Entitled(Guid tenant, CancellationToken token) => features is not null && await features.IsEnabledAsync(tenant, FeatureKeys.StorefrontScheduledSelling, token);
    private async Task RequireManagement(Guid tenant, CancellationToken token) { if (!await Entitled(tenant, token)) throw new BusinessRuleException("Scheduled Selling Windows is not enabled for this retailer."); }
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<Guid?> ResolveTenantIdAsync(string storeKey, CancellationToken token)
    { await using var c=new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q=new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1",c); Add(q,"@key",storeKey.Trim().ToUpperInvariant()); var value=await q.ExecuteScalarAsync(token); return value is Guid id?id:null; }

    public async Task<IReadOnlyDictionary<Guid, StorefrontProductAvailability>> GetProductAvailabilityAsync(Guid tenantId, IReadOnlyCollection<Guid> productIds, CancellationToken token)
    {
        if (!await Entitled(tenantId, token)) return new Dictionary<Guid, StorefrontProductAvailability>();
        var schedules = await ReadSchedulesAsync(tenantId, token);
        var result = new Dictionary<Guid, StorefrontProductAvailability>();
        foreach (var productId in productIds)
        {
            var matches = schedules.Where(x => MatchesEnabledSchedule(x, productId)).ToArray();
            if (matches.Length > 0) result[productId] = Resolve(matches);
        }
        return result;
    }

    private static bool MatchesEnabledSchedule(Row row, Guid productId) => row.IsEnabled && row.ProductIds.Contains(productId);

    public async Task<IReadOnlyCollection<StorefrontAvailabilityAnnouncementDto>> GetAnnouncementsAsync(Guid tenantId, CancellationToken token)
    {
        if (!await Entitled(tenantId, token)) return [];
        return (await ReadSchedulesAsync(tenantId, token)).Where(x => x.ShowAnnouncement && x.IsEnabled && x.ProductIds.Count > 0)
            .Select(ToAnnouncement).ToArray();
    }

    private StorefrontAvailabilityAnnouncementDto ToAnnouncement(Row row)
    {
        var window = ResolveSchedule(row);
        return new(row.Id, row.Name, row.Title ?? row.Name, row.Message, OrderLabel(row), DeliveryLabel(row), AnnouncementStatus(row),
            window.NextOpenAtUtc ?? window.NextCloseAtUtc, row.ProductIds.Distinct().ToArray(), window.NextOpenAtUtc, window.NextCloseAtUtc, clock.GetUtcNow(), row.TimeZoneId);
    }

    public async Task<IReadOnlyCollection<StorefrontAvailabilityAnnouncementDto>> GetCampaignWindowsAsync(Guid tenantId, CancellationToken token)
        => !await Entitled(tenantId, token) ? [] : (await ReadSchedulesAsync(tenantId, token)).Where(x => x.IsEnabled && x.ProductIds.Count > 0).Select(ToAnnouncement).ToArray();

    public async Task<IReadOnlyCollection<StorefrontAvailabilityScheduleDto>> GetSchedulesAsync(Guid tenantId, CancellationToken token)
    { await RequireManagement(tenantId, token); return (await ReadSchedulesAsync(tenantId, token)).Select(ToDto).ToArray(); }

    public async Task<StorefrontAvailabilityScheduleDto> SaveScheduleAsync(Guid tenantId, Guid? scheduleId, SaveStorefrontAvailabilityScheduleInput input, CancellationToken token)
    {
        await RequireManagement(tenantId, token);
        if (string.IsNullOrWhiteSpace(input.ScheduleName) || input.ScheduleName.Trim().Length > 150 || input.WeekdayMask is 0 or > 127)
            throw new BusinessRuleException("Enter a schedule name and at least one weekday.");
        var orderStart = ParseTime(input.OrderStartTime, "order booking start"); var orderEnd = ParseTime(input.OrderEndTime, "order booking end");
        var deliveryStart = ParseTime(input.DeliveryStartTime, "delivery start"); var deliveryEnd = ParseTime(input.DeliveryEndTime, "delivery end");
        var tz = string.IsNullOrWhiteSpace(input.TimeZoneId) ? "Asia/Kolkata" : input.TimeZoneId.Trim();
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(tz); } catch { throw new BusinessRuleException("Enter a valid IANA timezone."); }
        var id = scheduleId ?? Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString); await connection.OpenAsync(token); await using var tx = await connection.BeginTransactionAsync(token);
        await using (var command = new SqlCommand("""
            MERGE commerce.StorefrontAvailabilitySchedules AS target USING (SELECT @id ScheduleId) AS source ON target.ScheduleId=source.ScheduleId AND target.TenantId=@tenant
            WHEN MATCHED THEN UPDATE SET ScheduleName=@name,IsEnabled=@enabled,StartTime=@orderStart,EndTime=@orderEnd,OrderStartTime=@orderStart,OrderEndTime=@orderEnd,DeliveryStartTime=@deliveryStart,DeliveryEndTime=@deliveryEnd,WeekdayMask=@mask,TimeZoneId=@tz,CustomerTitle=@title,CustomerMessage=@message,ShowAnnouncement=@announce,UpdatedAt=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT(ScheduleId,TenantId,ScheduleName,IsEnabled,StartTime,EndTime,OrderStartTime,OrderEndTime,DeliveryStartTime,DeliveryEndTime,WeekdayMask,TimeZoneId,CustomerTitle,CustomerMessage,ShowAnnouncement) VALUES(@id,@tenant,@name,@enabled,@orderStart,@orderEnd,@orderStart,@orderEnd,@deliveryStart,@deliveryEnd,@mask,@tz,@title,@message,@announce);
            """, connection, (SqlTransaction)tx))
        { Add(command, "@id", id); Add(command, "@tenant", tenantId); Add(command, "@name", input.ScheduleName.Trim()); Add(command, "@enabled", input.IsEnabled); Add(command, "@orderStart", orderStart); Add(command, "@orderEnd", orderEnd); Add(command, "@deliveryStart", deliveryStart); Add(command, "@deliveryEnd", deliveryEnd); Add(command, "@mask", input.WeekdayMask); Add(command, "@tz", tz); Add(command, "@title", input.CustomerTitle); Add(command, "@message", input.CustomerMessage); Add(command, "@announce", input.ShowAnnouncement); await command.ExecuteNonQueryAsync(token); }
        await using (var command = new SqlCommand("DELETE FROM commerce.StorefrontAvailabilityScheduleProducts WHERE ScheduleId=@id AND TenantId=@tenant; DELETE FROM commerce.StorefrontAvailabilityScheduleCategories WHERE ScheduleId=@id AND TenantId=@tenant;", connection, (SqlTransaction)tx)) { Add(command, "@id", id); Add(command, "@tenant", tenantId); await command.ExecuteNonQueryAsync(token); }
        foreach (var productId in (input.ProductIds ?? []).Distinct()) await InsertAssignment(connection, (SqlTransaction)tx, "Products", id, tenantId, productId, token);
        foreach (var categoryId in (input.CategoryIds ?? []).Distinct()) await InsertAssignment(connection, (SqlTransaction)tx, "Categories", id, tenantId, categoryId, token);
        await tx.CommitAsync(token);
        return (await GetSchedulesAsync(tenantId, token)).Single(x => x.ScheduleId == id);
    }

    public async Task DeleteScheduleAsync(Guid tenantId, Guid scheduleId, CancellationToken token)
    { await RequireManagement(tenantId, token); await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token); await using var q = new SqlCommand("DELETE FROM commerce.StorefrontAvailabilitySchedules WHERE ScheduleId=@id AND TenantId=@tenant", c); Add(q, "@id", scheduleId); Add(q, "@tenant", tenantId); try { if (await q.ExecuteNonQueryAsync(token) == 0) throw new BusinessRuleException("Schedule was not found."); } catch(SqlException error) when(error.Number==547) { throw new BusinessRuleException("This schedule is still referenced. Remove its campaign links first, or disable the schedule instead."); } }

    public async Task EnsureOrderEligibleAsync(Guid tenantId, IReadOnlyCollection<Guid> productIds, CancellationToken token)
    {
        var unavailable = (await GetProductAvailabilityAsync(tenantId, productIds, token)).Values.FirstOrDefault(x => !x.CanOrderNow);
        if (unavailable is not null)
            throw new BusinessRuleException(unavailable.NextOrderOpenAtUtc.HasValue
                ? $"Ordering is closed for this product. Booking opens at {TimeZoneInfo.ConvertTime(unavailable.NextOrderOpenAtUtc.Value, Zone(unavailable.TimeZoneId!)):ddd dd MMM hh:mm tt} ({unavailable.TimeZoneId})."
                : "Ordering is closed for this product. Please try during the next booking window.");
    }

    private static async Task InsertAssignment(SqlConnection c, SqlTransaction tx, string kind, Guid scheduleId, Guid tenantId, Guid value, CancellationToken token)
    {
        var table = kind == "Products" ? "commerce.StorefrontAvailabilityScheduleProducts" : "commerce.StorefrontAvailabilityScheduleCategories";
        var column = kind == "Products" ? "ProductId" : "ProductCategoryId";
        var ownership = kind == "Products"
            ? "EXISTS(SELECT 1 FROM master.Products x WHERE x.ProductId=@value AND x.TenantId=@tenant AND x.IsActive=1 AND x.IsDeleted=0)"
            : "EXISTS(SELECT 1 FROM master.ProductCategories x JOIN master.TenantProductCategories tc ON tc.ProductCategoryId=x.ProductCategoryId AND tc.TenantId=@tenant WHERE x.ProductCategoryId=@value AND x.IsActive=1 AND x.IsDeleted=0)";
        await using var q = new SqlCommand($"INSERT {table}(ScheduleId,TenantId,{column}) SELECT @id,@tenant,@value WHERE {ownership};", c, tx);
        Add(q, "@id", scheduleId); Add(q, "@tenant", tenantId); Add(q, "@value", value);
        if (await q.ExecuteNonQueryAsync(token) == 0) throw new BusinessRuleException("One or more schedule assignments do not belong to this tenant.");
    }

    private async Task<IReadOnlyCollection<Row>> ReadSchedulesAsync(Guid tenantId, CancellationToken token)
    {
        var map = new Dictionary<Guid, Row>(); await using var c = new SqlConnection(ConnectionString); await c.OpenAsync(token);
        await using var q = new SqlCommand("""
            SELECT s.ScheduleId,s.ScheduleName,s.IsEnabled,s.OrderStartTime,s.OrderEndTime,s.DeliveryStartTime,s.DeliveryEndTime,s.WeekdayMask,s.TimeZoneId,s.CustomerTitle,s.CustomerMessage,s.ShowAnnouncement,p.ProductId,c.ProductCategoryId
            FROM commerce.StorefrontAvailabilitySchedules s
            LEFT JOIN commerce.StorefrontAvailabilityScheduleProducts p ON p.ScheduleId=s.ScheduleId AND p.TenantId=s.TenantId
            LEFT JOIN commerce.StorefrontAvailabilityScheduleCategories c ON c.ScheduleId=s.ScheduleId AND c.TenantId=s.TenantId
            WHERE s.TenantId=@tenant ORDER BY s.ScheduleName;
            """, c); Add(q, "@tenant", tenantId); await using var r = await q.ExecuteReaderAsync(token);
        while (await r.ReadAsync(token)) { var id=r.GetGuid(0); if(!map.TryGetValue(id,out var row)){row=new(id,r.GetString(1),r.GetBoolean(2),r.GetTimeSpan(3),r.GetTimeSpan(4),r.GetTimeSpan(5),r.GetTimeSpan(6),r.GetByte(7),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),r.IsDBNull(10)?null:r.GetString(10),r.GetBoolean(11),new(),new());map[id]=row;} if(!r.IsDBNull(12))row.ProductIds.Add(r.GetGuid(12)); if(!r.IsDBNull(13))row.CategoryIds.Add(r.GetGuid(13)); }
        foreach (var row in map.Values) row.DirectProductIds.UnionWith(row.ProductIds);
        if (map.Count > 0)
        {
            await r.DisposeAsync();
            await using var p = new SqlCommand("""
                WITH CategoryTree AS (
                    SELECT a.ScheduleId, a.TenantId, a.ProductCategoryId
                    FROM commerce.StorefrontAvailabilityScheduleCategories a
                    JOIN commerce.StorefrontAvailabilitySchedules s ON s.ScheduleId=a.ScheduleId AND s.TenantId=a.TenantId
                    WHERE a.TenantId=@tenant
                    UNION ALL
                    SELECT tree.ScheduleId, tree.TenantId, child.ProductCategoryId
                    FROM CategoryTree tree
                    JOIN master.ProductCategories child ON child.ParentCategoryId=tree.ProductCategoryId
                    WHERE child.IsActive=1 AND child.IsDeleted=0
                )
                SELECT tree.ScheduleId,p.ProductId
                FROM CategoryTree tree
                JOIN master.Products p ON p.TenantId=tree.TenantId AND p.CategoryId=tree.ProductCategoryId
                WHERE tree.TenantId=@tenant AND p.IsActive=1 AND p.IsDeleted=0
                OPTION (MAXRECURSION 100);
                """, c); Add(p, "@tenant", tenantId); await using var pr=await p.ExecuteReaderAsync(token); while(await pr.ReadAsync(token)) if(map.TryGetValue(pr.GetGuid(0),out var row)) { row.ProductIds.Add(pr.GetGuid(1)); row.CategoryCoveredProductIds.Add(pr.GetGuid(1)); }
        }
        return map.Values;
    }

    private static StorefrontAvailabilityScheduleDto ToDto(Row x) => new(x.Id,x.Name,x.IsEnabled,x.OrderStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture),x.OrderEnd.ToString(@"hh\:mm", CultureInfo.InvariantCulture),x.DeliveryStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture),x.DeliveryEnd.ToString(@"hh\:mm", CultureInfo.InvariantCulture),x.Mask,x.TimeZoneId,x.Title,x.Message,x.ShowAnnouncement,x.DirectProductIds.ToArray(),x.CategoryIds.Distinct().ToArray(),x.CategoryCoveredProductIds.ToArray());
    private StorefrontProductAvailability Resolve(IReadOnlyCollection<Row> rows)
    {
        var states = rows.Where(x => x.IsEnabled).Select(x => (row: x, state: ResolveSchedule(x))).ToArray();
        var now = clock.GetUtcNow();
        var open = states.Where(x => x.state.IsOpen).OrderBy(x => x.state.NextCloseAtUtc).FirstOrDefault();
        if (open.row is not null)
            return new("ORDERING_OPEN", open.row.Name, open.row.Title, open.row.Message, OrderLabel(open.row), DeliveryLabel(open.row), open.state.NextCloseAtUtc,
                true, "ORDERING_OPEN", null, open.state.NextCloseAtUtc, now, open.row.TimeZoneId);

        var next = states.Where(x => x.state.NextOpenAtUtc.HasValue).OrderBy(x => x.state.NextOpenAtUtc).FirstOrDefault();
        return next.row is null
            ? new("ORDERING_CLOSED", CanOrderNow: false, Reason: "NO_ORDER_WINDOW", ServerNowUtc: now)
            : new("ORDERING_CLOSED", next.row.Name, next.row.Title, next.row.Message, OrderLabel(next.row), DeliveryLabel(next.row), next.state.NextOpenAtUtc,
                false, "ORDERING_CLOSED", next.state.NextOpenAtUtc, null, now, next.row.TimeZoneId);
    }

    private WindowSnapshot ResolveSchedule(Row row)
    {
        var zone = Zone(row.TimeZoneId);
        var nowUtc = clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var offset = -1; offset <= 8; offset++)
        {
            var date = local.Date.AddDays(offset);
            if ((row.Mask & (1 << (int)date.DayOfWeek)) == 0) continue;
            var start = new DateTime(date.Year, date.Month, date.Day, row.OrderStart.Hours, row.OrderStart.Minutes, 0, DateTimeKind.Unspecified);
            var endDate = row.OrderStart == row.OrderEnd || row.OrderEnd > row.OrderStart ? date : date.AddDays(1);
            var end = new DateTime(endDate.Year, endDate.Month, endDate.Day, row.OrderEnd.Hours, row.OrderEnd.Minutes, 0, DateTimeKind.Unspecified);
            var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, zone));
            var endUtc = row.OrderStart == row.OrderEnd ? startUtc.AddDays(1) : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(end, zone));
            intervals.Add((startUtc, endUtc));
        }
        var current = intervals.FirstOrDefault(x => nowUtc >= x.Start && nowUtc < x.End);
        if (current != default) return new(true, current.End, null);
        return new(false, null, intervals.Where(x => x.Start > nowUtc).OrderBy(x => x.Start).Select(x => (DateTimeOffset?)x.Start).FirstOrDefault());
    }

    private string AnnouncementStatus(Row x)
    {
        var order = ResolveSchedule(x);
        if (!order.IsOpen) return "ORDERING_CLOSED";
        var delivery = ResolveWindow(x.DeliveryStart, x.DeliveryEnd, x.Mask, x.TimeZoneId);
        return delivery.IsOpen ? "ORDERING_OPEN_DELIVERY_NOW" : "ORDERING_OPEN_DELIVERY_LATER";
    }

    private WindowSnapshot ResolveWindow(TimeSpan start, TimeSpan end, byte mask, string timeZoneId)
    {
        var row = new Row(Guid.Empty, string.Empty, true, start, end, start, end, mask, timeZoneId, null, null, false, [], []);
        return ResolveSchedule(row);
    }
    private static TimeZoneInfo Zone(string id)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && TimeZoneInfo.TryFindSystemTimeZoneById(iana, out zone)) return zone;
        throw new BusinessRuleException("Ordering availability could not be verified. Please contact the store about its configured timezone.");
    }
    private static string WindowLabel(Row x)=>$"{x.Start:hh\\:mm}–{x.End:hh\\:mm}"; private static void Add(SqlCommand q,string name,object? value)=>q.Parameters.AddWithValue(name,value??DBNull.Value);
    private static string OrderLabel(Row x)=>$"{x.OrderStart:hh\\:mm}–{x.OrderEnd:hh\\:mm}";
    private static string DeliveryLabel(Row x)=>$"{x.DeliveryStart:hh\\:mm}–{x.DeliveryEnd:hh\\:mm}";
    private static TimeSpan ParseTime(string? value,string label)=>TimeSpan.TryParse(value,CultureInfo.InvariantCulture,out var time)&&time>=TimeSpan.Zero&&time<TimeSpan.FromDays(1)?time:throw new BusinessRuleException($"Enter a valid {label} time.");
    private sealed record WindowSnapshot(bool IsOpen, DateTimeOffset? NextCloseAtUtc, DateTimeOffset? NextOpenAtUtc);
    private sealed class Row(Guid id,string name,bool enabled,TimeSpan orderStart,TimeSpan orderEnd,TimeSpan deliveryStart,TimeSpan deliveryEnd,byte mask,string tz,string? title,string? message,bool announce,List<Guid> products,List<Guid> categories){public HashSet<Guid> DirectProductIds { get; } = new();public HashSet<Guid> CategoryCoveredProductIds { get; } = new();public Guid Id=>id;public string Name=>name;public bool IsEnabled=>enabled;public TimeSpan OrderStart=>orderStart;public TimeSpan OrderEnd=>orderEnd;public TimeSpan Start=>orderStart;public TimeSpan End=>orderEnd;public TimeSpan DeliveryStart=>deliveryStart;public TimeSpan DeliveryEnd=>deliveryEnd;public byte Mask=>mask;public string TimeZoneId=>tz;public string? Title=>title;public string? Message=>message;public bool ShowAnnouncement=>announce;public List<Guid> ProductIds=>products;public List<Guid> CategoryIds=>categories;}
}
