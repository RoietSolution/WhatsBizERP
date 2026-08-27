using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.DemoRequests;

public sealed class DemoRequestCaptchaVerifier(HttpClient client, IOptions<DemoRequestOptions> options) : IDemoRequestCaptchaVerifier
{
    private readonly CaptchaOptions settings = options.Value.Captcha;

    public async Task<bool> VerifyAsync(string? tokenValue, string? ipAddress, CancellationToken token)
    {
        if (!settings.Enabled) return true;
        if (!settings.Provider.Equals("Turnstile", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(settings.SecretKey) || string.IsNullOrWhiteSpace(tokenValue)) return false;
        using var response = await client.PostAsync(settings.VerificationUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = settings.SecretKey,
            ["response"] = tokenValue,
            ["remoteip"] = ipAddress ?? string.Empty
        }), token);
        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(cancellationToken: token);
        return result?.Success == true;
    }

    private sealed record TurnstileResponse([property: JsonPropertyName("success")] bool Success);
}
