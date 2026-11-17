using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using WhatsBiz.Infrastructure.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductImageCleanupSafetyTests
{
    [Fact]
    public async Task CleanupOptionsDefaultDisabledAndWorkerDoesNotCreateScopes()
    {
        var options = new ProductImageCleanupOptions();
        options.Enabled.Should().BeFalse();
        var scopes = new ThrowingScopeFactory();
        var worker = new ProductImageCleanupWorker(scopes, NullLogger<ProductImageCleanupWorker>.Instance, Options.Create(options));

        await worker.StartAsync(CancellationToken.None);
        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        scopes.CreateCalls.Should().Be(0);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void LeaseFencingMigrationIsAdditiveAndRequiresQuiescentJobs()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "../../../../"));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V94-ProductImageCleanupLeaseFencing.sql"));
        var validator = File.ReadAllText(Path.Combine(root, "database/migrations/validators/V94-ProductImageCleanupLeaseFencing.validate.sql"));
        ParseSql(migration, "V94 migration"); ParseSql(validator, "V94 validator");
        migration.Should().Contain("LeaseToken uniqueidentifier NULL")
            .And.Contain("Status=N'PROCESSING'")
            .And.Contain("THROW 94002")
            .And.Contain("WITH CHECK ADD CONSTRAINT CK_ProductImageCleanupJobs_ProcessingLease")
            .And.NotContain("DELETE FROM")
            .And.NotContain("UPDATE master.ProductImageCleanupJobs");
        validator.Should().Contain("is_not_trusted=0").And.Contain("CK_ProductImageCleanupJobs_ProcessingLease");
    }

    [Fact]
    public void V94LeaseConstraintCompilesDynamicallyAfterLeaseTokenAddition()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "../../../../"));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/V94-ProductImageCleanupLeaseFencing.sql"));
        var addColumn = migration.IndexOf("ALTER TABLE master.ProductImageCleanupJobs ADD LeaseToken", StringComparison.Ordinal);
        var addConstraint = Regex.Match(migration, @"EXEC\s*\(\s*N'(?<sql>(?:''|[^'])*)'\s*\)\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        addColumn.Should().BeGreaterOrEqualTo(0);
        addConstraint.Success.Should().BeTrue("the constraint must execute in a later dynamic batch");
        addConstraint.Index.Should().BeGreaterThan(addColumn);
        var generatedConstraint = addConstraint.Groups["sql"].Value.Replace("''", "'", StringComparison.Ordinal);
        generatedConstraint.Should().Contain("ADD CONSTRAINT CK_ProductImageCleanupJobs_ProcessingLease")
            .And.Contain("LeaseToken IS NOT NULL")
            .And.Contain("LeaseUntilUtc IS NOT NULL");
        ParseSql(generatedConstraint, "V94 dynamically compiled lease constraint");
    }

    [Fact]
    public void WorkerRenewsOnlyTheCurrentUnexpiredLeaseAndUsesRowVersionForFinalization()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "../../../../"));
        var worker = File.ReadAllText(Path.Combine(root, "backend/src/WhatsBiz.Infrastructure/Products/ProductImageCleanupWorker.cs"));
        worker.Should().Contain("LeaseToken = Guid.NewGuid()")
            .And.Contain("Status == \"PROCESSING\" && x.LeaseUntilUtc < now")
            .And.Contain("Status=N'PROCESSING' AND LeaseToken=@lease AND LeaseUntilUtc>")
            .And.Contain("context.Entry(job).Property(x => x.RowVersion).OriginalValue = snapshot.RowVersion")
            .And.Contain("LostOwnershipToken")
            .And.Contain("if (!options.Value.Enabled) return;");
    }

    private static void ParseSql(string text, string name)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(text);
        _ = parser.Parse(reader, out IList<ParseError> errors);
        errors.Should().BeEmpty("{0} must parse as SQL Server 2022 T-SQL: {1}", name,
            string.Join(Environment.NewLine, errors.Select(x => $"Line {x.Line}, column {x.Column}: {x.Message}")));
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int CreateCalls { get; private set; }
        public IServiceScope CreateScope()
        {
            CreateCalls++;
            throw new InvalidOperationException("A disabled cleanup worker must not create a processing scope.");
        }
    }
}
