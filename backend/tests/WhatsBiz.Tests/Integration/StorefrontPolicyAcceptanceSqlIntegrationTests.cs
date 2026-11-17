using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;
using WhatsBiz.Infrastructure.Storefront;

namespace WhatsBiz.Tests.Integration;

[Collection("SQL EF compatibility")]
public sealed class StorefrontPolicyAcceptanceSqlIntegrationTests
{
    private const string AuthorizationVariable="WHATS_BIZ_V84_ROLLBACK_TEST_AUTHORIZATION";
    private const string AuthorizationValue="I_AUTHORIZE_ROLLBACK_ONLY_V84_TESTS";
    private const string FixtureActor="V84_SQL_INTEGRATION_TEST";
    private const string FixtureCustomerPrefix="V84 SQL Integration Test ";

    [WhatsBiz.Tests.Integration.SqlIntegrationFact(Timeout=180_000)]
    public async Task V84SignupPoliciesAreTenantScopedAtomicAndExistingOtpLoginRemainsAvailable()
    {
        var connectionString=SqlIntegrationDatabase.ConnectionString;
        var builder=new SqlConnectionStringBuilder(connectionString);
        IntegrationDatabaseTarget.AssertApproved(builder);
        if(!string.Equals(Environment.GetEnvironmentVariable(AuthorizationVariable),AuthorizationValue,StringComparison.Ordinal))
            throw new InvalidOperationException($"Set {AuthorizationVariable} to its exact rollback-only authorization value before running this SQL test.");

        var root=FindRepositoryRoot();
        var connection=new SqlConnection(connectionString);
        await using(connection)
        {
            await connection.OpenAsync();
            await SqlIntegrationDatabase.VerifyOpenedDatabaseAsync(connection);
            await AssertExactServerAsync(connection);
            foreach(var version in new[]{78,79,80,81,82,83})
                await ExecuteScriptAsync(connection,null,Path.Combine(root,$"database/migrations/validators/V{version}-{MigrationSuffix(version)}.validate.sql"));

            await ExecuteScriptAsync(connection,null,Path.Combine(root,"database/migrations/V84-StorefrontCustomerTermsAcceptance.sql"));
            await ExecuteScriptAsync(connection,null,Path.Combine(root,"database/migrations/validators/V84-StorefrontCustomerTermsAcceptance.validate.sql"));

            var (tenantId,storeKey)=await FindActiveTenantAsync(connection);
            await SetTenantContextAsync(connection,tenantId);
            var fixtureIds=new List<Guid>();
            var phones=new List<string>();
            await CleanupFixtureAsync(connection,tenantId,fixtureIds,phones);
            try
            {
                var marker=Guid.NewGuid().ToString("N");
                var platformTerms=await FindActivePlatformTermsAsync(connection);
                if(platformTerms is null){var platformTermsId=Guid.NewGuid();fixtureIds.Add(platformTermsId);platformTerms=await InsertPublishedPolicyAsync(connection,platformTermsId,"CUSTOMER_TERMS",null,$"IT-{marker[..8]}","Platform test terms",FixtureActor);}
                var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                {
                    ["ConnectionStrings:DefaultConnection"]=connectionString,
                    ["StorefrontCustomerAuth:FixedOtpEnabled"]="true",
                    ["StorefrontCustomerAuth:FixedOtp"]="654321"
                }).Build();
                var auth=new StorefrontCustomerAuthenticationService(configuration,new StorefrontOtpPolicy("Test",configuration),new NoopOtpSender(),new TestCustomerSessions());
                var policyController=new StorefrontPolicyController(configuration);

                var fallback=await ReadPoliciesAsync(policyController,storeKey);
                fallback.Single(x=>x.PolicyType=="CUSTOMER_TERMS").PolicyId.Should().Be(platformTerms.Value.Id);
                fallback.Single(x=>x.PolicyType=="PRIVACY_NOTICE").IsPublished.Should().BeTrue("V84 preserves the existing public privacy notice as the platform fallback");
                var platformPrivacyDto=fallback.Single(x=>x.PolicyType=="PRIVACY_NOTICE");
                var platformPrivacy=(platformPrivacyDto.PolicyId,platformPrivacyDto.Version,Convert.FromHexString(platformPrivacyDto.ContentHash));

                var termsId=Guid.NewGuid();fixtureIds.Add(termsId);
                var privacyId=Guid.NewGuid();fixtureIds.Add(privacyId);
                var terms=await InsertPublishedPolicyAsync(connection,termsId,"CUSTOMER_TERMS",tenantId,$"IT-{marker[..8]}-R","Retailer test terms",FixtureActor);
                var privacy=await InsertPublishedPolicyAsync(connection,privacyId,"PRIVACY_NOTICE",tenantId,$"IT-{marker[..8]}-P","Retailer test privacy",FixtureActor);
                var overridden=await ReadPoliciesAsync(policyController,storeKey);
                overridden.Single(x=>x.PolicyType=="CUSTOMER_TERMS").PolicyId.Should().Be(termsId);
                overridden.Single(x=>x.PolicyType=="PRIVACY_NOTICE").PolicyId.Should().Be(privacyId);
                overridden.Single(x=>x.PolicyType=="PRIVACY_NOTICE").PolicyId.Should().NotBe(platformPrivacy.PolicyId);

                var successPhone=NewTestPhone();phones.Add(successPhone);
                var successChallenge=await auth.RequestOtpAsync(storeKey,new StorefrontOtpRequest(successPhone),CancellationToken.None);
                successChallenge.Should().NotBeNull();
                var success=await auth.VerifyOtpAsync(storeKey,Input(successChallenge!.ChallengeId,successPhone,"654321",$"{FixtureCustomerPrefix}{marker} success",true,terms,privacy),CancellationToken.None);
                success.Should().NotBeNull();
                var acceptedCustomerId=success!.Customer.Id;
                (await CountAcceptancesAsync(connection,tenantId,acceptedCustomerId)).Should().Be(2);
                (await CountMatchingCustomersAsync(connection,tenantId,successPhone)).Should().Be(1);
                (await IsChallengeConsumedAsync(connection,tenantId,successChallenge.ChallengeId)).Should().BeTrue();

                var loginChallengeId=await InsertSecondChallengeAsync(connection,tenantId,successPhone,"654321");
                var existingLogin=await auth.VerifyOtpAsync(storeKey,new StorefrontOtpVerifyInput(loginChallengeId,successPhone,"654321",null,null,false),CancellationToken.None);
                existingLogin!.Customer.Id.Should().Be(acceptedCustomerId);
                (await CountAcceptancesAsync(connection,tenantId,acceptedCustomerId)).Should().Be(2,"an existing OTP login is not forced into first-time signup consent");

                var rejectedPhone=NewTestPhone();phones.Add(rejectedPhone);
                var rejectedChallenge=await auth.RequestOtpAsync(storeKey,new StorefrontOtpRequest(rejectedPhone),CancellationToken.None);
                await Assert.ThrowsAsync<BusinessRuleException>(()=>auth.VerifyOtpAsync(storeKey,Input(rejectedChallenge!.ChallengeId,rejectedPhone,"654321",$"{FixtureCustomerPrefix}{marker} rejected",false,terms,privacy),CancellationToken.None));
                (await CountMatchingCustomersAsync(connection,tenantId,rejectedPhone)).Should().Be(0);
                (await CountAcceptancesForPhoneAsync(connection,tenantId,rejectedPhone)).Should().Be(0);
                (await IsChallengeConsumedAsync(connection,tenantId,rejectedChallenge!.ChallengeId)).Should().BeFalse("rejected acceptance rolls the consumed OTP marker back");

                await SetPolicyActiveAsync(connection,termsId,false);
                var replacementTermsId=Guid.NewGuid();fixtureIds.Add(replacementTermsId);
                var replacementTerms=await InsertPublishedPolicyAsync(connection,replacementTermsId,"CUSTOMER_TERMS",tenantId,$"IT-{marker[..8]}-N","Replacement terms",FixtureActor);
                var stalePhone=NewTestPhone();phones.Add(stalePhone);
                var staleChallenge=await auth.RequestOtpAsync(storeKey,new StorefrontOtpRequest(stalePhone),CancellationToken.None);
                await Assert.ThrowsAsync<BusinessRuleException>(()=>auth.VerifyOtpAsync(storeKey,Input(staleChallenge!.ChallengeId,stalePhone,"654321",$"{FixtureCustomerPrefix}{marker} stale",true,terms,privacy),CancellationToken.None));
                (await CountMatchingCustomersAsync(connection,tenantId,stalePhone)).Should().Be(0);
                (await CountAcceptancesForPhoneAsync(connection,tenantId,stalePhone)).Should().Be(0);
                (await IsChallengeConsumedAsync(connection,tenantId,staleChallenge!.ChallengeId)).Should().BeFalse();
                var afterReplacement=await ReadPoliciesAsync(policyController,storeKey);
                afterReplacement.Single(x=>x.PolicyType=="CUSTOMER_TERMS").PolicyId.Should().Be(replacementTermsId);

                var concurrentPhone=NewTestPhone();phones.Add(concurrentPhone);
                var firstChallenge=await auth.RequestOtpAsync(storeKey,new StorefrontOtpRequest(concurrentPhone),CancellationToken.None);
                firstChallenge.Should().NotBeNull();
                var secondChallengeId=await InsertSecondChallengeAsync(connection,tenantId,concurrentPhone,"654321");
                var attempts=await Task.WhenAll(
                    auth.VerifyOtpAsync(storeKey,Input(firstChallenge!.ChallengeId,concurrentPhone,"654321",$"{FixtureCustomerPrefix}{marker} concurrent",true,replacementTerms,privacy),CancellationToken.None),
                    auth.VerifyOtpAsync(storeKey,Input(secondChallengeId,concurrentPhone,"654321",$"{FixtureCustomerPrefix}{marker} concurrent",true,replacementTerms,privacy),CancellationToken.None));
                attempts.Should().NotContainNulls();
                attempts.Select(x=>x!.Customer.Id).Distinct().Should().HaveCount(1);
                (await CountMatchingCustomersAsync(connection,tenantId,concurrentPhone)).Should().Be(1);
                (await CountAcceptancesAsync(connection,tenantId,attempts[0]!.Customer.Id)).Should().Be(2,"the losing concurrent signup resolves to the created customer instead of duplicating acceptance rows");
            }
            finally
            {
                await CleanupFixtureAsync(connection,tenantId,fixtureIds,phones);
            }
        }
    }

    private static StorefrontOtpVerifyInput Input(Guid challenge,string phone,string code,string name,bool accepted,(Guid Id,string Version,byte[] Hash) terms,(Guid Id,string Version,byte[] Hash) privacy)
        =>new(challenge,phone,code,name,null,true,AcceptTerms:accepted,TermsPolicyId:terms.Id,TermsContentHash:Convert.ToHexString(terms.Hash),AcknowledgePrivacy:accepted,PrivacyPolicyId:privacy.Id,PrivacyContentHash:Convert.ToHexString(privacy.Hash));

    private static async Task<IReadOnlyCollection<StorefrontCustomerPolicyDto>> ReadPoliciesAsync(StorefrontPolicyController controller,string storeKey)
    {
        var action=await controller.Current(storeKey,CancellationToken.None);
        return action.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<IReadOnlyCollection<StorefrontCustomerPolicyDto>>().Subject;
    }

    private static async Task<(Guid Id,string Key)> FindActiveTenantAsync(SqlConnection connection)
    {
        await using var command=new SqlCommand("SELECT TOP(1) TenantId,TenantKey FROM core.Tenants WHERE IsActive=1 AND TenantKey IS NOT NULL;",connection);
        await using var reader=await command.ExecuteReaderAsync();
        if(!await reader.ReadAsync())throw new InvalidOperationException("Disposable integration database has no active tenant fixture.");
        return(reader.GetGuid(0),reader.GetString(1));
    }

    private static async Task<(Guid Id,string Version,byte[] Hash)?> FindActivePlatformTermsAsync(SqlConnection connection)
    {await using var command=new SqlCommand("SELECT TOP(1) PolicyId,Version,ContentHash FROM commerce.StorefrontPolicies WHERE PolicyType=N'CUSTOMER_TERMS' AND TenantId IS NULL AND IsPublished=1 AND IsActive=1 AND AcceptanceRequired=1 AND EffectiveAtUtc<=SYSUTCDATETIME();",connection);await using var reader=await command.ExecuteReaderAsync();return await reader.ReadAsync()?(reader.GetGuid(0),reader.GetString(1),(byte[])reader[2]):null;}

    private static async Task<(Guid Id,string Version,byte[] Hash)> InsertPublishedPolicyAsync(SqlConnection connection,Guid policyId,string type,Guid? tenant,string version,string content,string actor)
    {
        var hash=SHA256.HashData(Encoding.Unicode.GetBytes(content));var now=DateTimeOffset.UtcNow;
        await using var command=new SqlCommand("INSERT commerce.StorefrontPolicies(PolicyId,PolicyType,TenantId,Version,LanguageCode,Content,ContentHash,EffectiveAtUtc,IsPublished,IsActive,AcceptanceRequired,ApprovalReference,CreatedBy,PublishedAtUtc,PublishedBy) VALUES(@id,@type,@tenant,@version,N'en',@content,@hash,@effective,1,1,1,N'TEST_ONLY_APPROVAL',@actor,@published,@actor);",connection);
        command.Parameters.AddWithValue("@id",policyId);command.Parameters.AddWithValue("@type",type);command.Parameters.AddWithValue("@tenant",tenant??(object)DBNull.Value);command.Parameters.AddWithValue("@version",version);command.Parameters.AddWithValue("@content",content);command.Parameters.AddWithValue("@hash",hash);command.Parameters.AddWithValue("@effective",now.AddMinutes(-1));command.Parameters.AddWithValue("@published",now);command.Parameters.AddWithValue("@actor",actor);
        await command.ExecuteNonQueryAsync();return(policyId,version,hash);
    }

    private static async Task SetPolicyActiveAsync(SqlConnection connection,Guid id,bool active)
    {await using var command=new SqlCommand("UPDATE commerce.StorefrontPolicies SET IsActive=@active WHERE PolicyId=@id;",connection);command.Parameters.AddWithValue("@active",active);command.Parameters.AddWithValue("@id",id);await command.ExecuteNonQueryAsync();}

    private static async Task<Guid> InsertSecondChallengeAsync(SqlConnection connection,Guid tenant,string phone,string otp)
    {
        var id=Guid.NewGuid();var salt=RandomNumberGenerator.GetBytes(16);var hash=StorefrontCustomerAuthenticationService.HashOtp(otp,salt);var now=DateTimeOffset.UtcNow;
        await using var command=new SqlCommand("INSERT commerce.StorefrontOtpChallenges(ChallengeId,TenantId,MobileNumberNormalized,OtpHash,OtpSalt,CreatedAt,ExpiresAt,AttemptCount,ConsumedAt) VALUES(@id,@tenant,@mobile,@hash,@salt,@created,@expires,0,NULL);",connection);
        command.Parameters.AddWithValue("@id",id);command.Parameters.AddWithValue("@tenant",tenant);command.Parameters.AddWithValue("@mobile",phone);command.Parameters.AddWithValue("@hash",hash);command.Parameters.AddWithValue("@salt",salt);command.Parameters.AddWithValue("@created",now);command.Parameters.AddWithValue("@expires",now.AddMinutes(5));await command.ExecuteNonQueryAsync();return id;
    }

    private static async Task<int> CountAcceptancesAsync(SqlConnection connection,Guid tenant,Guid customer)
    {await using var command=new SqlCommand("SELECT COUNT(*) FROM commerce.StorefrontPolicyAcceptances WHERE TenantId=@tenant AND CustomerId=@customer;",connection);command.Parameters.AddWithValue("@tenant",tenant);command.Parameters.AddWithValue("@customer",customer);return Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    private static async Task<int> CountMatchingCustomersAsync(SqlConnection connection,Guid tenant,string phone)
    {await using var command=new SqlCommand("SELECT COUNT(*) FROM sales.Customers WHERE TenantId=@tenant AND MobileNormalized=@mobile AND IsDeleted=0;",connection);command.Parameters.AddWithValue("@tenant",tenant);command.Parameters.AddWithValue("@mobile",phone);return Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    private static async Task<int> CountAcceptancesForPhoneAsync(SqlConnection connection,Guid tenant,string phone)
    {await using var command=new SqlCommand("SELECT COUNT(*) FROM commerce.StorefrontPolicyAcceptances a JOIN sales.Customers c ON c.CustomerId=a.CustomerId AND c.TenantId=a.TenantId WHERE a.TenantId=@tenant AND c.MobileNormalized=@mobile;",connection);command.Parameters.AddWithValue("@tenant",tenant);command.Parameters.AddWithValue("@mobile",phone);return Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    private static async Task<bool> IsChallengeConsumedAsync(SqlConnection connection,Guid tenant,Guid challenge)
    {await using var command=new SqlCommand("SELECT CONVERT(bit,CASE WHEN ConsumedAt IS NULL THEN 0 ELSE 1 END) FROM commerce.StorefrontOtpChallenges WHERE TenantId=@tenant AND ChallengeId=@id;",connection);command.Parameters.AddWithValue("@tenant",tenant);command.Parameters.AddWithValue("@id",challenge);return await command.ExecuteScalarAsync() is bool value&&value;}

    private static async Task CleanupFixtureAsync(SqlConnection connection,Guid tenant,List<Guid> policyIds,List<string> phones)
    {
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await using(var session=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;",connection,transaction)){session.Parameters.AddWithValue("@tenant",tenant);await session.ExecuteNonQueryAsync();}
            await using(var disable=new SqlCommand("DISABLE TRIGGER commerce.TR_StorefrontPolicies_ImmutablePublished ON commerce.StorefrontPolicies; DISABLE TRIGGER commerce.TR_StorefrontPolicyAcceptances_Immutable ON commerce.StorefrontPolicyAcceptances;",connection,transaction))await disable.ExecuteNonQueryAsync();
            await using(var acceptance=new SqlCommand("DELETE a FROM commerce.StorefrontPolicyAcceptances a WHERE a.TenantId=@tenant AND (a.CustomerId IN(SELECT CustomerId FROM sales.Customers WHERE TenantId=@tenant AND CustomerName LIKE @prefix) OR a.PolicyId IN(SELECT PolicyId FROM commerce.StorefrontPolicies WHERE CreatedBy=@actor));",connection,transaction)){acceptance.Parameters.AddWithValue("@tenant",tenant);acceptance.Parameters.AddWithValue("@prefix",FixtureCustomerPrefix+"%");acceptance.Parameters.AddWithValue("@actor",FixtureActor);await acceptance.ExecuteNonQueryAsync();}
            await using(var customers=new SqlCommand("DELETE FROM sales.Customers WHERE TenantId=@tenant AND CustomerName LIKE @prefix;",connection,transaction)){customers.Parameters.AddWithValue("@tenant",tenant);customers.Parameters.AddWithValue("@prefix",FixtureCustomerPrefix+"%");await customers.ExecuteNonQueryAsync();}
            await using(var otp=new SqlCommand("DELETE FROM commerce.StorefrontOtpChallenges WHERE TenantId=@tenant AND MobileNumberNormalized LIKE '98999%';",connection,transaction)){otp.Parameters.AddWithValue("@tenant",tenant);await otp.ExecuteNonQueryAsync();}
            await using(var policies=new SqlCommand("DELETE FROM commerce.StorefrontPolicies WHERE CreatedBy=@actor OR PolicyId IN(SELECT value FROM STRING_SPLIT(@ids,','));",connection,transaction)){policies.Parameters.AddWithValue("@actor",FixtureActor);policies.Parameters.AddWithValue("@ids",string.Join(',',policyIds));await policies.ExecuteNonQueryAsync();}
            await using(var enable=new SqlCommand("ENABLE TRIGGER commerce.TR_StorefrontPolicies_ImmutablePublished ON commerce.StorefrontPolicies; ENABLE TRIGGER commerce.TR_StorefrontPolicyAcceptances_Immutable ON commerce.StorefrontPolicyAcceptances;",connection,transaction))await enable.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch{await transaction.RollbackAsync();throw;}
    }

    private static async Task SetTenantContextAsync(SqlConnection connection,Guid tenant)
    {await using var command=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;",connection);command.Parameters.AddWithValue("@tenant",tenant);await command.ExecuteNonQueryAsync();}

    private static string NewTestPhone()=>"98999"+RandomNumberGenerator.GetInt32(0,100_000).ToString("D5",CultureInfo.InvariantCulture);
    private static async Task ExecuteScriptAsync(SqlConnection connection,SqlTransaction? transaction,string path)
    {var text=await File.ReadAllTextAsync(path);foreach(var batch in Regex.Split(text,@"(?im)^\s*GO\s*(?:--.*)?$").Where(x=>!string.IsNullOrWhiteSpace(x))){await using var command=new SqlCommand(batch,connection,transaction){CommandTimeout=120};await command.ExecuteNonQueryAsync();}}
    private static async Task AssertExactServerAsync(SqlConnection connection)
    {await using var command=new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'));",connection);var actual=(string?)await command.ExecuteScalarAsync();if(!string.Equals(actual,IntegrationDatabaseTarget.Server,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("V84 SQL test connected to an unapproved SQL Server.");}
    private static string MigrationSuffix(int version)=>version switch{78=>"CityMarketplaceSearchAnalytics",79=>"MultiChannelOrderIntake",80=>"OrderIntakeQuotesAndSalesOrders",81=>"OrderIntakeDeliveryAddressSnapshots",82=>"GuestVisitorAnalytics",83=>"SupplierShopifyCatalogueImport",_=>throw new ArgumentOutOfRangeException(nameof(version))};
    private static string FindRepositoryRoot(){for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)if(File.Exists(Path.Combine(directory.FullName,"database/migrations/V84-StorefrontCustomerTermsAcceptance.sql")))return directory.FullName;throw new DirectoryNotFoundException("Could not locate V84 migration source.");}

    private sealed class NoopOtpSender:ICustomerOtpSender{public Task SendAsync(string normalizedMobile,string otp,Guid challengeId,CancellationToken token)=>Task.CompletedTask;}
    private sealed class TestCustomerSessions:IStorefrontCustomerService
    {
        private Guid customerId;
        public Task<string> IssueSessionAsync(string storeKey,Guid tenantId,Guid customerId,CancellationToken token){this.customerId=customerId;return Task.FromResult("v84-sql-test-session");}
        public Task<StorefrontCustomerDto?> GetSessionAsync(string storeKey,string sessionToken,CancellationToken token)=>Task.FromResult<StorefrontCustomerDto?>(new(customerId,"V84 SQL test",null,null));
        public Task<IReadOnlyCollection<StorefrontCustomerOrderDto>?> GetOrdersAsync(string s,string t,CancellationToken c)=>Task.FromResult<IReadOnlyCollection<StorefrontCustomerOrderDto>?>(null);
        public Task<StorefrontCustomerOrderDto?> GetOrderAsync(string s,string t,Guid id,CancellationToken c)=>Task.FromResult<StorefrontCustomerOrderDto?>(null);
        public Task<IReadOnlyCollection<StorefrontProductDto>?> GetWishlistAsync(string s,string t,CancellationToken c)=>Task.FromResult<IReadOnlyCollection<StorefrontProductDto>?>(null);
        public Task<bool> AddWishlistAsync(string s,string t,Guid id,CancellationToken c)=>Task.FromResult(false);
        public Task<bool> RemoveWishlistAsync(string s,string t,Guid id,CancellationToken c)=>Task.FromResult(false);
        public Task<bool> MergeWishlistAsync(string s,string t,IReadOnlyCollection<Guid> ids,CancellationToken c)=>Task.FromResult(false);
        public Task<IReadOnlyCollection<StorefrontCustomerAddressDto>?> GetAddressesAsync(string s,string t,CancellationToken c)=>Task.FromResult<IReadOnlyCollection<StorefrontCustomerAddressDto>?>(null);
        public Task<StorefrontCustomerAddressDto?> SaveAddressAsync(string s,string t,Guid? id,StorefrontCustomerAddressInput input,CancellationToken c)=>Task.FromResult<StorefrontCustomerAddressDto?>(null);
        public Task<bool> DeleteAddressAsync(string s,string t,Guid id,CancellationToken c)=>Task.FromResult(false);
        public Task<bool> SetDefaultAddressAsync(string s,string t,Guid id,CancellationToken c)=>Task.FromResult(false);
        public Task<StorefrontCustomerDto?> UploadProfileImageAsync(string s,string t,string file,Stream stream,CancellationToken c)=>Task.FromResult<StorefrontCustomerDto?>(null);
        public Task<StorefrontCustomerDto?> RemoveProfileImageAsync(string s,string t,CancellationToken c)=>Task.FromResult<StorefrontCustomerDto?>(null);
        public Task<StorefrontImage?> GetProfileImageAsync(string s,string t,CancellationToken c)=>Task.FromResult<StorefrontImage?>(null);
    }
}
