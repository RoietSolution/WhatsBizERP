using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Api.Configuration;
using WhatsBiz.Infrastructure.DemoRequests;
using WhatsBiz.Infrastructure.Products;
using WhatsBiz.Infrastructure.Identity;

namespace WhatsBiz.Tests.Configuration;

public sealed class DeploymentConfigurationTests
{
    [Theory]
    [InlineData("WhatsBizERP_PROD", true)]
    [InlineData("WhatsBizERP", false)]
    [InlineData("WhatsBizERP_QA", false)]
    [InlineData("OtherDatabase", false)]
    public void ProductionDatabaseTargetMustBeExplicitAndExact(string database, bool accepted)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Server=prod-sql;Database={database};User Id=test;Password=test-only"
        }).Build();

        if (accepted)
            DatabaseTargetGuard.Validate("Production", configuration).InitialCatalog.Should().Be("WhatsBizERP_PROD");
        else
            FluentActions.Invoking(() => DatabaseTargetGuard.Validate("Production", configuration))
                .Should().Throw<InvalidOperationException>()
                .WithMessage("Production must use the WhatsBizERP_PROD database. Startup was stopped before accepting requests.");
    }

    [Fact]
    public void ProductionDatabaseTargetFailsWhenConnectionStringIsNotExplicit()
    {
        var configuration = new ConfigurationBuilder().Build();

        FluentActions.Invoking(() => DatabaseTargetGuard.Validate("Production", configuration))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Connection string 'DefaultConnection' is missing.");
    }

    [Fact]
    public void StandardEnvironmentProviderBindsNestedDeploymentOptions()
    {
        const string prefix = "WHATSBIZ_CONFIGURATION_TEST_";
        var values = new Dictionary<string, string>
        {
            [$"{prefix}ConnectionStrings__DefaultConnection"] = "Server=qa.test;Database=WhatsBizERP;User Id=test;Password=test-only",
            [$"{prefix}ProductImageStorage__Provider"] = "S3",
            [$"{prefix}ProductImageStorage__S3__BucketName"] = "qa-bucket",
            [$"{prefix}ProductImageStorage__S3__Region"] = "ap-south-1",
            [$"{prefix}ProductImageStorage__S3__AccessKey"] = "test-access",
            [$"{prefix}ProductImageStorage__S3__SecretKey"] = "test-secret",
            [$"{prefix}DemoRequests__Email__Enabled"] = "true",
            [$"{prefix}DemoRequests__Email__Host"] = "smtp.test",
            [$"{prefix}DemoRequests__Email__LogoUrl"] = "https://assets.example.test/logo.png",
            [$"{prefix}DemoRequests__Email__FeatureImageUrl"] = "https://assets.example.test/features.png",
            [$"{prefix}PasswordReset__FrontendBaseUrl"] = "https://qa.example.test",
            [$"{prefix}PasswordReset__TokenLifespanMinutes"] = "45"
        };

        try
        {
            foreach (var value in values) Environment.SetEnvironmentVariable(value.Key, value.Value);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var storage = configuration.GetSection(ProductImageStorageOptions.SectionName).Get<ProductImageStorageOptions>()!;
            var demoRequests = configuration.GetSection(DemoRequestOptions.SectionName).Get<DemoRequestOptions>()!;
            var passwordReset = configuration.GetSection(PasswordResetOptions.SectionName).Get<PasswordResetOptions>()!;

            configuration.GetConnectionString("DefaultConnection").Should().Contain("Server=qa.test");
            storage.Provider.Should().Be("S3");
            storage.S3.Should().BeEquivalentTo(new
            {
                BucketName = "qa-bucket",
                Region = "ap-south-1",
                AccessKey = "test-access",
                SecretKey = "test-secret"
            });
            demoRequests.Email.Enabled.Should().BeTrue();
            demoRequests.Email.Host.Should().Be("smtp.test");
            demoRequests.Email.LogoUrl.Should().Be("https://assets.example.test/logo.png");
            demoRequests.Email.FeatureImageUrl.Should().Be("https://assets.example.test/features.png");
            passwordReset.FrontendBaseUrl.Should().Be("https://qa.example.test");
            passwordReset.TokenLifespanMinutes.Should().Be(45);
        }
        finally
        {
            foreach (var value in values) Environment.SetEnvironmentVariable(value.Key, null);
        }
    }

    [Theory]
    [InlineData("appsettings.QA.json", "qa-api.khatadhari.com", "qa.khatadhari.com", "khatadhari-whatsbiz-qa")]
    [InlineData("appsettings.Production.json", "api.khatadhari.com", "app.khatadhari.com", "khatadhari-whatsbiz-prod")]
    public void HostedEnvironmentFilesContainOnlySafeDeploymentValues(
        string fileName,
        string apiHost,
        string webHost,
        string bucketName)
    {
        var path = Path.Combine(RepositoryRoot(), "backend", "src", "WhatsBiz.Api", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        root.GetProperty("AllowedHosts").GetString().Should().Be(apiHost);
        root.GetProperty("Cors").GetProperty("AllowedOriginsCsv").GetString().Should().Be($"https://{webHost}");
        root.GetProperty("ProductImageStorage").GetProperty("S3").GetProperty("BucketName").GetString().Should().Be(bucketName);
        root.GetProperty("PasswordReset").GetProperty("FrontendBaseUrl").GetString().Should().Be($"https://{webHost}");
        root.TryGetProperty("ConnectionStrings", out _).Should().BeFalse();
        File.ReadAllText(path).Should().NotContainAny(
            "AccessKey", "SecretKey", "SigningKey", "\"Password\":", "AccessToken", "AppSecret", "VerifyToken");
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../"));
}
