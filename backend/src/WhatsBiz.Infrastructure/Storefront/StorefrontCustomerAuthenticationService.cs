using System.Net.Http.Json;
using System.Net.Mail;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WhatsBiz.Infrastructure.Notifications;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class StorefrontOtpPolicy
{
    private readonly string? fixedCode;
    public bool SkipDelivery { get; }

    public StorefrontOtpPolicy(string environmentName, IConfiguration configuration)
    {
        // A configured fixed code remains an explicit test/deployment override; otherwise the configured SMS sender is used.
        if (configuration.GetValue<bool>("StorefrontCustomerAuth:FixedOtpEnabled"))
        {
            fixedCode = ValidateFixedCode(configuration["StorefrontCustomerAuth:FixedOtp"], "Storefront customer fixed OTP");
            SkipDelivery = true;
            return;
        }

        var qaEnabled = configuration.GetValue<bool>("StorefrontOtp:QaTestModeEnabled");
        if (qaEnabled && !string.Equals(environmentName, "QA", StringComparison.Ordinal))
            throw new InvalidOperationException("Storefront QA test OTP mode is only allowed in the exact QA environment.");

        if (environmentName is "Development" or "Test")
        {
            fixedCode = configuration["StorefrontOtp:DevelopmentCode"];
            SkipDelivery = fixedCode is not null;
        }
        else if (qaEnabled)
        {
            fixedCode = ValidateFixedCode(configuration["StorefrontOtp:QaTestCode"], "Storefront QA test OTP");
            SkipDelivery = true;
        }
    }

    public string CreateCode() => fixedCode ?? RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    private static string ValidateFixedCode(string? value, string description)
    {
        if (value?.Length != 6 || !value.All(c => c is >= '0' and <= '9'))
            throw new InvalidOperationException($"{description} must be six digits.");
        return value;
    }
}

public sealed class CustomerOtpSender(StorefrontOtpPolicy policy, IConfiguration configuration, IHttpClientFactory clients) : ICustomerOtpSender
{
    public async Task SendAsync(string normalizedMobile, string otp, Guid challengeId, CancellationToken token)
    {
        if (policy.SkipDelivery) return;
        var endpoint = configuration["StorefrontOtp:Endpoint"];
        if (string.Equals(configuration["StorefrontOtp:Provider"], "2Factor", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = "https://2factor.in/API/V1/OTP/SEND";
            var apiKey = configuration["StorefrontOtp:ApiKey"];
            if (IsTwoFactorManualOtpEndpoint(endpoint))
            {
                if (string.IsNullOrWhiteSpace(apiKey)
                    || otp.Length != 6 || !otp.All(char.IsAsciiDigit)
                    || !normalizedMobile.All(char.IsAsciiDigit)
                    || !TryBuildTwoFactorManualOtpUri(endpoint, apiKey, "+91" + normalizedMobile, otp, out var manualOtpUri))
                    throw new InvalidOperationException("The 2Factor manual OTP endpoint, API key, mobile number, or OTP is invalid.");
                using var manualOtpRequest = new HttpRequestMessage(HttpMethod.Post, manualOtpUri);
                using var manualOtpResponse = await clients.CreateClient("StorefrontOtp").SendAsync(manualOtpRequest, token);
                var manualOtpResponseBody = await manualOtpResponse.Content.ReadAsStringAsync(token);
                if (!manualOtpResponse.IsSuccessStatusCode || !HttpCustomerMessageProvider.TwoFactorAccepted(manualOtpResponseBody, out _))
                    throw new InvalidOperationException("The 2Factor OTP provider rejected the request.");
                return;
            }
            var template = configuration["StorefrontOtp:TemplateName"];
            if (!HttpCustomerMessageProvider.TryTwoFactorEndpoint(endpoint, out var twoFactorUri)
                || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(template))
                throw new InvalidOperationException("The 2Factor OTP endpoint template or API key is not configured.");
            using var twoFactorRequest = new HttpRequestMessage(HttpMethod.Post, twoFactorUri)
            {
                Content = JsonContent.Create(new { to = "+91" + normalizedMobile, channel = "SMS", template_name = template, var1 = otp })
            };
            HttpCustomerMessageProvider.AddTwoFactorAuthentication(twoFactorRequest, apiKey,
                configuration["StorefrontOtp:ApiKeyHeader"], configuration["StorefrontOtp:ApiKeyScheme"]);
            using var twoFactorResponse = await clients.CreateClient("StorefrontOtp").SendAsync(twoFactorRequest, token);
            var responseBody = await twoFactorResponse.Content.ReadAsStringAsync(token);
            if (!twoFactorResponse.IsSuccessStatusCode || !HttpCustomerMessageProvider.TwoFactorAccepted(responseBody, out _))
                throw new InvalidOperationException("The 2Factor OTP provider rejected the request.");
            return;
        }
        var accessToken = configuration["StorefrontOtp:AccessToken"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("The customer OTP provider is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(new { recipient = "+91" + normalizedMobile, message = $"Your KhataDhari verification code is {otp}. It expires in 5 minutes.", referenceId = challengeId });
        using var response = await clients.CreateClient("StorefrontOtp").SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The customer OTP provider rejected the request.");
    }

    private static bool IsTwoFactorManualOtpEndpoint(string endpoint)
        => endpoint.Contains("/SMS/", StringComparison.OrdinalIgnoreCase)
            && (endpoint.Contains("/AUTOGEN/OTP1", StringComparison.OrdinalIgnoreCase)
                || endpoint.Contains("{otp_code}", StringComparison.OrdinalIgnoreCase)
                || endpoint.Contains(":otp_code", StringComparison.OrdinalIgnoreCase));

    private static bool TryBuildTwoFactorManualOtpUri(string endpoint, string apiKey, string phoneNumber, string otp, out Uri uri)
    {
        var apiKeySegment = Uri.EscapeDataString(apiKey);
        var phoneSegment = Uri.EscapeDataString(phoneNumber);
        var otpSegment = Uri.EscapeDataString(otp);
        var expanded = endpoint
            .Replace("{api_key}", apiKeySegment, StringComparison.OrdinalIgnoreCase)
            .Replace(":api_key", apiKeySegment, StringComparison.OrdinalIgnoreCase)
            .Replace("{phone_number}", phoneSegment, StringComparison.OrdinalIgnoreCase)
            .Replace(":phone_number", phoneSegment, StringComparison.OrdinalIgnoreCase)
            .Replace("{otp_code}", otpSegment, StringComparison.OrdinalIgnoreCase)
            .Replace(":otp_code", otpSegment, StringComparison.OrdinalIgnoreCase)
            .Replace("/AUTOGEN/OTP1", "/" + otpSegment, StringComparison.OrdinalIgnoreCase);
        return HttpCustomerMessageProvider.TryTwoFactorEndpoint(expanded, out uri);
    }
}

public sealed partial class StorefrontCustomerAuthenticationService(
    IConfiguration configuration,
    StorefrontOtpPolicy policy,
    ICustomerOtpSender sender,
    IStorefrontCustomerService customers) : IStorefrontCustomerAuthenticationService
{
    private const int ExpiryMinutes = 5;
    private const int CooldownSeconds = 45;
    private const int MaxAttempts = 5;
    private static readonly PasswordHasher<object> Passwords = new();
    private string ConnectionString => configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Database connection unavailable.");

    public async Task<StorefrontOtpChallengeDto?> RequestOtpAsync(string storeKey, StorefrontOtpRequest input, CancellationToken token)
    {
        var tenantId = await ResolveTenant(storeKey, token);
        var mobile = NormalizeMobile(input.MobileNumber);
        if (tenantId is null || mobile is null) return null;
        var now = DateTimeOffset.UtcNow;
        var challengeId = Guid.NewGuid();
        var otp = policy.CreateCode();
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashOtp(otp, salt);
        await using var connection = await Open(tenantId.Value, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        await using (var throttle = new SqlCommand("""
            SELECT
              MAX(CreatedAt),
              SUM(CASE WHEN CreatedAt>=DATEADD(hour,-1,SYSUTCDATETIME()) THEN 1 ELSE 0 END)
            FROM commerce.StorefrontOtpChallenges WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@tenant AND MobileNumberNormalized=@mobile;
            """, connection, transaction))
        {
            throttle.Parameters.AddWithValue("@tenant", tenantId);
            throttle.Parameters.AddWithValue("@mobile", mobile);
            await using var reader = await throttle.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var last = reader.IsDBNull(0) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(0);
                var hourly = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                if (last is not null && now - last < TimeSpan.FromSeconds(CooldownSeconds))
                    throw new BusinessRuleException("Please wait before requesting another code.");
                if (hourly >= 5) throw new BusinessRuleException("Too many verification codes requested. Please try again later.");
            }
        }
        await using (var insert = new SqlCommand("""
            INSERT commerce.StorefrontOtpChallenges
              (ChallengeId,TenantId,MobileNumberNormalized,OtpHash,OtpSalt,CreatedAt,ExpiresAt,AttemptCount)
            VALUES(@id,@tenant,@mobile,@hash,@salt,@created,@expires,0);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("@id", challengeId);
            insert.Parameters.AddWithValue("@tenant", tenantId);
            insert.Parameters.AddWithValue("@mobile", mobile);
            insert.Parameters.AddWithValue("@hash", hash);
            insert.Parameters.AddWithValue("@salt", salt);
            insert.Parameters.AddWithValue("@created", now);
            insert.Parameters.AddWithValue("@expires", now.AddMinutes(ExpiryMinutes));
            await insert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        try { await sender.SendAsync(mobile, otp, challengeId, token); }
        catch
        {
            await using var failed = await Open(tenantId.Value, token);
            await using var consume = new SqlCommand("UPDATE commerce.StorefrontOtpChallenges SET ConsumedAt=SYSUTCDATETIME() WHERE ChallengeId=@id AND TenantId=@tenant;", failed);
            consume.Parameters.AddWithValue("@id", challengeId);
            consume.Parameters.AddWithValue("@tenant", tenantId);
            await consume.ExecuteNonQueryAsync(token);
            throw new BusinessRuleException("The verification code could not be sent. Please try again.");
        }
        return new(challengeId, now.AddMinutes(ExpiryMinutes), CooldownSeconds);
    }

    public async Task<StorefrontAuthenticationDto?> VerifyOtpAsync(string storeKey, StorefrontOtpVerifyInput input, CancellationToken token)
    {
        var tenantId = await ResolveTenant(storeKey, token);
        var mobile = NormalizeMobile(input.MobileNumber);
        if (tenantId is null || mobile is null || input.ChallengeId == Guid.Empty || !OtpPattern().IsMatch(input.Otp ?? string.Empty)) return null;
        var email = NormalizeEmail(input.Email);
        if (!string.IsNullOrWhiteSpace(input.Email) && email is null) throw new BusinessRuleException("Enter a valid email address.");
        var name = input.Name?.Trim();
        if (input.IsRegistration && string.IsNullOrWhiteSpace(name)) throw new BusinessRuleException("Name is required.");
        if (!string.IsNullOrWhiteSpace(name) && name.Length > 250) throw new BusinessRuleException("Enter a valid customer name.");
        if (input.ReplacePassword || (!string.IsNullOrWhiteSpace(input.Password) || !string.IsNullOrWhiteSpace(input.ConfirmPassword))) ValidatePassword(input.Password, input.ConfirmPassword);

        await using var connection = await Open(tenantId.Value, token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        byte[] expected, salt;
        DateTimeOffset expires;
        int attempts;
        DateTimeOffset? consumed;
        await using (var select = new SqlCommand("""
            SELECT OtpHash,OtpSalt,ExpiresAt,AttemptCount,ConsumedAt
            FROM commerce.StorefrontOtpChallenges WITH(UPDLOCK,HOLDLOCK)
            WHERE ChallengeId=@id AND TenantId=@tenant AND MobileNumberNormalized=@mobile;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("@id", input.ChallengeId);
            select.Parameters.AddWithValue("@tenant", tenantId);
            select.Parameters.AddWithValue("@mobile", mobile);
            await using var reader = await select.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) { await transaction.RollbackAsync(token); return null; }
            expected=(byte[])reader[0];salt=(byte[])reader[1];expires=reader.GetDateTimeOffset(2);attempts=reader.GetInt32(3);consumed=reader.IsDBNull(4)?null:reader.GetDateTimeOffset(4);
        }
        if (!CanVerifyChallenge(expires, consumed, attempts, DateTimeOffset.UtcNow))
        { await transaction.RollbackAsync(token); return null; }
        if (!OtpMatches(expected, salt, input.Otp!))
        {
            await using var fail = new SqlCommand("UPDATE commerce.StorefrontOtpChallenges SET AttemptCount=AttemptCount+1 WHERE ChallengeId=@id AND TenantId=@tenant;", connection, transaction);
            fail.Parameters.AddWithValue("@id", input.ChallengeId); fail.Parameters.AddWithValue("@tenant", tenantId);
            await fail.ExecuteNonQueryAsync(token); await transaction.CommitAsync(token); return null;
        }
        await using (var consume = new SqlCommand("UPDATE commerce.StorefrontOtpChallenges SET ConsumedAt=SYSUTCDATETIME() WHERE ChallengeId=@id AND TenantId=@tenant AND ConsumedAt IS NULL;", connection, transaction))
        { consume.Parameters.AddWithValue("@id",input.ChallengeId);consume.Parameters.AddWithValue("@tenant",tenantId);if(await consume.ExecuteNonQueryAsync(token)!=1){await transaction.RollbackAsync(token);return null;} }
        var passwordHash = !string.IsNullOrWhiteSpace(input.Password) ? Passwords.HashPassword(new object(), input.Password) : null;
        // OTP login must authenticate an existing customer; only the explicit signup
        // flow may create one. This also prevents a password-reset OTP from creating
        // a new account when the supplied mobile number is unknown.
        var customerId = await ResolveCustomer(connection, transaction, tenantId.Value, storeKey, mobile, name, email, passwordHash, input, token);
        if (customerId is null)
        {
            await transaction.RollbackAsync(token);
            return null;
        }
        await transaction.CommitAsync(token);
        var session = await customers.IssueSessionAsync(storeKey, tenantId.Value, customerId.Value, token);
        var customer = await customers.GetSessionAsync(storeKey, session, token);
        return customer is null ? null : new(session, customer);
    }

    public async Task<StorefrontAuthenticationDto?> SignInPasswordAsync(string storeKey, StorefrontPasswordSignInInput input, CancellationToken token)
    {
        var tenantId = await ResolveTenant(storeKey, token);
        var mobile = NormalizeMobile(input.MobileNumber);
        if (tenantId is null || mobile is null || string.IsNullOrWhiteSpace(input.Password)) return null;
        await using var connection = await Open(tenantId.Value, token);
        await using var command = new SqlCommand("""
            SELECT TOP(1) CustomerId,StorefrontPasswordHash
            FROM sales.Customers
            WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0
              AND (MobileNormalized=@mobile OR (MobileNormalized IS NULL AND RIGHT(REPLACE(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),N'(',N''),10)=@mobile));
            """, connection);
        command.Parameters.AddWithValue("@tenant", tenantId.Value); command.Parameters.AddWithValue("@mobile", mobile);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token) || reader.IsDBNull(1)) return null;
        var customerId = reader.GetGuid(0); var hash = reader.GetString(1);
        if (Passwords.VerifyHashedPassword(new object(), hash, input.Password) == PasswordVerificationResult.Failed) return null;
        var session = await customers.IssueSessionAsync(storeKey, tenantId.Value, customerId, token);
        var customer = await customers.GetSessionAsync(storeKey, session, token);
        return customer is null ? null : new(session, customer);
    }

    public async Task<bool> ResetPasswordAsync(string storeKey, StorefrontPasswordResetInput input, CancellationToken token)
    {
        ValidatePassword(input.NewPassword, input.ConfirmPassword);
        var tenantId = await ResolveTenant(storeKey, token); var mobile = NormalizeMobile(input.MobileNumber);
        if (tenantId is null || mobile is null) return false;
        var result = await VerifyOtpAsync(storeKey, new StorefrontOtpVerifyInput(input.ChallengeId, input.MobileNumber, input.Otp, null, null, false, input.NewPassword, input.ConfirmPassword, true), token);
        return result is not null;
    }

    public async Task<bool> ChangePasswordAsync(string storeKey, string sessionToken, StorefrontChangePasswordInput input, CancellationToken token)
    {
        ValidatePassword(input.NewPassword, input.ConfirmPassword);
        var current = await customers.GetSessionAsync(storeKey, sessionToken, token); if (current is null) return false;
        var tenantId = await ResolveTenant(storeKey, token); if (tenantId is null) return false;
        await using var connection = await Open(tenantId.Value, token);
        await using var read = new SqlCommand("SELECT StorefrontPasswordHash FROM sales.Customers WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0;", connection);
        read.Parameters.AddWithValue("@tenant",tenantId.Value); read.Parameters.AddWithValue("@customer",current.Id);
        var hash = await read.ExecuteScalarAsync(token) as string;
        if (hash is null || Passwords.VerifyHashedPassword(new object(), hash, input.CurrentPassword) == PasswordVerificationResult.Failed) return false;
        await using var update = new SqlCommand("UPDATE sales.Customers SET StorefrontPasswordHash=@hash,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'STOREFRONT_PASSWORD' WHERE TenantId=@tenant AND CustomerId=@customer;", connection);
        update.Parameters.AddWithValue("@hash",Passwords.HashPassword(new object(),input.NewPassword)); update.Parameters.AddWithValue("@tenant",tenantId.Value); update.Parameters.AddWithValue("@customer",current.Id); await update.ExecuteNonQueryAsync(token); return true;
    }

    public async Task<StorefrontCustomerDto?> UpdateProfileAsync(string storeKey, string sessionToken, UpdateStorefrontCustomerInput input, CancellationToken token)
    {
        var current = await customers.GetSessionAsync(storeKey, sessionToken, token);
        if (current is null) return null;
        var name = input.Name?.Trim();
        var email = NormalizeEmail(input.Email);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 250) throw new BusinessRuleException("Enter a valid customer name.");
        if (!string.IsNullOrWhiteSpace(input.Email) && email is null) throw new BusinessRuleException("Enter a valid email address.");
        var tenantId = await ResolveTenant(storeKey, token);
        if (tenantId is null) return null;
        await using var connection = await Open(tenantId.Value, token);
        await using var command = new SqlCommand("UPDATE sales.Customers SET CustomerName=@name,Email=@email,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'STOREFRONT' WHERE TenantId=@tenant AND CustomerId=@customer AND IsActive=1 AND IsDeleted=0;", connection);
        command.Parameters.AddWithValue("@name", name);command.Parameters.AddWithValue("@email",email??(object)DBNull.Value);command.Parameters.AddWithValue("@tenant",tenantId);command.Parameters.AddWithValue("@customer",current.Id);
        if(await command.ExecuteNonQueryAsync(token)!=1)return null;
        return new(current.Id,name,email,current.Mobile);
    }

    private static void ValidatePassword(string? password, string? confirmation)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 128 || password.Any(char.IsWhiteSpace) || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new BusinessRuleException("Password must be at least 8 characters and include a letter and a number.");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal)) throw new BusinessRuleException("Passwords do not match.");
    }
    internal static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length != 10 || digits[0] is < '6' or > '9') return null;
        return digits;
    }
    internal static byte[] HashOtp(string otp, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(otp, salt, 100_000, HashAlgorithmName.SHA256, 32);
    internal static bool CanVerifyChallenge(DateTimeOffset expires, DateTimeOffset? consumed, int attempts, DateTimeOffset now)
        => consumed is null && expires > now && attempts < MaxAttempts;
    internal static bool OtpMatches(byte[] expected, byte[] salt, string otp)
        => CryptographicOperations.FixedTimeEquals(expected, HashOtp(otp, salt));
    private static string? NormalizeEmail(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : MailAddress.TryCreate(value.Trim(), out var address) && value.Trim().Length <= 256 ? address.Address.ToLowerInvariant() : null;

    private static async Task<Guid?> ResolveCustomer(SqlConnection connection, SqlTransaction transaction, Guid tenantId, string storeKey, string mobile, string? name, string? email, string? passwordHash, StorefrontOtpVerifyInput input, CancellationToken token)
    {
        await using var find = new SqlCommand("""
            SELECT TOP(1) CustomerId FROM sales.Customers WITH(UPDLOCK,HOLDLOCK)
            WHERE TenantId=@tenant AND IsActive=1 AND IsDeleted=0
              AND (MobileNormalized=@mobile OR (MobileNormalized IS NULL AND RIGHT(REPLACE(REPLACE(REPLACE(REPLACE(Mobile,N' ',N''),N'+',N''),N'-',N''),N'(',N''),10)=@mobile))
            ORDER BY CreatedOn;
            """, connection, transaction);
        find.Parameters.AddWithValue("@tenant",tenantId);find.Parameters.AddWithValue("@mobile",mobile);
        if(await find.ExecuteScalarAsync(token) is Guid existing)
        {
            await using var update = new SqlCommand("UPDATE sales.Customers SET MobileNormalized=@mobile,CustomerName=COALESCE(NULLIF(@name,N''),CustomerName),Email=COALESCE(@email,Email),StorefrontPasswordHash=CASE WHEN @replacePassword=1 THEN @password ELSE StorefrontPasswordHash END,ModifiedOn=SYSUTCDATETIME(),ModifiedBy=N'STOREFRONT_OTP' WHERE CustomerId=@id AND TenantId=@tenant;",connection,transaction);
            update.Parameters.AddWithValue("@mobile",mobile);update.Parameters.AddWithValue("@name",name??(object)DBNull.Value);update.Parameters.AddWithValue("@password",passwordHash??(object)DBNull.Value);update.Parameters.AddWithValue("@replacePassword",input.ReplacePassword);update.Parameters.AddWithValue("@email",email??(object)DBNull.Value);update.Parameters.AddWithValue("@id",existing);update.Parameters.AddWithValue("@tenant",tenantId);await update.ExecuteNonQueryAsync(token);
            return existing;
        }
        if (!input.IsRegistration) return null;
        var terms = await ReadRequiredPolicy(connection, transaction, tenantId, "CUSTOMER_TERMS", input.TermsPolicyId, input.TermsContentHash, input.AcceptTerms == true, token);
        var privacy = await ReadRequiredPolicy(connection, transaction, tenantId, "PRIVACY_NOTICE", input.PrivacyPolicyId, input.PrivacyContentHash, input.AcknowledgePrivacy == true, token);
        var id=Guid.NewGuid();
        await using var insert=new SqlCommand("""
            INSERT sales.Customers(CustomerId,TenantId,CustomerCode,CustomerName,CustomerType,Email,Mobile,MobileNormalized,StorefrontPasswordHash,Currency,CreditLimit,OpeningBalance,IsGSTRegistered,IsActive,IsDeleted,Remarks,CreatedBy)
            VALUES(@id,@tenant,@code,@name,N'RETAIL',@email,@mobile,@mobile,@password,N'INR',0,0,0,1,0,N'Verified storefront customer',N'STOREFRONT_OTP');
            """,connection,transaction);
        insert.Parameters.AddWithValue("@id",id);insert.Parameters.AddWithValue("@tenant",tenantId);insert.Parameters.AddWithValue("@code",$"WEB-{id:N}"[..16]);insert.Parameters.AddWithValue("@name",name??$"Customer {mobile[^4..]}");insert.Parameters.AddWithValue("@email",email??(object)DBNull.Value);insert.Parameters.AddWithValue("@mobile",mobile);insert.Parameters.AddWithValue("@password",passwordHash??(object)DBNull.Value);await insert.ExecuteNonQueryAsync(token);
        await RecordPolicyAcceptance(connection, transaction, tenantId, id, terms, storeKey, token);
        await RecordPolicyAcceptance(connection, transaction, tenantId, id, privacy, storeKey, token);
        return id;
    }

    private static async Task<(Guid Id, string Version, byte[] Hash)> ReadRequiredPolicy(SqlConnection connection, SqlTransaction transaction, Guid tenantId, string type, Guid? submittedId, string? submittedHash, bool accepted, CancellationToken token)
    {
        if (!accepted || submittedId is null || string.IsNullOrWhiteSpace(submittedHash))
            throw new BusinessRuleException("Please accept the current Terms & Conditions and acknowledge the Privacy Policy to create your account.");
        await using var command = new SqlCommand("""
            SELECT TOP(1) PolicyId,Version,ContentHash FROM commerce.StorefrontPolicies WITH(UPDLOCK,HOLDLOCK)
            WHERE PolicyId=@id AND PolicyType=@type AND (TenantId=@tenant OR TenantId IS NULL)
              AND LanguageCode=N'en' AND IsPublished=1 AND IsActive=1 AND AcceptanceRequired=1 AND EffectiveAtUtc<=SYSUTCDATETIME()
              AND (TenantId=@tenant OR NOT EXISTS(SELECT 1 FROM commerce.StorefrontPolicies tenantPolicy WITH(UPDLOCK,HOLDLOCK) WHERE tenantPolicy.PolicyType=@type AND tenantPolicy.TenantId=@tenant AND tenantPolicy.LanguageCode=N'en' AND tenantPolicy.IsPublished=1 AND tenantPolicy.IsActive=1 AND tenantPolicy.AcceptanceRequired=1 AND tenantPolicy.EffectiveAtUtc<=SYSUTCDATETIME()))
            ORDER BY CASE WHEN TenantId=@tenant THEN 0 ELSE 1 END;
            """, connection, transaction);
        command.Parameters.AddWithValue("@id", submittedId.Value); command.Parameters.AddWithValue("@type", type); command.Parameters.AddWithValue("@tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new BusinessRuleException("The current customer policy is unavailable. Please try again later.");
        var id = reader.GetGuid(0); var version = reader.GetString(1); var hash = (byte[])reader[2];
        if (!CryptographicOperations.FixedTimeEquals(hash, DecodeHash(submittedHash))) throw new BusinessRuleException("The customer policy changed. Review the current policy and try again.");
        return (id, version, hash);
    }

    private static byte[] DecodeHash(string value)
    {
        try { var hash = Convert.FromHexString(value); return hash.Length == 32 ? hash : new byte[32]; }
        catch (FormatException) { return new byte[32]; }
    }

    private static async Task RecordPolicyAcceptance(SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid customerId, (Guid Id, string Version, byte[] Hash) policy, string storeKey, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            INSERT commerce.StorefrontPolicyAcceptances(AcceptanceId,TenantId,CustomerId,PolicyId,PolicyType,PolicyVersion,ContentHash,AcceptedAtUtc,AcceptanceSource,StoreKey)
            VALUES(NEWID(),@tenant,@customer,@policy,(SELECT PolicyType FROM commerce.StorefrontPolicies WHERE PolicyId=@policy),@version,@hash,SYSUTCDATETIME(),N'CUSTOMER_SIGNUP',@storeKey);
            """, connection, transaction);
        command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@customer", customerId); command.Parameters.AddWithValue("@policy", policy.Id); command.Parameters.AddWithValue("@version", policy.Version); command.Parameters.AddWithValue("@hash", policy.Hash); command.Parameters.AddWithValue("@storeKey", storeKey.Trim().ToUpperInvariant());
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<Guid?> ResolveTenant(string storeKey,CancellationToken token)
    {
        var key=storeKey?.Trim().ToUpperInvariant();if(string.IsNullOrWhiteSpace(key)||!StoreKeyPattern().IsMatch(key))return null;
        await using var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("SELECT TenantId FROM core.Tenants WHERE TenantKey=@key AND IsActive=1;",c);q.Parameters.AddWithValue("@key",key);return await q.ExecuteScalarAsync(token) is Guid id?id:null;
    }
    private async Task<SqlConnection> Open(Guid tenantId,CancellationToken token){var c=new SqlConnection(ConnectionString);await c.OpenAsync(token);await using var q=new SqlCommand("EXEC sys.sp_set_session_context @key=N'TenantId',@value=@tenant;",c);q.Parameters.AddWithValue("@tenant",tenantId);await q.ExecuteNonQueryAsync(token);return c;}
    [GeneratedRegex("^[0-9]{6}$",RegexOptions.CultureInvariant)] private static partial Regex OtpPattern();
    [GeneratedRegex("^[A-Z0-9_-]{1,100}$",RegexOptions.CultureInvariant)] private static partial Regex StoreKeyPattern();
}
