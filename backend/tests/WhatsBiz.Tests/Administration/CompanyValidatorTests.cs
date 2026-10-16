using FluentAssertions;
using WhatsBiz.Application.Features.Administration;

namespace WhatsBiz.Tests.Administration;

public sealed class CompanyValidatorTests
{
    [Fact]
    public async Task AcceptsTwoDigitStateCode()
    {
        var result = await new CompanyValidator().ValidateAsync(new UpdateCompany(Input("22")));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Uttar Pradesh", "09")]
    [InlineData("Chhattisgarh", "22")]
    public async Task AcceptsStateSpecificGstCode(string state, string stateCode)
    {
        var result = await new CompanyValidator().ValidateAsync(new UpdateCompany(Input(stateCode, state: state)));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RejectsRecognizedStateWithMismatchedGstCode()
    {
        var result = await new CompanyValidator().ValidateAsync(new UpdateCompany(Input("22", state: "Uttar Pradesh")));
        result.Errors.Should().Contain(x => x.PropertyName == "Input.StateCode" && x.ErrorMessage == "GST State Code does not match the selected state.");
    }

    [Theory]
    [InlineData("2")]
    [InlineData("222")]
    [InlineData("AB")]
    public async Task RejectsInvalidStateCode(string stateCode)
    {
        var result = await new CompanyValidator().ValidateAsync(new UpdateCompany(Input(stateCode)));
        result.Errors.Should().Contain(x => x.PropertyName == "Input.StateCode" && x.ErrorMessage == "State code must be exactly two digits.");
    }

    [Fact]
    public async Task AcceptsSixDigitPostalCodeAsString()
    {
        var result = await new CompanyValidator().ValidateAsync(new UpdateCompany(Input("22", "012345")));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12A456")]
    public async Task RejectsInvalidPostalCode(string postalCode)
    {
        var result = await new CompanyValidator().ValidateAsync(new UpdateCompany(Input("22", postalCode)));
        result.Errors.Should().Contain(x => x.PropertyName == "Input.PostalCode" && x.ErrorMessage == "PIN Code must be exactly six digits.");
    }

    private static CompanyInput Input(string stateCode, string? postalCode = null, string? state = null) => new(
        "SHOP", "Shop", null, null, null, null, null, null, null, state, stateCode,
        "India", postalCode, null, null, null, null, null, null, null);
}
