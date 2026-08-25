using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Products;

public static class ProductImageStorageDependencyInjection
{
    public static IServiceCollection AddProductImageStorage(this IServiceCollection services,IConfiguration configuration)
    {
        services.AddOptions<ProductImageStorageOptions>()
            .Bind(configuration.GetSection(ProductImageStorageOptions.SectionName))
            .Validate(x=>ProductImageStorageProviders.All.Contains(x.Provider),"ProductImageStorage:Provider must be DATABASE, LOCAL, or S3.")
            .Validate(x=>!x.Provider.Equals(ProductImageStorageProviders.Local,StringComparison.OrdinalIgnoreCase)||!string.IsNullOrWhiteSpace(x.LocalRootPath),"ProductImageStorage:LocalRootPath is required for LOCAL storage.")
            .Validate(x=>!x.Provider.Equals(ProductImageStorageProviders.S3,StringComparison.OrdinalIgnoreCase)||(!string.IsNullOrWhiteSpace(x.S3.BucketName)&&(!string.IsNullOrWhiteSpace(x.S3.Region)||!string.IsNullOrWhiteSpace(x.S3.ServiceUrl))),"S3 storage requires a bucket and either a region or service URL.")
            .Validate(x=>string.IsNullOrWhiteSpace(x.S3.AccessKey)==string.IsNullOrWhiteSpace(x.S3.SecretKey),"S3 access key and secret key must be supplied together.")
            .ValidateOnStart();
        services.AddSingleton<IExternalProductImageStore,LocalProductImageStore>();
        services.AddSingleton<IExternalProductImageStore,S3ProductImageStore>();
        services.AddSingleton<IProductImageStorage,ProductImageStorage>();
        return services;
    }
}
