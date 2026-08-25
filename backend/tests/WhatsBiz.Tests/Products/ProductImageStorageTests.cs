using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Products;

namespace WhatsBiz.Tests.Products;

public sealed class ProductImageStorageTests
{
    [Fact]
    public void DefaultProviderIsS3()
    {
        new ProductImageStorageOptions().Provider.Should().Be(ProductImageStorageProviders.S3);
    }

    [Fact]
    public async Task DatabaseProviderPreservesTheExistingBinaryContract()
    {
        var storage=Storage(ProductImageStorageProviders.Database,[]);
        var tenant=Guid.NewGuid();var stored=await storage.StoreAsync(new(tenant,Guid.NewGuid(),Guid.NewGuid(),[1,2,3],[4,5],"image/webp"),default);
        stored.Provider.Should().Be(ProductImageStorageProviders.Database);stored.ObjectKey.Should().BeNull();stored.ContentHash.Should().HaveLength(64);
        var content=await storage.ReadAsync(new(tenant,stored.Provider,null,[1,2,3],"image/webp"),default);
        content!.Content.Should().Equal(1,2,3);
    }

    [Fact]
    public async Task LocalProviderRoundTripsBothVariantsAndRejectsCrossTenantKeys()
    {
        var root=Path.Combine(Path.GetTempPath(),"whatsbiz-product-images-"+Guid.NewGuid().ToString("N"));
        try
        {
            var options=Options.Create(new ProductImageStorageOptions{Provider=ProductImageStorageProviders.Local,LocalRootPath=root});
            var local=new LocalProductImageStore(options,new TestEnvironment(root));var storage=new ProductImageStorage(options,[local],NullLogger<ProductImageStorage>.Instance);
            var tenant=Guid.NewGuid();var stored=await storage.StoreAsync(new(tenant,Guid.NewGuid(),Guid.NewGuid(),[10,20],[30],"image/webp"),default);
            stored.ObjectKey.Should().StartWith($"tenants/{tenant:N}/products/");
            (await storage.ReadAsync(new(tenant,stored.Provider,stored.ObjectKey,[],"image/webp"),default))!.Content.Should().Equal(10,20);
            (await storage.ReadAsync(new(tenant,stored.Provider,stored.ThumbnailObjectKey,[],"image/webp"),default))!.Content.Should().Equal(30);
            await FluentActions.Awaiting(()=>storage.ReadAsync(new(Guid.NewGuid(),stored.Provider,stored.ObjectKey,[],"image/webp"),default)).Should().ThrowAsync<InvalidOperationException>();
            await storage.DeleteAsync(new(tenant,stored.Provider,stored.ObjectKey,stored.ThumbnailObjectKey),default);
            (await storage.ReadAsync(new(tenant,stored.Provider,stored.ObjectKey,[],"image/webp"),default)).Should().BeNull();
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact]
    public async Task S3SelectionUsesPrefixedTenantKeysWithoutKeepingDatabasePayloads()
    {
        var fake=new RecordingStore();var options=Options.Create(new ProductImageStorageOptions{Provider=ProductImageStorageProviders.S3});
        var storage=new ProductImageStorage(options,[fake],NullLogger<ProductImageStorage>.Instance);var tenant=Guid.NewGuid();
        var stored=await storage.StoreAsync(new(tenant,Guid.NewGuid(),Guid.NewGuid(),[1],[2],"image/webp"),default);
        stored.Provider.Should().Be(ProductImageStorageProviders.S3);stored.ObjectKey.Should().StartWith($"product-images/tenants/{tenant:N}/products/");
        fake.Catalog.Should().Equal(1);fake.Thumbnail.Should().Equal(2);
        (await storage.ReadAsync(new(tenant,stored.Provider,stored.ObjectKey,[],"image/webp"),default))!.Content.Should().Equal(1);
    }

    [Fact]
    public void MigrationIsTransactionalBackwardCompatibleAndProviderConstrained()
    {
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!,"../../../../"));
        var sql=File.ReadAllText(Path.Combine(root,"database/WhatsBiz.Database/Scripts/V19-ProductImageStorageProviders.sql"));
        sql.Should().Contain("BEGIN TRANSACTION").And.Contain("StorageProvider=ISNULL").And.Contain("N''DATABASE''").And.Contain("N''LOCAL''").And.Contain("N''S3''").And.Contain("CK_ProductImages_ExternalKeys");
    }

    private static ProductImageStorage Storage(string provider,IEnumerable<IExternalProductImageStore> stores)
    {return new(Options.Create(new ProductImageStorageOptions{Provider=provider}),stores,NullLogger<ProductImageStorage>.Instance);}
    private static string SourceFile([CallerFilePath]string path="")=>path;
    private sealed class TestEnvironment(string contentRoot):IHostEnvironment
    {public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="WhatsBiz.Tests";public string ContentRootPath{get;set;}=contentRoot;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
    private sealed class RecordingStore:IExternalProductImageStore
    {
        private readonly Dictionary<string,byte[]> values=[];public string Provider=>ProductImageStorageProviders.S3;public string KeyPrefix=>"product-images/";public byte[] Catalog{get;private set;}=[];public byte[] Thumbnail{get;private set;}=[];
        public Task StorePairAsync(string catalogKey,byte[] catalog,string thumbnailKey,byte[] thumbnail,string contentType,CancellationToken token){Catalog=catalog;Thumbnail=thumbnail;values[catalogKey]=catalog;values[thumbnailKey]=thumbnail;return Task.CompletedTask;}
        public Task<byte[]?> ReadAsync(string objectKey,CancellationToken token)=>Task.FromResult(values.GetValueOrDefault(objectKey));
        public Task DeletePairAsync(string? catalogKey,string? thumbnailKey,CancellationToken token){if(catalogKey is not null)values.Remove(catalogKey);if(thumbnailKey is not null)values.Remove(thumbnailKey);return Task.CompletedTask;}
    }
}
