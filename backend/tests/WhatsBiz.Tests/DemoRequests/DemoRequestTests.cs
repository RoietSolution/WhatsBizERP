using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.DemoRequests;
using WhatsBiz.Infrastructure.DemoRequests;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.DemoRequests;

public sealed class DemoRequestTests
{
    private const string LogoUrl = "https://khatadhari-public-assets.s3.ap-south-1.amazonaws.com/KhataDhari_Logo.png";
    private const string FeatureImageUrl = "https://khatadhari-public-assets.s3.ap-south-1.amazonaws.com/khatadhari-features.png";

    [Fact]
    public async Task ValidSubmissionIsNormalizedAttributedAndNotified()
    {
        var repository = new FakeRepository();
        var notification = new FakeNotification();
        var sender = Sender(repository, notification);
        var result = await sender.Send(new SubmitDemoRequest(Valid() with { Mobile = "+91 98765-43210", UtmSource = " instagram " }, "203.0.113.9", "test-agent"));
        result.Success.Should().BeTrue();
        result.ReferenceNo.Should().Be("KD-000123");
        repository.CreatedInput!.Mobile.Should().Be("+919876543210");
        repository.Source.Should().Be("instagram");
        notification.Calls.Should().Be(1);
        repository.NotificationStatus.Should().Be("SENT");
    }

    [Theory]
    [InlineData("", "9876543210", null)]
    [InlineData("Rahul", "123", null)]
    [InlineData("Rahul", "9876543210", "not-an-email")]
    public async Task InvalidSubmissionIsRejected(string name, string mobile, string? email)
    {
        var sender = Sender(new FakeRepository(), new FakeNotification());
        var act = () => sender.Send(new SubmitDemoRequest(Valid() with { Name = name, Mobile = mobile, Email = email }, "203.0.113.9", "test"));
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task DuplicateSubmissionReturnsOriginalReferenceWithoutSecondEmail()
    {
        var repository = new FakeRepository { Duplicate = true };
        var notification = new FakeNotification();
        var result = await Sender(repository, notification).Send(new SubmitDemoRequest(Valid(), "203.0.113.9", "test"));
        result.Duplicate.Should().BeTrue();
        result.ReferenceNo.Should().Be("KD-000123");
        notification.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NotificationFailureDoesNotLoseSuccessfulLead()
    {
        var repository = new FakeRepository();
        var result = await Sender(repository, new FakeNotification { Throw = true }).Send(new SubmitDemoRequest(Valid(), "203.0.113.9", "test"));
        result.Success.Should().BeTrue();
        result.LeadId.Should().Be(123);
        repository.CreatedInput.Should().NotBeNull();
    }

    [Fact]
    public async Task NotificationWithRequesterEmailSendsInternalAndAcknowledgementToCorrectRecipients()
    {
        var sender = new FakeEmailSender();
        var service = NotificationService(sender);

        var status = await service.NotifyAsync(Detail("requester@example.com"), CancellationToken.None);

        status.Should().Be("SENT");
        sender.Emails.Should().HaveCount(2);
        sender.Emails[0].Recipient.Should().Be("support@khatadhari.com");
        sender.Emails[0].Subject.Should().Be("New KhataDhari Demo Request - KD-000123");
        sender.Emails[0].IsBodyHtml.Should().BeFalse();
        sender.Emails[1].Recipient.Should().Be("requester@example.com");
        sender.Emails[1].Subject.Should().Be("Your KhataDhari Demo Request Has Been Received");
        sender.Emails[1].IsBodyHtml.Should().BeTrue();
        sender.Emails[1].Body.Should().Contain("team will contact you shortly");
        sender.Emails[1].Body.Should().Contain($"src=\"{LogoUrl}\"");
        sender.Emails[1].Body.Should().Contain("alt=\"KhataDhari\"");
        sender.Emails[1].Body.Should().Contain($"src=\"{FeatureImageUrl}\"");
        sender.Emails[1].Body.Should().Contain("alt=\"KhataDhari All-in-One Retail ERP and WhatsApp Commerce Features\"");
        sender.Emails[1].Body.IndexOf("What happens next?", StringComparison.Ordinal)
            .Should().BeLessThan(sender.Emails[1].Body.IndexOf(FeatureImageUrl, StringComparison.Ordinal));
        sender.Emails[1].Body.IndexOf(FeatureImageUrl, StringComparison.Ordinal)
            .Should().BeLessThan(sender.Emails[1].Body.IndexOf("KhataDhari Team", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("http://assets.example.test/logo.png", "file:///tmp/features.png")]
    [InlineData("https://localhost/logo.png", "https://user:secret@assets.example.test/features.png")]
    public async Task MissingOrUnsafeImageUrlsAreOmittedWithoutBlockingAcknowledgement(string? logoUrl, string? featureImageUrl)
    {
        var sender = new FakeEmailSender();

        var status = await NotificationService(sender, logoUrl: logoUrl, featureImageUrl: featureImageUrl)
            .NotifyAsync(Detail("requester@example.com"), CancellationToken.None);

        status.Should().Be("SENT");
        var acknowledgement = sender.Emails.Single(x => x.Recipient == "requester@example.com");
        acknowledgement.IsBodyHtml.Should().BeTrue();
        acknowledgement.Body.Should().NotContain("<img");
        acknowledgement.Body.Should().Contain("What happens next?");
    }

    [Fact]
    public async Task RequesterValuesAreHtmlEncodedAndCannotInjectEmailMarkup()
    {
        var sender = new FakeEmailSender();
        var request = Detail("requester@example.com") with { Name = "<script>alert('x')</script>", ReferenceNo = "KD-<123>" };

        await NotificationService(sender).NotifyAsync(request, CancellationToken.None);

        var body = sender.Emails.Single(x => x.Recipient == "requester@example.com").Body;
        body.Should().NotContain("<script>");
        body.Should().Contain("&lt;script&gt;");
        body.Should().Contain("KD-&lt;123&gt;");
    }

    [Fact]
    public async Task NotificationWithoutRequesterEmailSendsOnlyInternalNotification()
    {
        var sender = new FakeEmailSender();

        var status = await NotificationService(sender).NotifyAsync(Detail(null), CancellationToken.None);

        status.Should().Be("SENT");
        sender.Emails.Should().ContainSingle().Which.Recipient.Should().Be("support@khatadhari.com");
    }

    [Fact]
    public async Task InternalNotificationFailureDoesNotPreventRequesterAcknowledgement()
    {
        var sender = new FakeEmailSender { FailRecipient = "support@khatadhari.com" };

        var status = await NotificationService(sender).NotifyAsync(Detail("requester@example.com"), CancellationToken.None);

        status.Should().Be("FAILED");
        sender.AttemptedRecipients.Should().Equal("support@khatadhari.com", "requester@example.com");
        sender.Emails.Should().ContainSingle().Which.Recipient.Should().Be("requester@example.com");
    }

    [Fact]
    public async Task EmailFailureLoggingDoesNotLeakPasswordOrProviderExceptionText()
    {
        const string secret = "smtp-password-must-not-appear";
        var logger = new ListLogger<DemoRequestNotificationService>();
        var sender = new FakeEmailSender { FailRecipient = "requester@example.com", FailureMessage = secret };

        var status = await NotificationService(sender, logger, secret).NotifyAsync(Detail("requester@example.com"), CancellationToken.None);

        status.Should().Be("SENT");
        sender.Emails.Should().ContainSingle().Which.Recipient.Should().Be("support@khatadhari.com");
        logger.Messages.Should().Contain(message => message.Contains("requester acknowledgement failed", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void PublicSubmissionAndAuthorizedStatusUpdateHaveRequiredSecurityAttributes()
    {
        var submit = typeof(DemoRequestsController).GetMethod(nameof(DemoRequestsController.Submit))!;
        submit.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Should().ContainSingle();
        var update = typeof(DemoRequestsController).GetMethod(nameof(DemoRequestsController.UpdateStatus))!;
        var permission = update.GetCustomAttributes(typeof(HasPermissionAttribute), true).Cast<HasPermissionAttribute>().Single();
        permission.Policy.Should().EndWith(Permissions.Admin.Settings);
        typeof(DemoRequestsController).GetCustomAttributes(typeof(ApiControllerAttribute), true).Should().ContainSingle();
    }

    private static ISender Sender(FakeRepository repository, FakeNotification notification)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IDemoRequestRepository>(repository);
        services.AddSingleton<IDemoRequestNotificationService>(notification);
        services.AddSingleton<IDemoRequestCaptchaVerifier>(new FakeCaptcha());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static DemoRequestInput Valid() => new("Rahul Sharma", "9876543210", "rahul@example.com", "Rahul Saree Centre", "Delhi", "Saree / Garments / Boutique", "Interested in a demo.", null, null, null, null, "https://khatadhari.com/", null);

    private static DemoRequestNotificationService NotificationService(
        FakeEmailSender sender,
        ILogger<DemoRequestNotificationService>? logger = null,
        string password = "test-password",
        string? logoUrl = LogoUrl,
        string? featureImageUrl = FeatureImageUrl) =>
        new(Options.Create(new DemoRequestOptions
        {
            Email = new SmtpOptions
            {
                Enabled = true,
                Host = "smtp.test",
                Port = 587,
                EnableSsl = true,
                Username = "website@khatadhari.com",
                Password = password,
                FromAddress = "website@khatadhari.com",
                FromName = "KhataDhari Website",
                SupportAddress = "support@khatadhari.com",
                LogoUrl = logoUrl,
                FeatureImageUrl = featureImageUrl
            }
        }), sender, logger ?? new ListLogger<DemoRequestNotificationService>());

    private static DemoRequestDetail Detail(string? email) =>
        new(123, "KD-000123", "Rahul Sharma", "9876543210", email, "Rahul Saree Centre", "Delhi", "Retail", "Interested in a demo.", "Website", null, null, null, null, null, null, "NEW", "PENDING", DateTimeOffset.UtcNow, null);

    private sealed class FakeCaptcha : IDemoRequestCaptchaVerifier { public Task<bool> VerifyAsync(string? tokenValue, string? ipAddress, CancellationToken token) => Task.FromResult(true); }
    private sealed class FakeNotification : IDemoRequestNotificationService
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }
        public Task<string> NotifyAsync(DemoRequestDetail request, CancellationToken token) { Calls++; return Throw ? Task.FromException<string>(new InvalidOperationException("SMTP unavailable")) : Task.FromResult("SENT"); }
    }
    private sealed class FakeEmailSender : IDemoRequestEmailSender
    {
        public List<DemoRequestEmail> Emails { get; } = [];
        public List<string> AttemptedRecipients { get; } = [];
        public string? FailRecipient { get; init; }
        public string FailureMessage { get; init; } = "SMTP unavailable";

        public Task SendAsync(DemoRequestEmail email, CancellationToken token)
        {
            AttemptedRecipients.Add(email.Recipient);
            if (email.Recipient == FailRecipient) throw new InvalidOperationException(FailureMessage);
            Emails.Add(email);
            return Task.CompletedTask;
        }
    }
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
    private sealed class FakeRepository : IDemoRequestRepository
    {
        public bool Duplicate { get; init; }
        public DemoRequestInput? CreatedInput { get; private set; }
        public string? Source { get; private set; }
        public string? NotificationStatus { get; private set; }
        public Task<DemoRequestCreateResult> CreateAsync(DemoRequestInput input, string source, string? ipAddress, string? userAgent, CancellationToken token) { CreatedInput = input; Source = source; return Task.FromResult(new DemoRequestCreateResult(123, "KD-000123", Duplicate)); }
        public Task<DemoRequestDetail> GetAsync(long id, CancellationToken token) => Task.FromResult(new DemoRequestDetail(id, "KD-000123", "Rahul Sharma", "9876543210", null, null, null, null, null, "Website", null, null, null, null, null, null, "NEW", "PENDING", DateTimeOffset.UtcNow, null));
        public Task<PagedDemoRequests> SearchAsync(string? search, string? status, DateTimeOffset? fromDate, DateTimeOffset? toDate, int pageNumber, int pageSize, CancellationToken token) => Task.FromResult(new PagedDemoRequests([], 0, 1, 25));
        public Task SetNotificationStatusAsync(long id, string status, CancellationToken token) { NotificationStatus = status; return Task.CompletedTask; }
        public Task<DemoRequestDetail> UpdateStatusAsync(long id, string status, string? user, CancellationToken token) => GetAsync(id, token);
    }
}
