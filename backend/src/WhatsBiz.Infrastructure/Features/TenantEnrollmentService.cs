using System.Data;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Identity;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Infrastructure.Features;

public sealed partial class TenantEnrollmentService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<ApplicationRole> roles,
    IFeatureService features) : ITenantEnrollmentService
{
    public async Task<byte[]> CreateTemplateAsync(CancellationToken cancellationToken = default)
    {
        var plans = await db.Database.SqlQueryRaw<PlanOption>(
            "SELECT PlanKey,Name PlanName FROM core.Plans WHERE IsActive=1 ORDER BY Name").ToListAsync(cancellationToken);
        var featureOptions = await db.Database.SqlQueryRaw<FeatureOption>(
            "SELECT FeatureKey,Name FeatureName FROM core.Features WHERE IsActive=1 ORDER BY Version,SortOrder,Name").ToListAsync(cancellationToken);

        using var book = new XLWorkbook();
        var instructions = book.AddWorksheet("Instructions");
        instructions.Cell("A1").Value = "KhataDhari QA tenant enrollment";
        instructions.Cell("A1").Style.Font.SetBold().Font.SetFontSize(16);
        var notes = new[]
        {
            "Complete row 2 in Tenant, Administrator, and Subscription.",
            "Features are optional overrides. Leave Enabled Override blank to use the selected plan default.",
            "Products are intentionally excluded; import them later from Products > Import.",
            "The temporary administrator password is sensitive. Delete the completed workbook after a successful import.",
            "Import is create-only and atomic: validation failure creates no tenant or user."
        };
        for (var index = 0; index < notes.Length; index++) instructions.Cell(index + 3, 1).Value = notes[index];
        instructions.Column(1).Width = 110;

        var tenant = Sheet(book, "Tenant", ["Tenant Key", "Tenant Name"]);
        tenant.Cell(2, 1).Value = "NEW_RETAILER";
        tenant.Cell(2, 2).Value = "New Retailer Name";

        var administrator = Sheet(book, "Administrator", ["Username", "Email", "Temporary Password"]);
        administrator.Cell(2, 1).Value = "retailer.admin";
        administrator.Cell(2, 2).Value = "admin@example.com";

        var subscription = Sheet(book, "Subscription", ["Plan Key", "Start Date", "End Date (Optional)"]);
        subscription.Cell(2, 1).Value = plans.FirstOrDefault()?.PlanKey ?? "V1_DEFAULT";
        subscription.Cell(2, 2).Value = DateTime.UtcNow.Date;
        subscription.Column(2).Style.DateFormat.Format = "yyyy-mm-dd";
        subscription.Column(3).Style.DateFormat.Format = "yyyy-mm-dd";

        var featureSheet = Sheet(book, "Features", ["Feature Key", "Enabled Override", "Feature Name"]);
        var row = 2;
        foreach (var feature in featureOptions)
        {
            featureSheet.Cell(row, 1).Value = feature.FeatureKey;
            featureSheet.Cell(row, 3).Value = feature.FeatureName;
            row++;
        }

        var planSheet = Sheet(book, "Plan Reference", ["Plan Key", "Plan Name"]);
        row = 2;
        foreach (var plan in plans)
        {
            planSheet.Cell(row, 1).Value = plan.PlanKey;
            planSheet.Cell(row, 2).Value = plan.PlanName;
            row++;
        }

        foreach (var sheet in book.Worksheets)
        {
            sheet.SheetView.FreezeRows(1);
            sheet.ColumnsUsed().AdjustToContents(10, 60);
        }
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<TenantEnrollmentResult> ImportAsync(byte[] workbook, string? actor, CancellationToken cancellationToken = default)
    {
        EnrollmentInput input;
        try { input = Read(workbook); }
        catch (BusinessRuleException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new BusinessRuleException("The tenant enrollment workbook is invalid or unreadable.");
        }

        Validate(input);
        var planId = await db.Database.SqlQuery<Guid>($"SELECT PlanId AS Value FROM core.Plans WHERE PlanKey={input.PlanKey} AND IsActive=1")
            .SingleOrDefaultAsync(cancellationToken);
        if (planId == Guid.Empty) throw new BusinessRuleException($"Plan '{input.PlanKey}' does not exist or is inactive.");
        if (await db.Database.SqlQuery<int>($"SELECT COUNT(1) AS Value FROM core.Tenants WHERE TenantKey={input.TenantKey}").SingleAsync(cancellationToken) > 0)
            throw new BusinessRuleException($"Tenant key '{input.TenantKey}' already exists.");
        if (await users.FindByNameAsync(input.Username) is not null) throw new BusinessRuleException($"Username '{input.Username}' already exists.");
        if (await users.FindByEmailAsync(input.Email) is not null) throw new BusinessRuleException($"Email '{input.Email}' already belongs to another user.");
        var administratorRole = await roles.FindByNameAsync("Administrator")
            ?? throw new BusinessRuleException("The Administrator role is missing. Start the current API once to initialize roles.");

        var planFeatures = await db.Database.SqlQuery<PlanFeatureRow>($"""
            SELECT f.FeatureId,f.FeatureKey,f.Name FeatureName,pf.IsEnabled SubscriptionAllowed
            FROM core.Features f
            LEFT JOIN core.PlanFeatures pf ON pf.FeatureId=f.FeatureId AND pf.PlanId={planId}
            WHERE f.IsActive=1
            ORDER BY f.Version,f.SortOrder,f.Name
            """).ToListAsync(cancellationToken);
        if (planFeatures.Any(x => x.SubscriptionAllowed is null))
            throw new BusinessRuleException("The selected plan is missing entitlement rows for one or more active features.");
        var available = planFeatures.ToDictionary(x => x.FeatureKey, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in input.FeatureOverrides)
        {
            if (!available.TryGetValue(entry.Key, out var feature)) throw new BusinessRuleException($"Feature '{entry.Key}' does not exist or is inactive.");
            if (entry.Value && feature.SubscriptionAllowed != true) throw new BusinessRuleException($"Feature '{entry.Key}' is not included in plan '{input.PlanKey}'.");
        }

        var tenantId = Guid.NewGuid();
        var changedBy = string.IsNullOrWhiteSpace(actor) ? "tenant-enrollment" : actor.Trim();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT core.Tenants(TenantId,TenantKey,Name,IsActive,CreatedBy)
                VALUES({tenantId},{input.TenantKey},{input.TenantName},1,{changedBy})
                """, cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT core.Subscriptions(SubscriptionId,TenantId,PlanId,StartDate,EndDate,IsActive,CreatedBy)
                VALUES({Guid.NewGuid()},{tenantId},{planId},{input.StartDate},{input.EndDate},1,{changedBy})
                """, cancellationToken);
            foreach (var feature in planFeatures)
            {
                var enabled = input.FeatureOverrides.GetValueOrDefault(feature.FeatureKey, feature.SubscriptionAllowed == true);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT core.TenantFeatures(TenantFeatureId,TenantId,FeatureId,IsEnabled,Reason,IsActive,CreatedBy)
                    VALUES({Guid.NewGuid()},{tenantId},{feature.FeatureId},{enabled},{"Initialized by QA tenant enrollment from plan " + input.PlanKey},1,{changedBy})
                    """, cancellationToken);
            }

            var user = new ApplicationUser
            {
                TenantId = tenantId,
                UserName = input.Username,
                Email = input.Email,
                EmailConfirmed = true,
                IsActive = true,
                CreatedBy = changedBy
            };
            Ensure(await users.CreateAsync(user, input.Password), "Administrator could not be created");
            Ensure(await users.AddToRoleAsync(user, administratorRole.Name!), "Administrator role could not be assigned");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        features.InvalidateAll();
        return new(tenantId, input.TenantKey, input.TenantName, input.PlanKey, input.Username, input.Email, planFeatures.Count);
    }

    private static EnrollmentInput Read(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var book = new XLWorkbook(stream);
        var tenant = RequiredSheet(book, "Tenant");
        var administrator = RequiredSheet(book, "Administrator");
        var subscription = RequiredSheet(book, "Subscription");
        var overrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (book.TryGetWorksheet("Features", out var featureSheet))
            foreach (var row in featureSheet.RowsUsed().Skip(1))
            {
                var key = row.Cell(1).GetString().Trim().ToUpperInvariant();
                var value = row.Cell(2).GetString().Trim();
                if (key.Length == 0 || value.Length == 0) continue;
                if (!overrides.TryAdd(key, Boolean(value, row.RowNumber())))
                    throw new BusinessRuleException($"Features row {row.RowNumber()}: feature '{key}' is duplicated.");
            }
        return new(
            tenant.Cell(2, 1).GetString().Trim().ToUpperInvariant(),
            tenant.Cell(2, 2).GetString().Trim(),
            administrator.Cell(2, 1).GetString().Trim(),
            administrator.Cell(2, 2).GetString().Trim(),
            administrator.Cell(2, 3).GetString(),
            subscription.Cell(2, 1).GetString().Trim().ToUpperInvariant(),
            Date(subscription.Cell(2, 2), DateTimeOffset.UtcNow),
            subscription.Cell(2, 3).IsEmpty() ? null : Date(subscription.Cell(2, 3), DateTimeOffset.UtcNow),
            overrides);
    }

    private static void Validate(EnrollmentInput input)
    {
        if (!TenantKeyPattern().IsMatch(input.TenantKey)) throw new BusinessRuleException("Tenant Key must contain only A-Z, 0-9, underscore, or hyphen.");
        if (input.TenantKey.Length > 100) throw new BusinessRuleException("Tenant Key cannot exceed 100 characters.");
        if (input.TenantName.Length is 0 or > 200) throw new BusinessRuleException("Tenant Name is required and cannot exceed 200 characters.");
        if (input.Username.Length == 0) throw new BusinessRuleException("Administrator Username is required.");
        if (input.Email.Length == 0) throw new BusinessRuleException("Administrator Email is required.");
        if (input.Password.Length == 0) throw new BusinessRuleException("Administrator Temporary Password is required.");
        if (input.PlanKey.Length == 0) throw new BusinessRuleException("Subscription Plan Key is required.");
        if (input.EndDate < input.StartDate) throw new BusinessRuleException("Subscription End Date cannot be earlier than Start Date.");
    }

    private static IXLWorksheet Sheet(XLWorkbook book, string name, string[] headers)
    {
        var sheet = book.AddWorksheet(name);
        for (var index = 0; index < headers.Length; index++) sheet.Cell(1, index + 1).Value = headers[index];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightBlue;
        return sheet;
    }
    private static IXLWorksheet RequiredSheet(XLWorkbook book, string name) =>
        book.TryGetWorksheet(name, out var sheet) ? sheet : throw new BusinessRuleException($"Required worksheet '{name}' is missing.");
    private static bool Boolean(string value, int row) => value.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "YES" or "Y" or "1" => true,
        "FALSE" or "NO" or "N" or "0" => false,
        _ => throw new BusinessRuleException($"Features row {row}: Enabled Override must be TRUE or FALSE.")
    };
    private static DateTimeOffset Date(IXLCell cell, DateTimeOffset fallback)
    {
        if (cell.IsEmpty()) return fallback;
        if (cell.TryGetValue<DateTime>(out var date)) return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
        if (DateTimeOffset.TryParse(cell.GetString(), out var parsed)) return parsed;
        throw new BusinessRuleException($"{cell.Worksheet.Name} row {cell.Address.RowNumber}: invalid date.");
    }
    private static void Ensure(IdentityResult result, string title)
    {
        if (!result.Succeeded) throw new BusinessRuleException($"{title}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }

    [GeneratedRegex("^[A-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TenantKeyPattern();
    private sealed record EnrollmentInput(string TenantKey, string TenantName, string Username, string Email, string Password, string PlanKey, DateTimeOffset StartDate, DateTimeOffset? EndDate, IReadOnlyDictionary<string, bool> FeatureOverrides);
    private sealed class PlanOption { public string PlanKey { get; set; } = ""; public string PlanName { get; set; } = ""; }
    private sealed class FeatureOption { public string FeatureKey { get; set; } = ""; public string FeatureName { get; set; } = ""; }
    private sealed class PlanFeatureRow { public Guid FeatureId { get; set; } public string FeatureKey { get; set; } = ""; public string FeatureName { get; set; } = ""; public bool? SubscriptionAllowed { get; set; } }
}
