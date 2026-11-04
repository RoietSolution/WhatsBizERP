using ClosedXML.Excel;
using FluentAssertions;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Infrastructure.Features;

namespace WhatsBiz.Tests.Administration;

public sealed class BusinessTypePolicyTests
{
    [Theory]
    [InlineData(" grocery ","GROCERY")][InlineData("home_goods","HOME_GOODS")]
    public void CodesAreNormalized(string input,string expected)=>BusinessTypePolicy.Code(input).Should().Be(expected);
    [Theory]
    [InlineData("")][InlineData("two words")][InlineData("../shop")][InlineData("1TYPE")]
    public void InvalidCodesRejected(string input){var action=()=>BusinessTypePolicy.Code(input);action.Should().Throw<BusinessRuleException>();}
    [Fact]
    public void DescriptionsArePlainSingleLineAndBounded()
    {
        BusinessTypePolicy.Description("  Your\n local   retailer ").Should().Be("Your local retailer");
        var html=()=>BusinessTypePolicy.Description("<b>Shop</b>");html.Should().Throw<BusinessRuleException>();
        var longText=()=>BusinessTypePolicy.Description(new string('x',161));longText.Should().Throw<BusinessRuleException>();
    }
    [Fact]
    public void NamesDoNotRetainDuplicateWhitespace()=>BusinessTypePolicy.Name("  Grocery   & Essentials ").Should().Be("Grocery & Essentials");
    [Fact]
    public void OnboardingRequiresBusinessTypeButAllowsEmptyDescription()
    {
        using var book=new XLWorkbook();var sheet=book.AddWorksheet("Tenant");
        var headers=new[]{"Tenant Key","Tenant Name","Display Name (Optional)","Business Type","Short Business Description (Optional)"};
        for(var i=0;i<headers.Length;i++)sheet.Cell(1,i+1).Value=headers[i];
        sheet.Cell(2,1).Value="FIXTURE";sheet.Cell(2,2).Value="Fixture retailer";
        var missing=()=>TenantEnrollmentService.ReadBusinessProfile(sheet);missing.Should().Throw<BusinessRuleException>().WithMessage("*Business Type is required*");
        sheet.Cell(2,4).Value="GROCERY";var result=TenantEnrollmentService.ReadBusinessProfile(sheet);
        result.Type.Should().Be("GROCERY");result.Description.Should().BeEmpty();
    }
}
