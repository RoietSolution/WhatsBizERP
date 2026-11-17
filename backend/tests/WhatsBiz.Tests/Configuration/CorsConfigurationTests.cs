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

    [Fact]
    public void VerifiedStorefrontOriginsAreAddedAndUnverifiedOnesAreExcluded()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://shop.khatadhari.com",
            ["StorefrontDomains:Mappings:0:Origin"] = "https://www.guturgo.com",
            ["StorefrontDomains:Mappings:0:StoreKey"] = "GUTURGO",
            ["StorefrontDomains:Mappings:0:Verified"] = "true",
            ["StorefrontDomains:Mappings:1:Origin"] = "https://unverified.example.com",
            ["StorefrontDomains:Mappings:1:StoreKey"] = "OTHER",
            ["StorefrontDomains:Mappings:1:Verified"] = "false",
        }).Build();

        Assert.Equal(["https://shop.khatadhari.com", "https://www.guturgo.com"], CorsConfiguration.GetAllowedOrigins(configuration));
    }
}
