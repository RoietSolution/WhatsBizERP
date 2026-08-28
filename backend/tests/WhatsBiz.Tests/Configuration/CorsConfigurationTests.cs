using Microsoft.Extensions.Configuration;
using WhatsBiz.Api.Extensions;

namespace WhatsBiz.Tests.Configuration;

public sealed class CorsConfigurationTests
{
    [Fact]
    public void CsvCompletelyOverridesTheArrayFromAppsettings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
            ["Cors:AllowedOrigins:1"] = "https://stale.example",
            ["Cors:AllowedOriginsCsv"] = "https://qa.khatadhari.com"
        }).Build();

        Assert.Equal(["https://qa.khatadhari.com"], CorsConfiguration.GetAllowedOrigins(configuration));
    }

    [Fact]
    public void ArrayRemainsSupportedForLocalDevelopment()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200/"
        }).Build();

        Assert.Equal(["http://localhost:4200"], CorsConfiguration.GetAllowedOrigins(configuration));
    }
}
