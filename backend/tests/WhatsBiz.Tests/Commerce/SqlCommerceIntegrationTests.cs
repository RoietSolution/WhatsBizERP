using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsBiz.Application.Common.Features;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.WhatsApp;
using WhatsBiz.Application.Features.WhatsAppCommerce;
using WhatsBiz.Domain.Commerce;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Domain.Products;
using WhatsBiz.Infrastructure.Analytics;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.WhatsApp;
using WhatsBiz.Infrastructure.WhatsAppCommerce;

namespace WhatsBiz.Tests.Commerce;

[CollectionDefinition("SQL commerce", DisableParallelization = true)]
public sealed class SqlCommerceCollectionDefinition : ICollectionFixture<SqlCommerceFixture>;

[Collection("SQL commerce")]
public sealed class SqlCommerceIntegrationTests(SqlCommerceFixture fixture)
{
    [Fact]
    public async Task SqlBackedCommerceAndSecurityRegressionPasses()
    {
        await fixture.InitializeAsync();
        try
        {
            var setup = await fixture.Commerce.GetSetupAsync(fixture.TenantA, fixture.WarehouseId, default);
            var products = setup.Products;
            var fixtureProducts = products.Where(x => fixture.ProductIds.Contains(x.ProductId)).ToArray();
            products.Should().Contain(x => x.ProductId == fixture.TShirt399 && x.SellingPrice == 399);
            products.Should().NotContain(x => x.ProductId == fixture.TenantBProduct);

            var under500 = fixtureProducts.Where(x => x.CategoryName == "T-Shirts" && x.SellingPrice <= 500).ToArray();
            under500.Select(x => x.ProductId).Should().Equal(fixture.TShirt399);
            fixtureProducts.Where(x => x.CategoryName == "T-Shirts" && x.SellingPrice <= 10).Should().BeEmpty();

            fixtureProducts.Where(x => x.CategoryName == "Sarees" && x.SellingPrice <= 1500)
                .Select(x => x.ProductId).Should().BeEquivalentTo(new[] { fixture.RedSaree, fixture.RedSilkSaree });
            fixtureProducts.Where(x => x.CategoryName == "Sarees" && x.ProductName.Contains("Red", StringComparison.OrdinalIgnoreCase) && x.SellingPrice <= 1500)
                .Select(x => x.ProductId).Should().BeEquivalentTo(new[] { fixture.RedSaree, fixture.RedSilkSaree });
            fixtureProducts.Where(x => x.CategoryName == "Sarees" && x.ProductName.Contains("Red", StringComparison.OrdinalIgnoreCase) && x.ProductName.Contains("Silk", StringComparison.OrdinalIgnoreCase) && x.SellingPrice <= 1500)
                .Select(x => x.ProductId).Should().Equal(fixture.RedSilkSaree);
            fixtureProducts.Where(x => x.ProductName.Contains("Red", StringComparison.OrdinalIgnoreCase) && x.SellingPrice <= 1000)
                .Select(x => x.ProductId).Should().BeEquivalentTo(new[] { fixture.RedShirt });
            fixtureProducts.Where(x => x.ProductName.Contains("Red", StringComparison.OrdinalIgnoreCase) && x.SellingPrice <= 1000)
                .Select(x => x.CategoryName).Distinct().Should().HaveCount(1);

            var collections = setup.Collections;
            collections.Should().ContainSingle(x => x.CollectionId == fixture.CollectionA);
            var tenantCollection = collections.Single(x => x.CollectionId == fixture.CollectionA);
            tenantCollection.ProductIds.Should().Contain(fixture.TShirt399);
            tenantCollection.ProductIds.Should().NotContain(fixture.TenantBProduct);

            var current = new StubCurrentUser(fixture.TenantA);
            await using var db = SqlCommerceFixture.CreateDb();
            var customerRepository = new CustomerRepository(db, current);
            (await customerRepository.GetById(fixture.CustomerA, false, default)).Should().NotBeNull();
            (await customerRepository.GetById(fixture.CustomerB, false, default)).Should().BeNull();
            (await customerRepository.Search(fixture.CustomerBCode, null, "name", false, 1, 20, default)).Item2.Should().Be(0);
            (await customerRepository.BelongsToCurrentTenant(fixture.CustomerB, default)).Should().BeFalse();
            (await customerRepository.BelongsToCurrentTenant(fixture.CustomerA, default)).Should().BeTrue();

            var collectionRepository = new CommerceCollectionRepository(db, current);
            (await collectionRepository.GetAsync(fixture.CollectionB, false, default)).Should().BeNull();
            (await collectionRepository.ProductsBelongToTenantAsync([fixture.TenantBProduct], default)).Should().BeFalse();
            (await collectionRepository.ProductsBelongToTenantAsync([fixture.TShirt399], default)).Should().BeTrue();
            (await collectionRepository.ProductsAsync(fixture.CollectionA, default)).Should().HaveCount(2);
            (await collectionRepository.ExistingProductIdsAsync(fixture.CollectionA, [fixture.TShirt399, fixture.TShirt699], default))
                .Should().Equal(fixture.TShirt399);

            fixture.Provider.CollectionSendCount.Should().Be(0);
            var crossTenant = async () => await fixture.Commerce.SendCollectionAsync(fixture.TenantA, fixture.CollectionA, fixture.CustomerB, default);
            await crossTenant.Should().ThrowAsync<Exception>();
            fixture.Provider.CollectionSendCount.Should().Be(0);
            var validSend = await fixture.Commerce.SendCollectionAsync(fixture.TenantA, fixture.CollectionA, fixture.CustomerA, default);
            validSend.Succeeded.Should().BeTrue();
            fixture.Provider.CollectionSendCount.Should().Be(1);

            await fixture.RecordAnalyticsAsync();
            await fixture.VerifyWebhookAsync();
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }
}

public sealed class SqlCommerceFixture
{
    public const string ConnectionString = "Server=DESKTOP-DQ0868S;Database=WhatsBizERP;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connection Timeout=10";
    private string Tag { get; } = $"SQLIT-{Guid.NewGuid():N}";
    public Guid TenantA { get; private set; }
    public Guid TenantB { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid CustomerA { get; private set; }
    public Guid CustomerB { get; private set; }
    public string CustomerBCode => $"{Tag}-CUS-B";
    public Guid TShirt399 { get; private set; }
    public Guid TShirt699 { get; private set; }
    public Guid RedShirt { get; private set; }
    public Guid RedSaree { get; private set; }
    public Guid RedSilkSaree { get; private set; }
    public Guid TenantBProduct { get; private set; }
    public Guid CollectionA { get; private set; }
    public Guid CollectionB { get; private set; }
    public IReadOnlyCollection<Guid> ProductIds => [TShirt399, TShirt699, RedShirt, RedSaree, RedSilkSaree, TenantBProduct];
    public WhatsAppCommerceService Commerce { get; private set; } = null!;
    public RecordingProvider Provider { get; private set; } = null!;
    private Guid TshirtCategoryId { get; set; }
    private Guid ShirtCategoryId { get; set; }
    private Guid SareeCategoryId { get; set; }
    private Guid BrandId { get; set; }
    private Guid UnitId { get; set; }
    private Guid ConversationId { get; } = Guid.NewGuid();
    private const string WebhookSecret = "sqlit-webhook-secret";
    private const string VerifyToken = "sqlit-verify-token";
    private const string WabaId = "991234567890";
    private const string PhoneId = "991234567891";

    public static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }

    public async Task InitializeAsync()
    {
        if (TenantA != Guid.Empty) return;
        await using var db = CreateDb();
        TenantA = await db.Database.SqlQuery<Guid>($"SELECT TOP(1) TenantId AS Value FROM core.Tenants WHERE IsActive=1 ORDER BY CreatedOn").SingleAsync();
        WarehouseId = await db.Database.SqlQuery<Guid>($"SELECT TOP(1) WarehouseId AS Value FROM inventory.Warehouses WHERE IsActive=1 AND IsDeleted=0 ORDER BY IsDefault DESC,CreatedOn").SingleAsync();
        TenantB = Guid.NewGuid();
        TshirtCategoryId = Guid.NewGuid(); ShirtCategoryId = Guid.NewGuid(); SareeCategoryId = Guid.NewGuid(); BrandId = Guid.NewGuid(); UnitId = Guid.NewGuid();
        TShirt399 = Guid.NewGuid(); TShirt699 = Guid.NewGuid(); RedShirt = Guid.NewGuid(); RedSaree = Guid.NewGuid(); RedSilkSaree = Guid.NewGuid(); TenantBProduct = Guid.NewGuid();
        CustomerA = Guid.NewGuid(); CustomerB = Guid.NewGuid(); CollectionA = Guid.NewGuid(); CollectionB = Guid.NewGuid();

        await ExecuteAsync($"INSERT core.Tenants(TenantId,TenantKey,Name,IsActive,CreatedBy) VALUES('{TenantB}','{Tag}-B','{Tag} Tenant B',1,'{Tag}')");
        db.ProductCategories.AddRange(new ProductCategory { ProductCategoryId = TshirtCategoryId, CategoryCode = $"{Tag}-CAT-T", CategoryName = "T-Shirts", CreatedBy = Tag }, new ProductCategory { ProductCategoryId = ShirtCategoryId, CategoryCode = $"{Tag}-CAT-H", CategoryName = "Shirts", CreatedBy = Tag }, new ProductCategory { ProductCategoryId = SareeCategoryId, CategoryCode = $"{Tag}-CAT-S", CategoryName = "Sarees", CreatedBy = Tag });
        db.Brands.Add(new Brand { BrandId = BrandId, BrandCode = $"{Tag}-BR", BrandName = $"{Tag} Brand", CreatedBy = Tag });
        db.UnitsOfMeasure.Add(new UnitOfMeasure { UnitId = UnitId, UnitCode = $"{Tag}-U", UnitName = "Piece", ShortName = "pc", CreatedBy = Tag });
        db.Customers.AddRange(Customer(CustomerA, TenantA, $"{Tag}-CUS-A", "SQLIT Customer A", "919900000001"), Customer(CustomerB, TenantB, CustomerBCode, "SQLIT Customer B", "919900000002"));
        db.Products.AddRange(Product(TShirt399, TenantA, "T-Shirt ₹399", 399), Product(TShirt699, TenantA, "T-Shirt ₹699", 699), Product(RedShirt, TenantA, "Red Shirt ₹899", 899, "Shirts"), Product(RedSaree, TenantA, "Red Saree ₹1299", 1299, "Sarees"), Product(RedSilkSaree, TenantA, "Red Silk Saree ₹1499", 1499, "Sarees"), Product(TenantBProduct, TenantB, "T-Shirt ₹299", 299));
        db.CommerceCollections.AddRange(new CommerceCollection { CollectionId = CollectionA, TenantId = TenantA, Name = "Wedding Collection", Slug = $"{Tag}-wedding", CreatedBy = Tag }, new CommerceCollection { CollectionId = CollectionB, TenantId = TenantB, Name = "Collection B", Slug = $"{Tag}-b", CreatedBy = Tag });
        await db.SaveChangesAsync();
        await ExecuteAsync($"INSERT commerce.CollectionProducts(CollectionProductId,TenantId,CollectionId,ProductId,DisplayOrder,CreatedBy) VALUES(NEWID(),'{TenantA}','{CollectionA}','{TShirt399}',1,'{Tag}'),(NEWID(),'{TenantA}','{CollectionA}','{RedSaree}',2,'{Tag}'); INSERT inventory.InventoryBalances(InventoryBalanceId,ProductId,WarehouseId,QuantityOnHand,QuantityReserved,AverageCost,LastPurchaseCost,CreatedBy) SELECT NEWID(),p.ProductId,'{WarehouseId}',10,0,p.PurchasePrice,p.PurchasePrice,'{Tag}' FROM master.Products p WHERE p.ProductCode LIKE '{Tag}-%'");
        await ConfigureMetaTestAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString }).Build();
        Provider = new RecordingProvider();
        var resolver = new WhatsAppCommerceProviderResolver([Provider]);
        var features = new AlwaysOnFeatures();
        Commerce = new WhatsAppCommerceService(configuration, null!, resolver, features, DataProtectionProvider.Create("WhatsBiz.SqlCommerceTests"));
    }

    private Product Product(Guid id, Guid tenant, string name, decimal price, string category = "T-Shirts") => new() { ProductId = id, TenantId = tenant, ProductCode = $"{Tag}-{id:N}"[..Math.Min(49, Tag.Length + 1 + 32)], ProductName = name, ShortDescription = name, CategoryId = category switch { "Shirts" => ShirtCategoryId, "Sarees" => SareeCategoryId, _ => TshirtCategoryId }, BrandId = BrandId, UnitId = UnitId, PurchasePrice = price / 2, SellingPrice = price, MRP = price, GSTPercentage = 5, CreatedBy = Tag };
    private static Customer Customer(Guid id, Guid tenant, string code, string name, string mobile) => new() { CustomerId = id, TenantId = tenant, CustomerCode = code, CustomerName = name, CustomerType = "Retail", Mobile = mobile, IsActive = true, Currency = "INR" };

    private async Task ConfigureMetaTestAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString }).Build();
        var service = new WhatsAppService(configuration, DataProtectionProvider.Create("WhatsBiz.SqlCommerceTests"), new AlwaysOnFeatures(), new WhatsAppCommerceProviderResolver([new MockWhatsAppProvider()]), NullLogger<WhatsAppService>.Instance);
        await service.SaveConfigurationAsync(TenantB, new SaveWhatsAppConfigurationInput(WhatsAppProviderModes.MetaTest, "991234567889", WabaId, PhoneId, "v20.0", "919900000002", true, "sqlit-access-token", VerifyToken, WebhookSecret), Tag, default);
    }

    public async Task RecordAnalyticsAsync()
    {
        var service = new CommerceAnalyticsService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString }).Build());
        using var document = JsonDocument.Parse($"{{\"fixture\":\"{Tag}\",\"secret\":false}}");
        foreach (var type in new[] { "PRODUCT_SEARCH", "PRODUCT_SEARCH_NO_MATCH", "PRODUCT_SEARCH_CLARIFICATION", "COLLECTION_SEARCH", "COLLECTION_SENT" })
            await service.RecordAsync(TenantA, new CommerceAnalyticsEventInput(type, CustomerA, ConversationId, TShirt399, null, CollectionA, document.RootElement.Clone()), default);
        await using var db = CreateDb();
        var rows = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM commerce.AnalyticsEvents WHERE TenantId={TenantA} AND ConversationId={ConversationId}").SingleAsync();
        rows.Should().Be(5);
        var metadata = await db.Database.SqlQuery<string?>($"SELECT TOP(1) MetadataJson AS Value FROM commerce.AnalyticsEvents WHERE ConversationId={ConversationId}").SingleAsync();
        metadata.Should().NotContain("access-token").And.NotContain("app-secret");
    }

    public async Task VerifyWebhookAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = ConnectionString }).Build();
        var service = new WhatsAppService(configuration, DataProtectionProvider.Create("WhatsBiz.SqlCommerceTests"), new AlwaysOnFeatures(), new WhatsAppCommerceProviderResolver([new MockWhatsAppProvider()]), NullLogger<WhatsAppService>.Instance);
        (await service.VerifyWebhookAsync("subscribe", VerifyToken, "challenge", default)).Should().Be("challenge");
        var message = new { id = $"{Tag}-message", from = "919900000001", type = "text", timestamp = "1700000000" };
        var value = new { metadata = new { phone_number_id = PhoneId }, messages = new[] { message } };
        var change = new { value };
        var entry = new { id = WabaId, changes = new[] { change } };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { @object = "whatsapp_business_account", entry = new[] { entry } }));
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), body)).ToLowerInvariant();
        (await service.ReceiveWebhookAsync(signature, body, default)).Should().BeTrue();
        (await service.ReceiveWebhookAsync("sha256=bad", body, default)).Should().BeFalse();
        (await service.ReceiveWebhookAsync(signature, body, default)).Should().BeTrue();
        var diagnostics = await service.GetDiagnosticsAsync(TenantB, default);
        diagnostics.LastInboundEventType.Should().Be("MESSAGE_RECEIVED");
        await using var db = CreateDb();
        var count = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM integration.WhatsAppWebhookEvents WHERE TenantId={TenantB}").SingleAsync();
        count.Should().Be(1);
        var duplicates = await db.Database.SqlQuery<long>($"SELECT DuplicateWebhookCount AS Value FROM integration.WhatsAppConfigurations WHERE TenantId={TenantB}").SingleAsync();
        duplicates.Should().Be(1);
    }

    public async Task CleanupAsync()
    {
        if (TenantB == Guid.Empty) return;
        await ExecuteAsync($"DELETE FROM integration.WhatsAppWebhookEvents WHERE TenantId='{TenantB}'; DELETE FROM commerce.AnalyticsEvents WHERE ConversationId='{ConversationId}'; DELETE FROM inventory.InventoryBalances WHERE CreatedBy='{Tag}'; DELETE FROM commerce.CollectionProducts WHERE CreatedBy='{Tag}'; DELETE FROM commerce.Collections WHERE CreatedBy='{Tag}'; DELETE FROM sales.Customers WHERE CustomerCode LIKE '{Tag}-%'; DELETE FROM master.Products WHERE CreatedBy='{Tag}'; DELETE FROM master.ProductCategories WHERE CategoryCode LIKE '{Tag}-CAT-%'; DELETE FROM master.Brands WHERE BrandCode='{Tag}-BR'; DELETE FROM master.UnitsOfMeasure WHERE UnitCode='{Tag}-U'; DELETE FROM integration.WhatsAppConfigurations WHERE TenantId='{TenantB}'; DELETE FROM core.Tenants WHERE TenantId='{TenantB}';");
        await using var db = CreateDb();
        (await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM master.Products WHERE CreatedBy={Tag}").SingleAsync()).Should().Be(0);
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
}

public sealed class RecordingProvider : IWhatsAppCommerceProvider
{
    private readonly MockWhatsAppProvider inner = new();
    public string Mode => inner.Mode;
    public int CollectionSendCount { get; private set; }
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendWelcomeAsync(string storeName, CancellationToken token) => inner.SendWelcomeAsync(storeName, token);
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderConfirmationAsync(string orderNumber, decimal amount, CancellationToken token) => inner.SendOrderConfirmationAsync(orderNumber, amount, token);
    public Task<IReadOnlyCollection<WhatsAppCommerceMessage>> SendOrderStatusAsync(string orderNumber, string status, CancellationToken token) => inner.SendOrderStatusAsync(orderNumber, status, token);
    public Task<WhatsAppProviderConnectionResult> ValidateConnectionAsync(WhatsAppProviderConnectionRequest request, CancellationToken token) => inner.ValidateConnectionAsync(request, token);
    public Task<WhatsAppProviderTestMessageResult> SendTestMessageAsync(WhatsAppProviderTestMessageRequest request, CancellationToken token) => inner.SendTestMessageAsync(request, token);
    public Task<WhatsAppCommerceSendResult> SendProductCollectionAsync(WhatsAppCommerceSendRequest request, CancellationToken token) { CollectionSendCount++; return inner.SendProductCollectionAsync(request, token); }
}

public sealed class AlwaysOnFeatures : IFeatureService
{
    public Task<bool> IsEnabledAsync(Guid tenantId, string featureKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<IReadOnlyDictionary<string, bool>> GetEffectiveFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool> { [FeatureKeys.WhatsAppCommerce] = true });
    public Task<TenantFeatureConfiguration> GetTenantConfigurationAsync(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyCollection<FeatureTenantSummary>> GetTenantsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TenantFeatureConfiguration> UpdateTenantConfigurationAsync(Guid tenantId, IReadOnlyCollection<TenantFeatureUpdate> updates, string? changedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public void InvalidateTenant(Guid tenantId) { }
    public void InvalidateAll() { }
}

public sealed class StubCurrentUser(Guid tenant) : ICurrentUserService
{
    public Guid? UserId => Guid.NewGuid();
    public Guid? TenantId => tenant;
    public string? Username => "sqlit";
    public string? Email => "sqlit@example.test";
    public IReadOnlyCollection<string> Roles => [];
    public IReadOnlyCollection<string> Permissions => [];
}
