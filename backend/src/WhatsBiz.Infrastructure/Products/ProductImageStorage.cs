using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Products;

public sealed class ProductImageStorageOptions
{
    public const string SectionName = "ProductImageStorage";
    public string Provider { get; set; } = ProductImageStorageProviders.S3;
    public string LocalRootPath { get; set; } = "App_Data/ProductImages";
    public S3StorageOptions S3 { get; set; } = new();
}

public sealed class S3StorageOptions
{
    public string BucketName { get; set; } = "";
    public string Region { get; set; } = "";
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; }
    public string KeyPrefix { get; set; } = "product-images";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}

internal interface IExternalProductImageStore
{
    string Provider { get; }
    string KeyPrefix { get; }
    Task StorePairAsync(string catalogKey, byte[] catalog, string thumbnailKey, byte[] thumbnail, string contentType, CancellationToken token);
    Task<byte[]?> ReadAsync(string objectKey, CancellationToken token);
    Task DeletePairAsync(string? catalogKey, string? thumbnailKey, CancellationToken token);
}

internal sealed class ProductImageStorage : IProductImageStorage
{
    private readonly IReadOnlyDictionary<string,IExternalProductImageStore> stores;
    private readonly ILogger<ProductImageStorage> logger;
    public string ActiveProvider { get; }

    public ProductImageStorage(IOptions<ProductImageStorageOptions> options, IEnumerable<IExternalProductImageStore> externalStores,
        ILogger<ProductImageStorage> logger)
    {
        ActiveProvider=options.Value.Provider.Trim().ToUpperInvariant();
        stores=externalStores.ToDictionary(x=>x.Provider,StringComparer.OrdinalIgnoreCase);this.logger=logger;
    }

    public async Task<StoredProductImage> StoreAsync(ProductImageStorageWriteRequest request,CancellationToken token)
    {
        var hash=Convert.ToHexString(SHA256.HashData(request.CatalogContent));
        if(ActiveProvider==ProductImageStorageProviders.Database)
            return new(ActiveProvider,null,null,request.CatalogContent.LongLength,request.ThumbnailContent.LongLength,hash);
        var store=Resolve(ActiveProvider);var prefix=store.KeyPrefix;
        var baseKey=$"{prefix}tenants/{request.TenantId:N}/products/{request.ProductId:N}/{request.ImageId:N}/";
        var catalogKey=baseKey+"catalog.webp";var thumbnailKey=baseKey+"thumbnail.webp";
        await store.StorePairAsync(catalogKey,request.CatalogContent,thumbnailKey,request.ThumbnailContent,request.ContentType,token);
        return new(ActiveProvider,catalogKey,thumbnailKey,request.CatalogContent.LongLength,request.ThumbnailContent.LongLength,hash);
    }

    public async Task<ProductImageStorageContent?> ReadAsync(ProductImageStorageReadRequest request,CancellationToken token)
    {
        if(request.Provider.Equals(ProductImageStorageProviders.Database,StringComparison.OrdinalIgnoreCase))
            return request.DatabaseContent.Length==0?null:new(request.DatabaseContent,request.ContentType);
        if(string.IsNullOrWhiteSpace(request.ObjectKey))return null;
        ValidateTenantKey(request.TenantId,request.ObjectKey);
        var content=await Resolve(request.Provider).ReadAsync(request.ObjectKey,token);
        return content is null?null:new(content,request.ContentType);
    }

    public async Task DeleteAsync(ProductImageStorageDeleteRequest request,CancellationToken token)
    {
        if(request.Provider.Equals(ProductImageStorageProviders.Database,StringComparison.OrdinalIgnoreCase))return;
        try
        {
            if(request.ObjectKey is not null)ValidateTenantKey(request.TenantId,request.ObjectKey);
            if(request.ThumbnailObjectKey is not null)ValidateTenantKey(request.TenantId,request.ThumbnailObjectKey);
            await Resolve(request.Provider).DeletePairAsync(request.ObjectKey,request.ThumbnailObjectKey,token);
        }
        catch(Exception exception)when(exception is not OperationCanceledException)
        {ProductImageStorageLogs.DeleteFailed(logger,exception,request.TenantId);}
    }

    private IExternalProductImageStore Resolve(string provider)=>stores.TryGetValue(provider,out var value)?value:throw new InvalidOperationException($"Product image storage provider '{provider}' is unavailable.");
    internal static void ValidateTenantKey(Guid tenantId,string key)
    {
        var normalized=key.Replace('\\','/');
        var tenantSegment=$"tenants/{tenantId:N}/";
        if(normalized.StartsWith('/')||normalized.Contains("../",StringComparison.Ordinal)||!(normalized.StartsWith(tenantSegment,StringComparison.Ordinal)||normalized.Contains('/'+tenantSegment,StringComparison.Ordinal)))
            throw new InvalidOperationException("The product image object key is invalid for the current tenant.");
    }
}

internal static partial class ProductImageStorageLogs
{
    [LoggerMessage(3201,LogLevel.Error,"Product image objects could not be removed for tenant {TenantId}. They remain eligible for orphan cleanup.")]
    public static partial void DeleteFailed(ILogger logger,Exception exception,Guid tenantId);
}

internal sealed class LocalProductImageStore : IExternalProductImageStore
{
    private readonly string root;
    public string Provider=>ProductImageStorageProviders.Local;
    public string KeyPrefix=>string.Empty;
    public LocalProductImageStore(IOptions<ProductImageStorageOptions> options,IHostEnvironment environment)
    {root=Path.GetFullPath(Path.IsPathRooted(options.Value.LocalRootPath)?options.Value.LocalRootPath:Path.Combine(environment.ContentRootPath,options.Value.LocalRootPath));}
    public async Task StorePairAsync(string catalogKey,byte[] catalog,string thumbnailKey,byte[] thumbnail,string contentType,CancellationToken token)
    {
        var catalogPath=PathFor(catalogKey);var thumbnailPath=PathFor(thumbnailKey);Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        try{await AtomicWrite(catalogPath,catalog,token);await AtomicWrite(thumbnailPath,thumbnail,token);}
        catch{TryDelete(catalogPath);TryDelete(thumbnailPath);throw;}
    }
    public async Task<byte[]?> ReadAsync(string objectKey,CancellationToken token)
    {var path=PathFor(objectKey);return File.Exists(path)?await File.ReadAllBytesAsync(path,token):null;}
    public Task DeletePairAsync(string? catalogKey,string? thumbnailKey,CancellationToken token)
    {if(catalogKey is not null)TryDelete(PathFor(catalogKey));if(thumbnailKey is not null)TryDelete(PathFor(thumbnailKey));return Task.CompletedTask;}
    private string PathFor(string key)
    {var path=Path.GetFullPath(Path.Combine(root,key.Replace('/',Path.DirectorySeparatorChar)));var prefix=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Product image path escapes the configured local root.");return path;}
    private static async Task AtomicWrite(string path,byte[] content,CancellationToken token)
    {var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllBytesAsync(temporary,content,token);File.Move(temporary,path,false);}finally{TryDelete(temporary);}}
    private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch(Exception exception)when(exception is IOException or UnauthorizedAccessException){}}
}

internal sealed class S3ProductImageStore : IExternalProductImageStore,IDisposable
{
    private readonly S3StorageOptions options;private readonly Lazy<IAmazonS3> client;
    public string Provider=>ProductImageStorageProviders.S3;
    public string KeyPrefix {get;}
    public S3ProductImageStore(IOptions<ProductImageStorageOptions> configuration)
    {options=configuration.Value.S3;KeyPrefix=string.IsNullOrWhiteSpace(options.KeyPrefix)?string.Empty:options.KeyPrefix.Trim().Trim('/')+"/";client=new(CreateClient);}
    public async Task StorePairAsync(string catalogKey,byte[] catalog,string thumbnailKey,byte[] thumbnail,string contentType,CancellationToken token)
    {
        try{await Put(catalogKey,catalog,contentType,token);await Put(thumbnailKey,thumbnail,contentType,token);}
        catch{await DeletePairAsync(catalogKey,thumbnailKey,CancellationToken.None);throw;}
    }
    public async Task<byte[]?> ReadAsync(string objectKey,CancellationToken token)
    {try{using var response=await client.Value.GetObjectAsync(options.BucketName,objectKey,token);await using var output=new MemoryStream();await response.ResponseStream.CopyToAsync(output,token);return output.ToArray();}catch(AmazonS3Exception ex)when(ex.StatusCode==System.Net.HttpStatusCode.NotFound){return null;}}
    public async Task DeletePairAsync(string? catalogKey,string? thumbnailKey,CancellationToken token)
    {var keys=new[]{catalogKey,thumbnailKey}.Where(x=>x is not null).Select(x=>new KeyVersion{Key=x!}).ToList();if(keys.Count==0)return;await client.Value.DeleteObjectsAsync(new DeleteObjectsRequest{BucketName=options.BucketName,Objects=keys},token);}
    private async Task Put(string key,byte[] content,string contentType,CancellationToken token)
    {await using var stream=new MemoryStream(content,false);await client.Value.PutObjectAsync(new PutObjectRequest{BucketName=options.BucketName,Key=key,InputStream=stream,ContentType=contentType,AutoCloseStream=false,ServerSideEncryptionMethod=ServerSideEncryptionMethod.AES256},token);}
    private IAmazonS3 CreateClient()
    {
        var config=new AmazonS3Config{ForcePathStyle=options.ForcePathStyle};
        if(!string.IsNullOrWhiteSpace(options.ServiceUrl)){config.ServiceURL=options.ServiceUrl;config.AuthenticationRegion=string.IsNullOrWhiteSpace(options.Region)?"us-east-1":options.Region;}
        else config.RegionEndpoint=RegionEndpoint.GetBySystemName(options.Region);
        return !string.IsNullOrWhiteSpace(options.AccessKey)&&!string.IsNullOrWhiteSpace(options.SecretKey)?new AmazonS3Client(new BasicAWSCredentials(options.AccessKey,options.SecretKey),config):new AmazonS3Client(config);
    }
    public void Dispose(){if(client.IsValueCreated)client.Value.Dispose();}
}
