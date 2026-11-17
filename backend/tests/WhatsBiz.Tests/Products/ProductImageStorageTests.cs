using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.RegularExpressions;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
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
    public async Task StorefrontAllCategoryResourceIsAcceptedByDatabaseAndExternalStorage()
    {
        var tenant=Guid.NewGuid();var media=Guid.NewGuid();
        var database=Storage(ProductImageStorageProviders.Database,[]);
        var databaseResult=await database.StoreStorefrontAsync(new(tenant,"category-all",media,[1,2],[3],"image/webp"),default);
        databaseResult.Provider.Should().Be(ProductImageStorageProviders.Database);
        databaseResult.ContentHash.Should().HaveLength(64);
        (await database.StoreStorefrontAsync(new(tenant,"category",Guid.NewGuid(),[9],[8],"image/webp"),default)).Provider.Should().Be(ProductImageStorageProviders.Database);
        var external=new RecordingStore();var s3=Storage(ProductImageStorageProviders.S3,[external]);
        var externalResult=await s3.StoreStorefrontAsync(new(tenant,"category-all",media,[4,5],[6],"image/webp"),default);
        externalResult.ObjectKey.Should().Contain($"tenants/{tenant:N}/storefront/category-all/{media:N}/");
        external.Catalog.Should().Equal(4,5);external.Thumbnail.Should().Equal(6);
    }
    [Fact]
    public async Task CampaignMediaReusesBinaryAndTenantScopedExternalStorage()
    {
        var tenant=Guid.NewGuid();var media=Guid.NewGuid();
        var database=Storage(ProductImageStorageProviders.Database,[]);
        var saved=await database.StoreStorefrontAsync(new(tenant,"campaign",media,[1,2],[3],"image/webp"),default);
        (await database.ReadStorefrontAsync(new(tenant,saved.Provider,null,[1,2],"image/webp"),default))!.Content.Should().Equal(1,2);
        var s3=Storage(ProductImageStorageProviders.S3,[new RecordingStore()]);
        var external=await s3.StoreStorefrontAsync(new(tenant,"campaign",media,[4,5],[6],"image/webp"),default);
        external.ObjectKey.Should().Contain($"tenants/{tenant:N}/storefront/campaign/{media:N}/");
        (await s3.ReadStorefrontAsync(new(tenant,external.Provider,external.ObjectKey,[],"image/webp"),default))!.Content.Should().Equal(4,5);
        await FluentActions.Awaiting(()=>s3.ReadStorefrontAsync(new(Guid.NewGuid(),external.Provider,external.ObjectKey,[],"image/webp"),default)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LocalProviderRoundTripsBothVariantsAndRejectsCrossTenantKeys()
    {
        var root=Path.Combine(Path.GetTempPath(),"whatsbiz-product-images-"+Guid.NewGuid().ToString("N"));
        try
        {
            var options=Options.Create(new ProductImageStorageOptions{Provider=ProductImageStorageProviders.Local,LocalRootPath=root});
            var local=new LocalProductImageStore(options,new TestEnvironment(root));var storage=new ProductImageStorage(options,[local],NullLogger<ProductImageStorage>.Instance);
            var tenant=Guid.NewGuid();var product=Guid.NewGuid();var image=Guid.NewGuid();var stored=await storage.StoreAsync(new(tenant,product,image,[10,20],[30],"image/webp"),default);
            stored.ObjectKey.Should().StartWith($"tenants/{tenant:N}/products/");
            (await storage.ReadAsync(new(tenant,stored.Provider,stored.ObjectKey,[],"image/webp"),default))!.Content.Should().Equal(10,20);
            (await storage.ReadAsync(new(tenant,stored.Provider,stored.ThumbnailObjectKey,[],"image/webp"),default))!.Content.Should().Equal(30);
            await FluentActions.Awaiting(()=>storage.ReadAsync(new(Guid.NewGuid(),stored.Provider,stored.ObjectKey,[],"image/webp"),default)).Should().ThrowAsync<InvalidOperationException>();
            await storage.DeleteAsync(new(tenant,product,image,stored.Provider,stored.ObjectKey,stored.ThumbnailObjectKey),default);
            (await storage.ReadAsync(new(tenant,stored.Provider,stored.ObjectKey,[],"image/webp"),default)).Should().BeNull();
            (await storage.ReadAsync(new(tenant,stored.Provider,stored.ThumbnailObjectKey,[],"image/webp"),default)).Should().BeNull();
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

    [Fact]
    public void ManagedDeletionAcceptsOnlyGeneratedKeysForExactTenantProductAndImage()
    {
        var tenant=Guid.NewGuid();var product=Guid.NewGuid();var image=Guid.NewGuid();
        var valid=$"prefix/tenants/{tenant:N}/products/{product:N}/{image:N}/catalog.webp";
        ProductImageStorage.ValidateManagedProductKey(tenant,product,image,valid,"catalog.webp");
        FluentActions.Invoking(()=>ProductImageStorage.ValidateManagedProductKey(Guid.NewGuid(),product,image,valid,"catalog.webp")).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(()=>ProductImageStorage.ValidateManagedProductKey(tenant,Guid.NewGuid(),image,valid,"catalog.webp")).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(()=>ProductImageStorage.ValidateManagedProductKey(tenant,product,image,$"https://cdn.shopify.com/{product}.jpg","catalog.webp")).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(()=>ProductImageStorage.ValidateManagedProductKey(tenant,product,image,$"prefix/tenants/{tenant:N}/products/{product:N}/{image:N}/../logo/catalog.webp","catalog.webp")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CleanupMigrationPreservesAuditAndTenantSafetyContracts()
    {
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!,"../../../../"));
        var sql=File.ReadAllText(Path.Combine(root,"database/migrations/V93-ProductImageRetentionCleanup.sql"));
        var validator=File.ReadAllText(Path.Combine(root,"database/migrations/validators/V93-ProductImageRetentionCleanup.validate.sql"));
        sql.Should().Contain("ProductImageCleanupJobs").And.Contain("UX_ProductImageCleanupJobs_Tenant_Image").And.Contain("TenantGuard").And.Contain("FK_ProductImageCleanupJobs_Image").And.Contain("XACT_ABORT");
        sql.Should().NotContain("DELETE FROM master.Products").And.NotContain("TRUNCATE TABLE").And.NotContain("MARKETPLACE_CHECKOUT");
        validator.Should().Contain("is_not_trusted=0").And.Contain("TenantGuard");
        ParseSql(sql,"V93 migration"); ParseSql(validator,"V93 validator");
        var dynamic = Regex.Matches(sql,@"EXEC\s*\(\s*N'(?<sql>(?:''|[^'])*)'\s*\)\s*;",RegexOptions.IgnoreCase|RegexOptions.Singleline);
        dynamic.Should().HaveCount(1);
        ParseSql(dynamic[0].Groups["sql"].Value.Replace("''","'",StringComparison.Ordinal),"V93 tenant trigger");
    }

    [Fact]
    public async Task ManagedS3PairDeletionUsesExactGeneratedProductKeysAndPropagatesFailures()
    {
        var fake=new RecordingStore();var options=Options.Create(new ProductImageStorageOptions{Provider=ProductImageStorageProviders.S3});
        var storage=new ProductImageStorage(options,[fake],NullLogger<ProductImageStorage>.Instance);var tenant=Guid.NewGuid();var product=Guid.NewGuid();var image=Guid.NewGuid();
        var saved=await storage.StoreAsync(new(tenant,product,image,[1,2],[3],"image/webp"),default);
        await storage.DeleteAsync(new(tenant,product,image,saved.Provider,saved.ObjectKey,saved.ThumbnailObjectKey),default);
        (await fake.ReadAsync(saved.ObjectKey!,default)).Should().BeNull();(await fake.ReadAsync(saved.ThumbnailObjectKey!,default)).Should().BeNull();
        await FluentActions.Awaiting(()=>storage.DeleteAsync(new(tenant,product,image,saved.Provider,"https://cdn.shopify.com/product.jpg",saved.ThumbnailObjectKey),default)).Should().ThrowAsync<InvalidOperationException>();
        fake.FailDelete=true;
        await FluentActions.Awaiting(()=>storage.DeleteAsync(new(tenant,product,image,saved.Provider,saved.ObjectKey,saved.ThumbnailObjectKey),default)).Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task VersionedS3DeletesExactObjectVersionsAndDeleteMarkersAcrossPages()
    {
        var tenant=Guid.NewGuid();var product=Guid.NewGuid();var image=Guid.NewGuid();
        var catalog=$"product-images/tenants/{tenant:N}/products/{product:N}/{image:N}/catalog.webp";
        var thumbnail=$"product-images/tenants/{tenant:N}/products/{product:N}/{image:N}/thumbnail.webp";
        var fake=new FakeVersionedS3Client();
        fake.Pages.Enqueue(new ListVersionsResponse { IsTruncated=true,NextKeyMarker=catalog,NextVersionIdMarker="page1",Versions=[new(){Key=catalog,VersionId="catalog-v1"},new(){Key=catalog,VersionId="catalog-delete-marker",IsDeleteMarker=true},new(){Key=catalog+"-unrelated",VersionId="do-not-delete"}] });
        fake.Pages.Enqueue(new ListVersionsResponse { IsTruncated=false,Versions=[new(){Key=catalog,VersionId="catalog-v2"}] });
        fake.Pages.Enqueue(new ListVersionsResponse { IsTruncated=false,Versions=[new(){Key=thumbnail,VersionId="thumbnail-v1"},new(){Key=thumbnail,VersionId="thumbnail-delete-marker",IsDeleteMarker=true}] });
        var options=Options.Create(new ProductImageStorageOptions { Provider=ProductImageStorageProviders.S3,S3=new S3StorageOptions { BucketName="synthetic-test-bucket",Region="ap-south-1",KeyPrefix="product-images" } });
        using var store=new S3ProductImageStore(options,fake);
        var storage=new ProductImageStorage(options,[store],NullLogger<ProductImageStorage>.Instance);

        await storage.DeleteAsync(new(tenant,product,image,ProductImageStorageProviders.S3,catalog,thumbnail),default);

        fake.ListRequests.Should().HaveCount(3);
        fake.ListRequests[0].Prefix.Should().Be(catalog);
        fake.ListRequests[1].KeyMarker.Should().Be(catalog);
        fake.ListRequests[1].VersionIdMarker.Should().Be("page1");
        fake.ListRequests[2].Prefix.Should().Be(thumbnail);
        fake.Deleted.SelectMany(x=>x.Objects).Select(x=>x.VersionId).Should().BeEquivalentTo("catalog-v1","catalog-delete-marker","catalog-v2","thumbnail-v1","thumbnail-delete-marker");
        fake.Deleted.SelectMany(x=>x.Objects).Should().NotContain(x=>x.Key==catalog+"-unrelated");
    }

    [Fact]
    public async Task VersionedS3PartialDeleteFailureIsSurfacedForRetry()
    {
        var fake=new FakeVersionedS3Client { FailDelete=true };
        fake.Pages.Enqueue(new ListVersionsResponse { IsTruncated=false,Versions=[new(){Key="product-images/tenants/test/products/product/image/catalog.webp",VersionId="v1"}] });
        var options=Options.Create(new ProductImageStorageOptions { Provider=ProductImageStorageProviders.S3,S3=new S3StorageOptions { BucketName="synthetic-test-bucket",Region="ap-south-1" } });
        using var store=new S3ProductImageStore(options,fake);
        var storage=new ProductImageStorage(options,[store],NullLogger<ProductImageStorage>.Instance);
        var tenant=Guid.NewGuid();var product=Guid.NewGuid();var image=Guid.NewGuid();
        var key=$"product-images/tenants/{tenant:N}/products/{product:N}/{image:N}/catalog.webp";
        fake.Pages.Clear();fake.Pages.Enqueue(new ListVersionsResponse { IsTruncated=false,Versions=[new(){Key=key,VersionId="v1"}] });
        await FluentActions.Awaiting(()=>storage.DeleteAsync(new(tenant,product,image,ProductImageStorageProviders.S3,key,null),default)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task VersionedS3MissingObjectIsAnIdempotentNoOp()
    {
        var fake=new FakeVersionedS3Client();
        fake.Pages.Enqueue(new ListVersionsResponse { IsTruncated=false });
        var options=Options.Create(new ProductImageStorageOptions { Provider=ProductImageStorageProviders.S3,S3=new S3StorageOptions { BucketName="synthetic-test-bucket",Region="ap-south-1" } });
        using var store=new S3ProductImageStore(options,fake);
        var storage=new ProductImageStorage(options,[store],NullLogger<ProductImageStorage>.Instance);
        var tenant=Guid.NewGuid();var product=Guid.NewGuid();var image=Guid.NewGuid();
        var key=$"product-images/tenants/{tenant:N}/products/{product:N}/{image:N}/catalog.webp";

        await storage.DeleteAsync(new(tenant,product,image,ProductImageStorageProviders.S3,key,null),default);

        fake.ListRequests.Should().ContainSingle();fake.Deleted.Should().BeEmpty();
    }

    private static ProductImageStorage Storage(string provider,IEnumerable<IExternalProductImageStore> stores)
    {return new(Options.Create(new ProductImageStorageOptions{Provider=provider}),stores,NullLogger<ProductImageStorage>.Instance);}
    private static string SourceFile([CallerFilePath]string path="")=>path;
    private static void ParseSql(string text,string name)
    {
        var parser=new TSql160Parser(initialQuotedIdentifiers:true);using var reader=new StringReader(text);_ = parser.Parse(reader,out IList<ParseError> errors);
        errors.Should().BeEmpty("{0} must parse as SQL Server 2022 T-SQL: {1}",name,string.Join(Environment.NewLine,errors.Select(error=>$"Line {error.Line}, column {error.Column}: {error.Message}")));
    }
    private sealed class TestEnvironment(string contentRoot):IHostEnvironment
    {public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="WhatsBiz.Tests";public string ContentRootPath{get;set;}=contentRoot;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
    private sealed class RecordingStore:IExternalProductImageStore
    {
        private readonly Dictionary<string,byte[]> values=[];public string Provider=>ProductImageStorageProviders.S3;public string KeyPrefix=>"product-images/";public byte[] Catalog{get;private set;}=[];public byte[] Thumbnail{get;private set;}=[];public bool FailDelete{get;set;}
        public Task StorePairAsync(string catalogKey,byte[] catalog,string thumbnailKey,byte[] thumbnail,string contentType,CancellationToken token){Catalog=catalog;Thumbnail=thumbnail;values[catalogKey]=catalog;values[thumbnailKey]=thumbnail;return Task.CompletedTask;}
        public Task<byte[]?> ReadAsync(string objectKey,CancellationToken token)=>Task.FromResult(values.GetValueOrDefault(objectKey));
        public Task DeletePairAsync(string? catalogKey,string? thumbnailKey,CancellationToken token){if(FailDelete)throw new IOException("Synthetic storage failure.");if(catalogKey is not null)values.Remove(catalogKey);if(thumbnailKey is not null)values.Remove(thumbnailKey);return Task.CompletedTask;}
    }
    private sealed class FakeVersionedS3Client:AmazonS3Client
    {
        public Queue<ListVersionsResponse> Pages {get;}=[];
        public List<ListVersionsRequest> ListRequests {get;}=[];
        public List<DeleteObjectsRequest> Deleted {get;}=[];
        public bool FailDelete {get;set;}
        public FakeVersionedS3Client():base(new AnonymousAWSCredentials(),new AmazonS3Config { ServiceURL="http://127.0.0.1:1",ForcePathStyle=true }) { }
        public override Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request,CancellationToken cancellationToken=default)
        {ListRequests.Add(request);return Task.FromResult(Pages.Dequeue());}
        public override Task<DeleteObjectsResponse> DeleteObjectsAsync(DeleteObjectsRequest request,CancellationToken cancellationToken=default)
        {
            Deleted.Add(request);
            if(FailDelete)return Task.FromResult(new DeleteObjectsResponse { DeleteErrors=[new DeleteError { Key=request.Objects[0].Key,VersionId=request.Objects[0].VersionId,Code="AccessDenied" }] });
            return Task.FromResult(new DeleteObjectsResponse());
        }
    }
}
