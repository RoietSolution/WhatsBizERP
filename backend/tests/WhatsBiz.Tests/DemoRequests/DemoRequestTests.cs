using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using WhatsBiz.Api.Authorization;
using WhatsBiz.Api.Controllers;
using WhatsBiz.Application;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.DemoRequests;
using WhatsBiz.SharedKernel;

namespace WhatsBiz.Tests.DemoRequests;

public sealed class DemoRequestTests
{
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
        var result = await Sender(new FakeRepository(), new FakeNotification { Throw = true }).Send(new SubmitDemoRequest(Valid(), "203.0.113.9", "test"));
        result.Success.Should().BeTrue();
        result.LeadId.Should().Be(123);
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
        services.AddApplication();
        services.AddSingleton<IDemoRequestRepository>(repository);
        services.AddSingleton<IDemoRequestNotificationService>(notification);
        services.AddSingleton<IDemoRequestCaptchaVerifier>(new FakeCaptcha());
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static DemoRequestInput Valid() => new("Rahul Sharma", "9876543210", "rahul@example.com", "Rahul Saree Centre", "Delhi", "Saree / Garments / Boutique", "Interested in a demo.", null, null, null, null, "https://khatadhari.com/", null);

    private sealed class FakeCaptcha : IDemoRequestCaptchaVerifier { public Task<bool> VerifyAsync(string? tokenValue, string? ipAddress, CancellationToken token) => Task.FromResult(true); }
    private sealed class FakeNotification : IDemoRequestNotificationService
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }
        public Task<string> NotifyAsync(DemoRequestDetail request, CancellationToken token) { Calls++; return Throw ? Task.FromException<string>(new InvalidOperationException("SMTP unavailable")) : Task.FromResult("SENT"); }
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
