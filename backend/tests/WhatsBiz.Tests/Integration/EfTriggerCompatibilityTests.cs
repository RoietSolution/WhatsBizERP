using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Infrastructure.Persistence;

namespace WhatsBiz.Tests.Integration;

public sealed class EfTriggerCompatibilityTests
{
    [Fact]
    public void EveryRepositoryTriggeredEfTableUsesTriggerCompatibleSqlServerSaves()
    {
        using var db = ModelOnlyContext();
        var triggers = RepositoryTriggers();
        triggers.Should().Contain(trigger => trigger.Table == "core.Users")
            .And.Contain(trigger => trigger.Table == "core.UserRoles")
            .And.Contain(trigger => trigger.Table == "commerce.StorefrontConfigurations");

        var unsafeMappings = UnsafeMappings(db, triggers);

        unsafeMappings.Should().BeEmpty("SQL Server rejects bare OUTPUT for INSERT, UPDATE and DELETE on tables with enabled triggers");
    }

    [Fact]
    public void EveryRepositoryTriggerDeclarationHasAParsedTarget()
    {
        const string declaration = @"\b(?:CREATE(?:\s+OR\s+ALTER)?|ALTER)\s+TRIGGER\b";
        foreach (var path in TriggerSqlFiles())
        {
            var sql = File.ReadAllText(path);
            var expected = Regex.Matches(sql, declaration, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
            ParseTriggers(sql, Path.GetFileName(path)).Count().Should().Be(expected, $"every trigger in {path} must have an auditable target");
        }
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public void TriggerAuditDetectsEachDmlEvent(string operation)
    {
        var sql = $"CREATE OR ALTER TRIGGER [master].[TR_Products_Test] ON [master].[Products] AFTER {operation} AS BEGIN SELECT 1; END;";
        ParseTriggers(sql, "synthetic.sql").Should().ContainSingle()
            .Which.Table.Should().Be("master.Products");
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public void NewTriggerOnUnconfiguredEfTableFailsTheGuard(string operation)
    {
        using var db = ModelOnlyContext();
        var trigger = ParseTriggers($"CREATE TRIGGER master.TR_Products_Test ON master.Products AFTER {operation} AS BEGIN SELECT 1; END;", "synthetic.sql");
        UnsafeMappings(db, trigger).Should().ContainSingle().Which.Should().Contain("master.Products");
    }

    private static string[] UnsafeMappings(ApplicationDbContext db, IEnumerable<TriggerTarget> triggers)
    {
        var mapped = db.Model.GetEntityTypes()
            .Where(type => type.GetTableName() is not null)
            .ToLookup(type => TableKey(type.GetSchema() ?? "dbo", type.GetTableName()!),
                StringComparer.OrdinalIgnoreCase);
        return triggers.Where(trigger => mapped.Contains(trigger.Table))
            .SelectMany(trigger => mapped[trigger.Table]
                .Where(type => type.IsSqlOutputClauseUsed())
                .Select(type => $"{trigger.Table} ({type.DisplayName()}, {trigger.File})"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToArray();
    }

    private static ApplicationDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=ModelOnly;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static TriggerTarget[] RepositoryTriggers()
    {
        return TriggerSqlFiles()
            .SelectMany(path => ParseTriggers(File.ReadAllText(path), Path.GetFileName(path)))
            .DistinctBy(trigger => trigger.Table + ":" + trigger.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> TriggerSqlFiles()
    {
        var database = Path.Combine(RepositoryRoot(), "database", "WhatsBiz.Database");
        return Directory.EnumerateFiles(database, "*.sql", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(database, path).Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj"));
    }

    private static IEnumerable<TriggerTarget> ParseTriggers(string sql, string file)
    {
        const string identifier = @"(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_]*)";
        var pattern = $@"\b(?:CREATE(?:\s+OR\s+ALTER)?|ALTER)\s+TRIGGER\s+(?<triggerSchema>{identifier})\s*\.\s*(?<trigger>{identifier})\s+ON\s+(?<schema>{identifier})\s*\.\s*(?<table>{identifier})";
        foreach (Match match in Regex.Matches(sql, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            yield return new TriggerTarget(
                TableKey(Unquote(match.Groups["schema"].Value), Unquote(match.Groups["table"].Value)),
                TableKey(Unquote(match.Groups["triggerSchema"].Value), Unquote(match.Groups["trigger"].Value)),
                file);
    }

    private static string TableKey(string schema, string name) => $"{schema}.{name}";
    private static string Unquote(string value) => value.Trim('[', ']');
    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../"));

    private sealed record TriggerTarget(string Table, string Name, string File);
}