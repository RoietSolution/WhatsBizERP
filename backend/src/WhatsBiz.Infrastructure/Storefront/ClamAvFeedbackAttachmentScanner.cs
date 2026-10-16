using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsBiz.Application.Features.Storefront;

namespace WhatsBiz.Infrastructure.Storefront;

public sealed class ClamAvFeedbackAttachmentScanner(IConfiguration configuration, ILogger<ClamAvFeedbackAttachmentScanner> logger) : IStorefrontFeedbackAttachmentScanner, IDisposable
{
    private readonly SemaphoreSlim scanGate = new(Math.Clamp(configuration.GetValue("FeedbackAttachments:Scanner:MaxConcurrentScans", 1), 1, 4));

    public async Task<StorefrontAttachmentScanResult> ScanAsync(byte[] content, CancellationToken token)
    {
        if (!configuration.GetValue<bool>("FeedbackAttachments:Scanner:Enabled")) return StorefrontAttachmentScanResult.Pending;
        var command = configuration["FeedbackAttachments:Scanner:Command"];
        if (string.IsNullOrWhiteSpace(command)) return StorefrontAttachmentScanResult.Failed;
        var temporary = Path.Combine(Path.GetTempPath(), $"whatsbiz-feedback-{Guid.NewGuid():N}.bin");
        Process? process = null;
        var acquired = false;
        try
        {
            await scanGate.WaitAsync(token); acquired = true;
            await File.WriteAllBytesAsync(temporary, content, token);
            process = new Process { StartInfo = new ProcessStartInfo { FileName = command, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("--no-summary"); process.StartInfo.ArgumentList.Add(temporary);
            if (!process.Start()) return StorefrontAttachmentScanResult.Failed;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(configuration.GetValue("FeedbackAttachments:Scanner:TimeoutSeconds", 30)));
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode switch { 0 => StorefrontAttachmentScanResult.Clean, 1 => StorefrontAttachmentScanResult.Infected, _ => StorefrontAttachmentScanResult.Failed };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Kill(process); return StorefrontAttachmentScanResult.Failed; }
        catch (Exception exception) { ScannerLogs.ExecutionFailed(logger, exception); return StorefrontAttachmentScanResult.Failed; }
        finally { Kill(process); process?.Dispose(); if (acquired) scanGate.Release(); try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static void Kill(Process? process)
    { try { if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); process.WaitForExit(1000); } } catch { } }
    public void Dispose() => scanGate.Dispose();
}

internal static partial class ScannerLogs
{
    [LoggerMessage(3520, LogLevel.Error, "Feedback attachment scanner execution failed.")]
    public static partial void ExecutionFailed(ILogger logger, Exception exception);
}
