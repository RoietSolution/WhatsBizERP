using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Infrastructure.DemoRequests;
using WhatsBiz.Infrastructure.Products;

namespace WhatsBiz.Tests.Configuration;

public sealed class DeploymentConfigurationTests
{
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
            [$"{prefix}DemoRequests__Email__Host"] = "smtp.test"
        };

        try
        {
            foreach (var value in values) Environment.SetEnvironmentVariable(value.Key, value.Value);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var storage = configuration.GetSection(ProductImageStorageOptions.SectionName).Get<ProductImageStorageOptions>()!;
            var demoRequests = configuration.GetSection(DemoRequestOptions.SectionName).Get<DemoRequestOptions>()!;

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
        root.TryGetProperty("ConnectionStrings", out _).Should().BeFalse();
        File.ReadAllText(path).Should().NotContainAny(
            "AccessKey", "SecretKey", "SigningKey", "Password", "AccessToken", "AppSecret", "VerifyToken");
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../../../"));
}
