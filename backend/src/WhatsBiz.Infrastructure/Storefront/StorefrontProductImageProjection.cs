using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Suppliers;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed record ManagedStorefrontProductImage(Guid Id, bool IsPrimary);
public sealed record ExternalStorefrontProductImage(Guid Id, string Url, int DisplayOrder, bool IsPrimary);
public sealed record StorefrontProductGallery(string? PrimaryUrl, IReadOnlyCollection<StorefrontProductImageDto> Images);

public static class StorefrontProductImageProjection
{
    public static StorefrontProductGallery Project(string storeKey, Guid productId,
        IEnumerable<ExternalStorefrontProductImage> externalImages,
        IEnumerable<ManagedStorefrontProductImage> managedImages)
    {
        var external = externalImages
            .Select(image => (Image: image, SafeUrl: ShopifyCatalogImportParser.SafeHttps(image.Url)))
            .Where(image => image.SafeUrl is not null)
            .OrderBy(image => image.Image.DisplayOrder == 1 ? 0 : 1)
            .ThenBy(image => image.Image.DisplayOrder)
            .ThenBy(image => image.SafeUrl, StringComparer.Ordinal)
            .ToArray();
        var managed = managedImages.ToArray();
        var primaryExternal = external.FirstOrDefault();
        var primaryManaged = managed.FirstOrDefault(image => image.IsPrimary) ?? managed.FirstOrDefault();
        var hasManagedPrimary = managed.Any(image => image.IsPrimary);
        var primaryUrl = primaryExternal.SafeUrl
            ?? (primaryManaged is { } primary
                ? $"/api/store/{Uri.EscapeDataString(storeKey)}/products/{productId}/image?imageId={primary.Id}"
                : null);
        var images = external.Select((image, index) => new StorefrontProductImageDto(
                image.Image.Id, image.SafeUrl!, image.SafeUrl!, index == 0))
            .Concat(managed.Select((image, index) => new StorefrontProductImageDto(
                image.Id,
                $"/api/store/{Uri.EscapeDataString(storeKey)}/products/{productId}/image?imageId={image.Id}",
                $"/api/store/{Uri.EscapeDataString(storeKey)}/products/{productId}/image?imageId={image.Id}&thumbnail=true",
                primaryExternal.SafeUrl is null && (hasManagedPrimary ? image.IsPrimary : index == 0))))
            .ToArray();
        return new(primaryUrl, images);
    }
}
