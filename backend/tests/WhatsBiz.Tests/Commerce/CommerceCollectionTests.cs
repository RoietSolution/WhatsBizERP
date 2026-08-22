using System.Reflection;
using FluentAssertions;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Features.CommerceCollections;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.Commerce;

public sealed class CommerceCollectionTests
{
    [Fact]
    public async Task CollectionRequiresNameAndValidDates()
    {
        var result = await new CollectionInputValidator().ValidateAsync(new CollectionInput("", null, true, -1, new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(5.5)), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(5.5))));
        result.Errors.Select(x => x.PropertyName).Should().Contain(nameof(CollectionInput.Name));
        result.Errors.Select(x => x.PropertyName).Should().Contain(nameof(CollectionInput.DisplayOrder));
        result.Errors.Should().Contain(x => x.ErrorMessage.Contains("End date"));
    }

    [Fact]
    public async Task ValidCollectionInputPassesValidation()
    {
        var result = await new CollectionInputValidator().ValidateAsync(new CollectionInput("Wedding Collection", "Festive products", true, 1, null, null));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CollectionEndpointsUseExistingProductPermissions()
    {
        foreach (var method in typeof(CommerceCollectionsController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            method.GetCustomAttributes<HasPermissionAttribute>().Should().ContainSingle();
        typeof(Permissions.Product).GetFields().Select(x => x.GetValue(null)).Should().Contain(Permissions.Product.Edit);
    }
}
