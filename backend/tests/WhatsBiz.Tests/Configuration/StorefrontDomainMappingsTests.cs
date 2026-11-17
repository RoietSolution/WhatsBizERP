using Microsoft.Extensions.Configuration;
using WhatsBiz.Api.Configuration;

namespace WhatsBiz.Tests.Configuration;

public sealed class StorefrontDomainMappingsTests
{
    [Fact]
    public void ResolvesOnlyExactVerifiedOrigins()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StorefrontDomains:Mappings:0:Origin"] = "https://www.guturgo.com",
            ["StorefrontDomains:Mappings:0:StoreKey"] = "GUTURGO",
            ["StorefrontDomains:Mappings:0:Verified"] = "true",
            ["StorefrontDomains:Mappings:1:Origin"] = "https://shop.example.com",
            ["StorefrontDomains:Mappings:1:StoreKey"] = "OTHER",
            ["StorefrontDomains:Mappings:1:Verified"] = "false",
        }).Build();

        Assert.Equal("GUTURGO", StorefrontDomainMappings.ResolveStoreKey(configuration, "https://www.guturgo.com"));
        Assert.Null(StorefrontDomainMappings.ResolveStoreKey(configuration, "https://guturgo.com"));
        Assert.Null(StorefrontDomainMappings.ResolveStoreKey(configuration, "https://www.guturgo.com.attacker.test"));
        Assert.Null(StorefrontDomainMappings.ResolveStoreKey(configuration, "https://shop.example.com"));
        Assert.Null(StorefrontDomainMappings.ResolveStoreKey(configuration, "https://www.guturgo.com/path"));
    }
}
