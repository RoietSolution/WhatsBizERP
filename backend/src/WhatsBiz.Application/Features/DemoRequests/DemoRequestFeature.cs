using FluentValidation;
using MediatR;
using System.Text.RegularExpressions;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Application.Features.DemoRequests;

public static class DemoRequestStatuses
{
    public static readonly IReadOnlyCollection<string> All =
    [
        "NEW", "CONTACTED", "FOLLOW_UP", "DEMO_SCHEDULED", "DEMO_COMPLETED",
        "TRIAL_STARTED", "CONVERTED", "NOT_INTERESTED", "LOST"
    ];
}

public sealed record DemoRequestInput(
    string Name,
    string Mobile,
    string? Email,
    string? BusinessName,
    string? City,
    string? BusinessType,
    string? Message,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmContent,
    string? LandingPage,
    string? Referrer,
    string? CaptchaToken = null);

public sealed record DemoRequestSubmissionResult(
    bool Success,
    long LeadId,
    string ReferenceNo,
    string Message,
    bool Duplicate = false);

public sealed record DemoRequestSummary(
    long Id,
    string ReferenceNo,
    string Name,
    string Mobile,
    string? BusinessName,
    string? BusinessType,
    string? City,
    string Source,
    DateTimeOffset CreatedOn,
    string Status);

public sealed record DemoRequestDetail(
    long Id,
    string ReferenceNo,
    string Name,
    string Mobile,
    string? Email,
    string? BusinessName,
    string? City,
    string? BusinessType,
    string? Message,
    string Source,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmContent,
    string? LandingPage,
    string? Referrer,
    string Status,
    string NotificationStatus,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ModifiedOn);

public sealed record PagedDemoRequests(
    IReadOnlyCollection<DemoRequestSummary> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record DemoRequestConfiguration(
    string? WhatsAppContactNumber,
    bool CaptchaEnabled,
    string? CaptchaSiteKey);

public sealed record SubmitDemoRequest(DemoRequestInput Input, string? IpAddress, string? UserAgent)
    : IRequest<DemoRequestSubmissionResult>;
public sealed record SearchDemoRequests(string? Search, string? Status, DateTimeOffset? From, DateTimeOffset? To, int PageNumber = 1, int PageSize = 25)
    : IRequest<PagedDemoRequests>;
public sealed record GetDemoRequest(long Id) : IRequest<DemoRequestDetail>;
public sealed record UpdateDemoRequestStatus(long Id, string Status, string? User) : IRequest<DemoRequestDetail>;

internal sealed partial class SubmitDemoRequestValidator : AbstractValidator<SubmitDemoRequest>
{
    public SubmitDemoRequestValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(100).Must(BePlainText).WithMessage("Name must not contain HTML markup.");
        RuleFor(x => x.Input.Mobile).NotEmpty().MaximumLength(24).Must(BeValidMobile).WithMessage("Enter a valid mobile number with 10 to 15 digits.");
        RuleFor(x => x.Input.Email).MaximumLength(254).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Input.Email));
        RuleFor(x => x.Input.BusinessName).MaximumLength(150).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.BusinessName));
        RuleFor(x => x.Input.City).MaximumLength(100).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.City));
        RuleFor(x => x.Input.BusinessType).MaximumLength(100).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.BusinessType));
        RuleFor(x => x.Input.Message).MaximumLength(2000).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.Message));
        RuleFor(x => x.Input.UtmSource).MaximumLength(100).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.UtmSource));
        RuleFor(x => x.Input.UtmMedium).MaximumLength(100).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.UtmMedium));
        RuleFor(x => x.Input.UtmCampaign).MaximumLength(150).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.UtmCampaign));
        RuleFor(x => x.Input.UtmContent).MaximumLength(150).Must(BePlainText).When(x => !string.IsNullOrWhiteSpace(x.Input.UtmContent));
        RuleFor(x => x.Input.LandingPage).MaximumLength(2048).Must(BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.Input.LandingPage));
        RuleFor(x => x.Input.Referrer).MaximumLength(2048).Must(BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.Input.Referrer));
        RuleFor(x => x.Input.CaptchaToken).MaximumLength(4096).When(x => !string.IsNullOrWhiteSpace(x.Input.CaptchaToken));
    }

    private static bool BeValidMobile(string mobile)
    {
        if (!MobileCharacters().IsMatch(mobile)) return false;
        var digits = NonDigits().Replace(mobile, string.Empty);
        return digits.Length is >= 10 and <= 15;
    }

    private static bool BePlainText(string? value) => value is null || !HtmlTag().IsMatch(value);
    private static bool BeHttpUrl(string? value) => value is null || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    [GeneratedRegex("^\\+?[0-9][0-9 ()-]*$")]
    private static partial Regex MobileCharacters();
    [GeneratedRegex("[^0-9]")]
    private static partial Regex NonDigits();
    [GeneratedRegex("<\\s*/?\\s*[a-z][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTag();
}

internal sealed class UpdateDemoRequestStatusValidator : AbstractValidator<UpdateDemoRequestStatus>
{
    public UpdateDemoRequestStatusValidator() => RuleFor(x => x.Status).Must(x => DemoRequestStatuses.All.Contains(x, StringComparer.OrdinalIgnoreCase)).WithMessage("Unsupported demo request status.");
}

public sealed class DemoRequestHandlers(
    IDemoRequestRepository repository,
    IDemoRequestNotificationService notifications,
    IDemoRequestCaptchaVerifier captcha)
    : IRequestHandler<SubmitDemoRequest, DemoRequestSubmissionResult>,
      IRequestHandler<SearchDemoRequests, PagedDemoRequests>,
      IRequestHandler<GetDemoRequest, DemoRequestDetail>,
      IRequestHandler<UpdateDemoRequestStatus, DemoRequestDetail>
{
    public async Task<DemoRequestSubmissionResult> Handle(SubmitDemoRequest request, CancellationToken cancellationToken)
    {
        if (!await captcha.VerifyAsync(request.Input.CaptchaToken, request.IpAddress, cancellationToken))
            throw new ValidationException("Bot verification failed. Please refresh the page and try again.");

        var input = Normalize(request.Input);
        var source = ResolveSource(input.UtmSource, input.Referrer);
        var created = await repository.CreateAsync(input, source, request.IpAddress, request.UserAgent, cancellationToken);
        if (!created.Duplicate)
        {
            try
            {
                var notificationStatus = await notifications.NotifyAsync(await repository.GetAsync(created.Id, cancellationToken), cancellationToken);
                await repository.SetNotificationStatusAsync(created.Id, notificationStatus, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // The lead is durable at this point. Notification implementations log provider failures.
            }
        }

        return new(true, created.Id, created.ReferenceNo, "Demo request submitted successfully.", created.Duplicate);
    }

    public Task<PagedDemoRequests> Handle(SearchDemoRequests request, CancellationToken cancellationToken) =>
        repository.SearchAsync(request.Search, request.Status, request.From, request.To, request.PageNumber, request.PageSize, cancellationToken);

    public Task<DemoRequestDetail> Handle(GetDemoRequest request, CancellationToken cancellationToken) => repository.GetAsync(request.Id, cancellationToken);

    public Task<DemoRequestDetail> Handle(UpdateDemoRequestStatus request, CancellationToken cancellationToken) =>
        repository.UpdateStatusAsync(request.Id, request.Status.ToUpperInvariant(), request.User, cancellationToken);

    private static DemoRequestInput Normalize(DemoRequestInput x) => new(
        Clean(x.Name)!, NormalizeMobile(x.Mobile), Clean(x.Email)?.ToLowerInvariant(), Clean(x.BusinessName), Clean(x.City),
        Clean(x.BusinessType), Clean(x.Message), Clean(x.UtmSource), Clean(x.UtmMedium), Clean(x.UtmCampaign), Clean(x.UtmContent),
        Clean(x.LandingPage), Clean(x.Referrer), null);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return new string(value.Trim().Where(c => !char.IsControl(c) || c is '\r' or '\n' or '\t').ToArray());
    }

    private static string NormalizeMobile(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return value.TrimStart().StartsWith('+') ? "+" + digits : digits;
    }

    private static string ResolveSource(string? utmSource, string? referrer)
    {
        if (!string.IsNullOrWhiteSpace(utmSource)) return utmSource;
        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var uri)) return "Direct";
        return uri.Host.Equals("khatadhari.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".khatadhari.com", StringComparison.OrdinalIgnoreCase)
            ? "Website"
            : "Referral";
    }
}
