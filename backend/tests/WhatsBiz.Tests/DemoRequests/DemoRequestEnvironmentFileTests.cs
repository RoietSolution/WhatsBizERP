using Microsoft.Extensions.Configuration;
using WhatsBiz.Api.Extensions;
using WhatsBiz.Infrastructure.DemoRequests;

namespace WhatsBiz.Tests.DemoRequests;

public sealed class DemoRequestEnvironmentFileTests
{
    [Fact]
    public void ExplicitEnvironmentFileIsLoadedIntoConfiguration()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["DemoRequests__DuplicateWindowMinutes=9"]);
            var configuration = new ConfigurationBuilder().AddKhataDhariEnvironmentFile(path).Build();

            Assert.Equal("9", configuration["DemoRequests:DuplicateWindowMinutes"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnvironmentFileNamesBindToDemoRequestOptions()
    {
        var values = EnvironmentFileConfigurationExtensions.Parse(
        [
            "# Book a Demo settings",
            "DemoRequests__Email__Enabled=true",
            "DemoRequests__Email__Host=\"smtp.khatadhari.test\"",
            "DemoRequests__Email__Password='secret=value'",
            "DemoRequests__Email__SupportAddress=support@khatadhari.test",
            "DemoRequests__WhatsAppContactNumber=919876543210"
        ]);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new DemoRequestOptions();
        configuration.GetSection(DemoRequestOptions.SectionName).Bind(options);

        Assert.True(options.Email.Enabled);
        Assert.Equal("smtp.khatadhari.test", options.Email.Host);
        Assert.Equal("secret=value", options.Email.Password);
        Assert.Equal("support@khatadhari.test", options.Email.SupportAddress);
        Assert.Equal("919876543210", options.WhatsAppContactNumber);
    }

    [Fact]
    public void InvalidEnvironmentFileLineReportsLocationWithoutItsValue()
    {
        var exception = Assert.Throws<FormatException>(() => EnvironmentFileConfigurationExtensions.Parse(
            ["DemoRequests__Email__Password"], "/etc/khatadhari/khatadhari.env"));

        Assert.Contains("line 1", exception.Message);
        Assert.DoesNotContain("Password", exception.Message);
    }
}
