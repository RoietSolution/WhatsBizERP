using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;

namespace WhatsBiz.Infrastructure.WhatsApp;

public sealed class WhatsAppUsageBillingService(IConfiguration configuration, IFeatureService features)
    : IWhatsAppUsageBillingService
{
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task RecordAcceptedAsync(WhatsAppUsageAcceptedMessage message, CancellationToken token)
    {
        if (!message.ProviderMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(message.MetaMessageId)) return;
        var recipient = NormalizeRecipient(message.RecipientNumber);
        if (recipient is null) return;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            DECLARE @waba nvarchar(50);
            SELECT @waba=WhatsAppBusinessAccountId FROM integration.WhatsAppConfigurations
            WHERE TenantId=@tenant AND ProviderMode=N'LIVE' AND IsEnabled=1 AND ConnectionStatus=N'CONNECTED'
              AND PhoneNumberId=@phone AND NULLIF(LTRIM(RTRIM(WhatsAppBusinessAccountId)),N'') IS NOT NULL;
            IF @waba IS NULL RETURN;
            IF NOT EXISTS(SELECT 1 FROM integration.WhatsAppMessageUsage WHERE TenantId=@tenant AND MetaMessageId=@message)
              INSERT integration.WhatsAppMessageUsage(WhatsAppMessageUsageId,TenantId,WabaId,PhoneNumberId,MetaMessageId,
                RecipientNumber,MessageCategory,TemplateName,SentAt,DeliveryStatus)
              VALUES(NEWID(),@tenant,@waba,@phone,@message,@recipient,@category,@template,@sent,N'SENT');
            """, connection);
        command.Parameters.AddWithValue("@tenant", message.TenantId);
        command.Parameters.AddWithValue("@phone", message.PhoneNumberId);
        command.Parameters.AddWithValue("@message", message.MetaMessageId);
        command.Parameters.AddWithValue("@recipient", recipient);
        command.Parameters.AddWithValue("@category", WhatsAppMessageCategories.Normalize(message.MessageCategory));
        command.Parameters.AddWithValue("@template", (object?)message.TemplateName?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@sent", message.SentAt);
        try { await command.ExecuteNonQueryAsync(token); }
        catch (SqlException exception) when (exception.Number is 2601 or 2627) { }
        var storedStatuses = new List<WhatsAppUsageStatus>();
        await using (var stored = new SqlCommand("""
            SELECT PhoneNumberId,ContactNumber,MessageStatus,EventTimestamp,PricingCategory,MetaBillable,MetaPricingModel
            FROM integration.WhatsAppWebhookEvents
            WHERE TenantId=@tenant AND MetaMessageId=@message AND EventType=N'MESSAGE_STATUS'
            ORDER BY EventTimestamp,CASE MessageStatus WHEN N'delivered' THEN 2 WHEN N'read' THEN 3 ELSE 1 END,ReceivedOn;
            """, connection))
        {
            stored.Parameters.AddWithValue("@tenant", message.TenantId);
            stored.Parameters.AddWithValue("@message", message.MetaMessageId);
            await using var reader = await stored.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) storedStatuses.Add(new(message.TenantId,message.ProviderMode,
                    reader.IsDBNull(0)?message.PhoneNumberId:reader.GetString(0),message.MetaMessageId,
                    reader.IsDBNull(1)?message.RecipientNumber:reader.GetString(1),reader.IsDBNull(2)?"UNKNOWN":reader.GetString(2),
                    reader.GetDateTimeOffset(3),reader.IsDBNull(4)?null:reader.GetString(4),
                    reader.IsDBNull(5)?null:reader.GetBoolean(5),reader.IsDBNull(6)?null:reader.GetString(6)));
        }
        foreach (var storedStatus in storedStatuses) await ApplyStatusAsync(storedStatus, token);
    }

    public async Task ApplyStatusAsync(WhatsAppUsageStatus status, CancellationToken token)
    {
        if (!status.ProviderMode.Equals(WhatsAppProviderModes.Live, StringComparison.OrdinalIgnoreCase)) return;
        var normalizedStatus = NormalizeStatus(status.Status);
        var suppliedCategory = NormalizeSuppliedCategory(status.MessageCategory);
        var recipient = NormalizeRecipient(status.RecipientNumber);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        await using (var update = new SqlCommand("""
            UPDATE integration.WhatsAppMessageUsage WITH(UPDLOCK,HOLDLOCK) SET
              RecipientNumber=COALESCE(@recipient,RecipientNumber),
              MessageCategory=COALESCE(@category,MessageCategory),
              MetaBillable=COALESCE(@billable,MetaBillable),
              MetaPricingModel=COALESCE(@pricingModel,MetaPricingModel),
              DeliveredAt=CASE WHEN @status=N'DELIVERED' AND DeliveredAt IS NULL THEN @at ELSE DeliveredAt END,
              BillingPeriod=CASE WHEN @status=N'DELIVERED' AND DeliveredAt IS NULL THEN DATEFROMPARTS(YEAR(@at),MONTH(@at),1) ELSE BillingPeriod END,
              DeliveryStatus=CASE
                WHEN @status=N'READ' THEN N'READ'
                WHEN @status=N'DELIVERED' AND DeliveryStatus<>N'READ' THEN N'DELIVERED'
                WHEN @status IN(N'FAILED',N'DELETED') AND DeliveryStatus=N'SENT' THEN @status
                ELSE DeliveryStatus END,
              ModifiedOn=SYSUTCDATETIME()
            WHERE TenantId=@tenant AND MetaMessageId=@message AND PhoneNumberId=@phone;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("@tenant", status.TenantId);
            update.Parameters.AddWithValue("@message", status.MetaMessageId);
            update.Parameters.AddWithValue("@phone", status.PhoneNumberId);
            update.Parameters.AddWithValue("@recipient", (object?)recipient ?? DBNull.Value);
            update.Parameters.AddWithValue("@category", (object?)suppliedCategory ?? DBNull.Value);
            update.Parameters.AddWithValue("@billable", (object?)status.MetaBillable ?? DBNull.Value);
            update.Parameters.AddWithValue("@pricingModel", (object?)status.PricingModel?.Trim() ?? DBNull.Value);
            update.Parameters.AddWithValue("@status", normalizedStatus);
            update.Parameters.AddWithValue("@at", status.EventAt);
            if (await update.ExecuteNonQueryAsync(token) == 0) { await transaction.CommitAsync(token); return; }
        }

        UsageState? usage = null;
        await using (var read = new SqlCommand("""
            SELECT RecipientNumber,MessageCategory,DeliveredAt,MetaBillable,EstimatedMetaCost
            FROM integration.WhatsAppMessageUsage WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@tenant AND MetaMessageId=@message;
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("@tenant", status.TenantId);
            read.Parameters.AddWithValue("@message", status.MetaMessageId);
            await using var reader = await read.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) usage = new(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTimeOffset(2), reader.IsDBNull(3) ? null : reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4));
        }

        if (usage?.DeliveredAt is not null && usage.EstimatedCost is null)
        {
            if (usage.MetaBillable == false)
            {
                await SetCost(connection, transaction, status.TenantId, status.MetaMessageId, null, null, 0m, null, token);
            }
            else if (usage.Category != WhatsAppMessageCategories.Unknown)
            {
                var resolution = await ResolveMarketAndPrice(connection, transaction, usage.Recipient, usage.Category, usage.DeliveredAt.Value, token);
                if (resolution?.Price is not null)
                    await SetCost(connection, transaction, status.TenantId, status.MetaMessageId, resolution.Price.Id, resolution.Market,
                        resolution.Price.Rate, resolution.Price, token);
                else if (resolution is not null)
                    await SetMarket(connection, transaction, status.TenantId, status.MetaMessageId, resolution.Market, token);
            }
        }
        await transaction.CommitAsync(token);
    }

    public async Task<WhatsAppUsageSummary> GetSummaryAsync(Guid tenantId, int year, int month, CancellationToken token)
    {
        if (year is < 2000 or > 9999 || month is < 1 or > 12) throw new BusinessRuleException("Select a valid billing month.");
        var period = new DateTime(year, month, 1);
        var rows = new List<SummaryRow>();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("""
            SELECT MessageCategory,EstimatedMetaCost,Currency
            FROM integration.WhatsAppMessageUsage
            WHERE TenantId=@tenant AND BillingPeriod=@period AND DeliveredAt IS NOT NULL;
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@period", period);
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) rows.Add(new(reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetDecimal(1), reader.IsDBNull(2) ? null : reader.GetString(2)));

        var tenantFeatures = await features.GetTenantConfigurationAsync(tenantId, token);
        var entitled = tenantFeatures.Features.FirstOrDefault(x => x.FeatureKey.Equals(FeatureKeys.WhatsAppCommerce, StringComparison.OrdinalIgnoreCase))?.EffectiveEnabled == true;
        return BuildSummary(period, rows, tenantFeatures.PlanName, entitled);
    }

    internal static WhatsAppUsageSummary BuildSummary(DateTime period, IReadOnlyCollection<SummaryRow> rows,
        string? planName, bool entitled)
    {
        var categoryRows = WhatsAppMessageCategories.All.Select(category =>
        {
            var selected = rows.Where(x => WhatsAppMessageCategories.Normalize(x.Category) == category).ToArray();
            var breakdown = selected.Where(x => x.Cost.HasValue && !string.IsNullOrWhiteSpace(x.Currency))
                .GroupBy(x => x.Currency!, StringComparer.OrdinalIgnoreCase)
                .Select(x => new WhatsAppUsageCurrencyAmount(x.Key.ToUpperInvariant(), x.Sum(y => y.Cost!.Value))).ToArray();
            var complete = selected.All(x => x.Cost.HasValue);
            var singleCurrency = breakdown.Length == 1 ? breakdown[0].Currency : null;
            decimal? cost = complete && breakdown.Length <= 1 ? selected.Sum(x => x.Cost ?? 0m) : null;
            return new WhatsAppUsageCategorySummary(category, selected.Length, cost, singleCurrency, complete, breakdown);
        }).ToArray();
        var allComplete = rows.All(x => x.Cost.HasValue);
        var totals = rows.Where(x => x.Cost.HasValue && !string.IsNullOrWhiteSpace(x.Currency))
            .GroupBy(x => x.Currency!, StringComparer.OrdinalIgnoreCase)
            .Select(x => new WhatsAppUsageCurrencyAmount(x.Key.ToUpperInvariant(), x.Sum(y => y.Cost!.Value))).ToArray();
        var onlyCurrency = totals.Length == 1 ? totals[0].Currency : null;
        decimal? total = allComplete && totals.Length <= 1 ? rows.Sum(x => x.Cost ?? 0m) : null;
        return new(period.ToString("yyyy-MM", CultureInfo.InvariantCulture), onlyCurrency, categoryRows, total,
            allComplete, totals, new(planName, entitled));
    }

    internal static string? NormalizeRecipient(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length is >= 8 and <= 15 ? digits : null;
    }

    private static string NormalizeStatus(string? value) => value?.Trim().ToUpperInvariant() switch
    { "SENT" => "SENT", "DELIVERED" => "DELIVERED", "READ" => "READ", "FAILED" => "FAILED", "DELETED" => "DELETED", _ => "UNKNOWN" };
    private static string? NormalizeSuppliedCategory(string? value)
    { var normalized = WhatsAppMessageCategories.Normalize(value); return normalized == WhatsAppMessageCategories.Unknown ? null : normalized; }

    private static async Task<PriceResolution?> ResolveMarketAndPrice(SqlConnection connection, SqlTransaction transaction,
        string recipient, string category, DateTimeOffset deliveredAt, CancellationToken token)
    {
        var markets = new List<string>();
        await using var command = new SqlCommand("""
            WITH candidates AS
            (
              SELECT DISTINCT MarketCode,CallingCode,
                     MAX(LEN(CallingCode)) OVER() MaxPrefix
              FROM integration.MetaWhatsAppPricing
              WHERE IsActive=1 AND @recipient LIKE CallingCode+N'%'
                AND EffectiveFrom<=@at AND (EffectiveTo IS NULL OR EffectiveTo>@at)
            )
            SELECT DISTINCT MarketCode
            FROM candidates WHERE LEN(CallingCode)=MaxPrefix;
            """, connection, transaction);
        command.Parameters.AddWithValue("@recipient", recipient);
        command.Parameters.AddWithValue("@at", deliveredAt);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) markets.Add(reader.GetString(0));
        await reader.CloseAsync();
        if (markets.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1) return null;
        var market=markets[0];
        Price? price=null;
        await using var rate = new SqlCommand("""
            SELECT TOP(2) MetaWhatsAppPricingId,Currency,Rate,EffectiveFrom
            FROM integration.MetaWhatsAppPricing
            WHERE IsActive=1 AND MarketCode=@market AND MessageCategory=@category
              AND EffectiveFrom<=@at AND (EffectiveTo IS NULL OR EffectiveTo>@at)
            ORDER BY EffectiveFrom DESC;
            """,connection,transaction);
        rate.Parameters.AddWithValue("@market",market);rate.Parameters.AddWithValue("@category",category);rate.Parameters.AddWithValue("@at",deliveredAt);
        var rates=new List<Price>();await using var rateReader=await rate.ExecuteReaderAsync(token);
        while(await rateReader.ReadAsync(token))rates.Add(new(rateReader.GetGuid(0),market,rateReader.GetString(1),rateReader.GetDecimal(2),rateReader.GetDateTimeOffset(3)));
        if(rates.Count==1)price=rates[0];
        return new(market,price);
    }

    private static async Task SetMarket(SqlConnection connection,SqlTransaction transaction,Guid tenantId,string messageId,string market,CancellationToken token)
    {await using var command=new SqlCommand("UPDATE integration.WhatsAppMessageUsage SET RecipientMarket=@market,ModifiedOn=SYSUTCDATETIME() WHERE TenantId=@tenant AND MetaMessageId=@message AND RecipientMarket IS NULL;",connection,transaction);command.Parameters.AddWithValue("@market",market);command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@message",messageId);await command.ExecuteNonQueryAsync(token);}

    private static async Task SetCost(SqlConnection connection, SqlTransaction transaction, Guid tenantId,
        string messageId, Guid? pricingId, string? market, decimal cost, Price? price, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            UPDATE integration.WhatsAppMessageUsage SET RecipientMarket=@market,MetaWhatsAppPricingId=@pricing,
              EstimatedMetaCost=@cost,Currency=@currency,PricingEffectiveFrom=@effective,ModifiedOn=SYSUTCDATETIME()
            WHERE TenantId=@tenant AND MetaMessageId=@message AND EstimatedMetaCost IS NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("@market", (object?)market ?? DBNull.Value);
        command.Parameters.AddWithValue("@pricing", (object?)pricingId ?? DBNull.Value);
        command.Parameters.AddWithValue("@cost", cost);
        command.Parameters.AddWithValue("@currency", (object?)price?.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue("@effective", (object?)price?.EffectiveFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@message", messageId);
        await command.ExecuteNonQueryAsync(token);
    }

    internal sealed record SummaryRow(string Category, decimal? Cost, string? Currency);
    private sealed record UsageState(string Recipient, string Category, DateTimeOffset? DeliveredAt, bool? MetaBillable, decimal? EstimatedCost);
    private sealed record Price(Guid Id, string Market, string Currency, decimal Rate, DateTimeOffset EffectiveFrom);
    private sealed record PriceResolution(string Market,Price? Price);
}
