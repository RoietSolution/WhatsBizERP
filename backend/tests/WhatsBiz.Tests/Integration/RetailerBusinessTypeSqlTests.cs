using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Infrastructure.Features;
using WhatsBiz.Infrastructure.Persistence;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class RetailerBusinessTypeSqlTests
{
    [Fact]
    public async Task LegacyProfilesPreservedAndOnboardingDraftUsesValidatedCatalogUntilRollback()
    {
        await using var connection=new SqlConnection(SqlIntegrationDatabase.ConnectionString);
        await connection.OpenAsync();await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
        connection.Database.Should().Be("WhatsBizERP_PosGstIntegrationTest");
        await using(var id=new SqlCommand("SELECT @@SERVERNAME",connection))(await id.ExecuteScalarAsync()).Should().Be("DESKTOP-DQ0868S");
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"database/migrations/V73-RetailerBusinessTypes.sql")))root=root.Parent;
        root.Should().NotBeNull();var known=Guid.NewGuid();var legacy=Guid.NewGuid();var enrolled=Guid.NewGuid();
        const string schemaSql="SELECT CONCAT(OBJECT_ID(N'core.BusinessTypes'),':',COL_LENGTH(N'commerce.StoreDirectoryEntries',N'BusinessTypeCode'))";
        string before;await using(var cmd=new SqlCommand(schemaSql,connection))before=(string)(await cmd.ExecuteScalarAsync())!;
        await using(var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            try
            {
                async Task Execute(string text){foreach(var batch in Regex.Split(text,@"(?im)^\s*GO\s*(?:--.*)?$"))if(!string.IsNullOrWhiteSpace(batch)){await using var cmd=new SqlCommand(batch,connection,tx);await cmd.ExecuteNonQueryAsync();}}
                async Task Migration(string name)=>await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations",name+".sql")));
                foreach(var name in new[]{"V67-StorefrontScheduledAvailability","V68-StorefrontFeaturedCategories","V69-StorefrontPopupCampaigns","V70-WhatsAppProductSharing","V71-StorefrontFeatureEntitlements","V72-PublicCatalogGovernance"})await Migration(name);
                foreach(var tenant in new[]{known,legacy,enrolled})
                {await using var cmd=new SqlCommand("INSERT core.Tenants(TenantId,TenantKey,Name,IsActive) VALUES(@tenant,@key,N'Business profile fixture',1)",connection,tx);cmd.Parameters.AddWithValue("@tenant",tenant);cmd.Parameters.AddWithValue("@key","business-"+tenant.ToString("N"));await cmd.ExecuteNonQueryAsync();}
                await using(var cmd=new SqlCommand("INSERT commerce.StoreDirectoryEntries(TenantId,BusinessType,ShortDescription,OptedIn,OwnerApproved) VALUES(@known,N'Electronics',N'Original approved description',1,1),(@legacy,N'Legacy specialty market',N'Original legacy description',1,1)",connection,tx))
                {cmd.Parameters.AddWithValue("@known",known);cmd.Parameters.AddWithValue("@legacy",legacy);await cmd.ExecuteNonQueryAsync();}
                await Migration("V73-RetailerBusinessTypes");await Migration("V73-RetailerBusinessTypes");
                await Execute(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/validators/V73-RetailerBusinessTypes.validate.sql")));
                await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);await db.Database.UseTransactionAsync(tx);
                var knownRow=await db.Set<StoreDirectoryEntry>().AsNoTracking().SingleAsync(e=>e.TenantId==known);
                knownRow.BusinessTypeCode.Should().Be("ELECTRONICS");knownRow.BusinessType.Should().Be("Electronics");knownRow.OwnerApproved.Should().BeTrue();knownRow.OptedIn.Should().BeTrue();knownRow.ShortDescription.Should().Be("Original approved description");
                var oldRow=await db.Set<StoreDirectoryEntry>().AsNoTracking().SingleAsync(e=>e.TenantId==legacy);
                oldRow.BusinessTypeCode.Should().BeNull();oldRow.OwnerApproved.Should().BeTrue();oldRow.ShortDescription.Should().Be("Original legacy description");
                var owner=new Owner();var catalog=new BusinessTypeCatalogService(db,owner);
                var resolved=await catalog.Resolve(" grocery & essentials ",true,default);resolved!.Code.Should().Be("GROCERY");
                var invalid=()=>catalog.Resolve("Uncontrolled spelling",true,default);await invalid.Should().ThrowAsync<BusinessRuleException>();
                var duplicate=()=>catalog.Save(new("DUPLICATE"," grocery   & essentials ",true,null),default);await duplicate.Should().ThrowAsync<BusinessRuleException>();
                var type=await db.Set<WhatsBiz.Domain.Tenants.BusinessType>().AsNoTracking().SingleAsync(x=>x.Code=="FOOD");
                var originalVersion=Convert.ToBase64String(type.Version);
                await catalog.Save(new("FOOD","Prepared Food",false,originalVersion),default);await Migration("V73-RetailerBusinessTypes");
                var disabled=()=>catalog.Resolve("FOOD",true,default);await disabled.Should().ThrowAsync<BusinessRuleException>();
                var stale=()=>catalog.Save(new("FOOD","Old decision",true,originalVersion),default);await stale.Should().ThrowAsync<BusinessRuleException>();
                var food=await db.Set<WhatsBiz.Domain.Tenants.BusinessType>().AsNoTracking().SingleAsync(x=>x.Code=="FOOD");food.Name.Should().Be("Prepared Food");food.IsActive.Should().BeFalse();
                var enrollment=new TenantEnrollmentService(db,null!,null!,null!);var bytes=await enrollment.CreateTemplateAsync();
                using(var workbook=new XLWorkbook(new MemoryStream(bytes)))
                {
                    workbook.Worksheet("Business Types").Column(1).CellsUsed().Select(c=>c.GetString()).Should().Contain("GROCERY").And.NotContain("FOOD");
                    var tenant=workbook.Worksheet("Tenant");tenant.Cell(2,1).Value="FIXTURE";tenant.Cell(2,2).Value="Fixture retailer";tenant.Cell(2,4).Value="GROCERY";tenant.Cell(2,5).Value="  Your  local retailer ";
                    var input=TenantEnrollmentService.ReadBusinessProfile(tenant);input.Description.Should().Be("Your local retailer");
                    var choice=await catalog.Resolve(input.Type,true,default);
                    await TenantEnrollmentService.InitializeBusinessProfileAsync(db,enrolled,choice!,input.Description,default);
                }
                var profile=await db.Set<StoreDirectoryEntry>().AsNoTracking().SingleAsync(e=>e.TenantId==enrolled);
                profile.BusinessTypeCode.Should().Be("GROCERY");profile.ShortDescription.Should().Be("Your local retailer");profile.OptedIn.Should().BeFalse();profile.OwnerApproved.Should().BeFalse();
                var directory=new PublicCatalogService(db,owner);var publicJson=JsonSerializer.Serialize(await directory.Directory(default));
                publicJson.Should().Contain("business-"+legacy.ToString("N")).And.NotContain("business-"+enrolled.ToString("N"));
                var legacySettings=JsonSerializer.SerializeToElement(await directory.DirectorySettings(legacy,default));legacySettings.GetProperty("BusinessTypeRequiresCompletion").GetBoolean().Should().BeTrue();
            }
            finally{await tx.RollbackAsync();}
        }
        await using(var clean=new SqlCommand("SELECT COUNT(*) FROM core.Tenants WHERE TenantId IN(@known,@legacy,@enrolled)",connection))
        {clean.Parameters.AddWithValue("@known",known);clean.Parameters.AddWithValue("@legacy",legacy);clean.Parameters.AddWithValue("@enrolled",enrolled);Convert.ToInt32(await clean.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture).Should().Be(0);}
        await using(var clean=new SqlCommand(schemaSql,connection))((string)(await clean.ExecuteScalarAsync())!).Should().Be(before,"catalog schema and nullable profile field must also roll back");
    }
    private sealed class Owner:ICurrentUserService{public Guid? TenantId=>null;public Guid? UserId=>Guid.Parse("00000000-0000-0000-0000-000000000001");public string? Username=>"business-fixture";public string? Email=>null;public IReadOnlyCollection<string> Roles=>["ApplicationOwner"];public IReadOnlyCollection<string> Permissions=>[];}
}
