using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Features.DemoRequests;
using WhatsBiz.Infrastructure.DemoRequests;

namespace WhatsBiz.Tests.DemoRequests;

[Collection("SQL demo requests")]
public sealed class DemoRequestSqlIntegrationTests
{
    private const string ConnectionString = "Server=DESKTOP-DQ0868S;Database=WhatsBizERP;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connection Timeout=10";

    [Fact]
    public async Task SqlPersistenceReferenceAndRapidDuplicateProtectionWorkTogether()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString }).Build();
        var repository = new DemoRequestRepository(configuration, Options.Create(new DemoRequestOptions { DuplicateWindowMinutes = 5 }));
        var mobile = "91" + Random.Shared.NextInt64(7000000000, 9999999999);
        var input = new DemoRequestInput("SQL Demo Test", mobile, null, null, null, null, null, "integration-test", null, null, null, "https://khatadhari.com/", null);
        var first = await repository.CreateAsync(input, "integration-test", "198.51.100.42", "test-agent", default);
        try
        {
            first.Duplicate.Should().BeFalse();
            first.ReferenceNo.Should().MatchRegex("^KD-[0-9]{6,}$");
            var stored = await repository.GetAsync(first.Id, default);
            stored.Name.Should().Be("SQL Demo Test");
            stored.Mobile.Should().Be(mobile);
            var second = await repository.CreateAsync(input, "integration-test", "198.51.100.42", "test-agent", default);
            second.Duplicate.Should().BeTrue();
            second.Id.Should().Be(first.Id);
            second.ReferenceNo.Should().Be(first.ReferenceNo);
        }
        finally
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand("DELETE FROM marketing.DemoRequests WHERE Id=@id", connection);
            cleanup.Parameters.Add("@id", System.Data.SqlDbType.BigInt).Value = first.Id;
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition("SQL demo requests", DisableParallelization = true)]
public sealed class DemoRequestSqlCollectionDefinition;
