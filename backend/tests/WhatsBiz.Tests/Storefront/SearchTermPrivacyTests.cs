using FluentAssertions;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Storefront;

public sealed class SearchTermPrivacyTests
{
    [Theory]
    [InlineData("  wings   of fire ", "WINGS OF FIRE")]
    [InlineData("साड़ी", "साड़ी")]
    [InlineData("Cafe\u0301", "CAFÉ")]
    public void NormalizesCaseWhitespaceAndUnicodeWithoutDamagingTerms(string input, string expected) =>
        SearchTermPrivacy.ForStorage(input).Should().Be(expected);

    [Theory]
    [InlineData("call 9876543210", "[REDACTED SEARCH]")]
    [InlineData("buyer@example.com", "[REDACTED SEARCH]")]
    [InlineData("near me grocery", "[REDACTED SEARCH]")]
    [InlineData("house number 23", "[REDACTED SEARCH]")]
    public void RedactsLikelyContactAndAddressSearches(string input, string expected) =>
        SearchTermPrivacy.ForStorage(input).Should().Be(expected);
}
