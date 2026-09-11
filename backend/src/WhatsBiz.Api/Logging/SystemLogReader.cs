using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WhatsBiz.Api.Configuration;

namespace WhatsBiz.Api.Logging;

public sealed record SystemLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Source,
    string? TraceId,
    string Message,
    string? RequestPath,
    string? HttpMethod,
    int? StatusCode,
    string? TenantId,
    string? UserId,
    string? ExceptionType);

public sealed record PagedSystemLogs(
    IReadOnlyCollection<SystemLogEntry> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed partial class SystemLogReader
{
    private const int MaximumMessageLength = 12000;
    private readonly string directory;
    private readonly string searchPattern;
    private readonly string filePrefix;
    private readonly bool enabled;

    public SystemLogReader(IOptions<LocalFileLoggingOptions> options, IWebHostEnvironment environment)
    {
        var settings = options.Value;
        enabled = settings.Enabled;
        var configuredPath = Path.IsPathRooted(settings.Path)
            ? Path.GetFullPath(settings.Path)
            : Path.GetFullPath(settings.Path, environment.ContentRootPath);
        directory = Path.GetDirectoryName(configuredPath) ?? environment.ContentRootPath;
        filePrefix = Path.GetFileNameWithoutExtension(configuredPath);
        searchPattern = $"{filePrefix}*{Path.GetExtension(configuredPath)}*";
    }

    public async Task<PagedSystemLogs> SearchAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? level,
        string? requestPath,
        int? statusCode,
        string? traceId,
        string? search,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!enabled || !Directory.Exists(directory)) return new([], 0, pageNumber, pageSize);

        var matches = new List<SystemLogEntry>();
        foreach (var file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CouldContainDate(file, from, to)) continue;
            await foreach (var entry in ReadAsync(file, cancellationToken))
            {
                if (entry.Timestamp < from || entry.Timestamp >= to) continue;
                if (!Matches(entry.Level, level) || !Contains(entry.RequestPath, requestPath) ||
                    (statusCode.HasValue && entry.StatusCode != statusCode) || !Contains(entry.TraceId, traceId) ||
                    (!string.IsNullOrWhiteSpace(search) && !Searchable(entry).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
                matches.Add(entry);
            }
        }

        var ordered = matches.OrderByDescending(x => x.Timestamp).ToArray();
        return new(
            ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArray(),
            ordered.Length,
            pageNumber,
            pageSize);
    }

    private static async IAsyncEnumerable<SystemLogEntry> ReadAsync(
        string file,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        ParsedLog? current = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var match = HeaderPattern().Match(line);
            if (match.Success)
            {
                if (current is not null) yield return current.ToEntry();
                current = ParsedLog.Create(match);
            }
            else if (current is not null && current.Message.Length < MaximumMessageLength)
            {
                current.Message.AppendLine().Append(line.AsSpan(0, Math.Min(line.Length, MaximumMessageLength - current.Message.Length)));
            }
        }
        if (current is not null) yield return current.ToEntry();
    }

    private static bool Matches(string actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected) || actual.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Contains(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected) || (actual?.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);
    private static string Searchable(SystemLogEntry entry) =>
        $"{entry.Message} {entry.Source} {entry.RequestPath} {entry.HttpMethod} {entry.TraceId} {entry.TenantId} {entry.UserId} {entry.ExceptionType}";

    private bool CouldContainDate(string file, DateTimeOffset from, DateTimeOffset to)
    {
        var name = Path.GetFileName(file);
        if (!name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase) || name.Length < filePrefix.Length + 8) return true;
        var dateText = name.AsSpan(filePrefix.Length, 8);
        if (!DateOnly.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate)) return true;
        return fileDate >= DateOnly.FromDateTime(from.DateTime) && fileDate < DateOnly.FromDateTime(to.DateTime);
    }

    private sealed class ParsedLog
    {
        private ParsedLog(DateTimeOffset timestamp, string level, string source, string? traceId, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Source = source;
            TraceId = NullIfEmpty(traceId);
            Message = new StringBuilder(message);
        }

        public DateTimeOffset Timestamp { get; }
        public string Level { get; }
        public string Source { get; }
        public string? TraceId { get; private set; }
        public StringBuilder Message { get; }

        public static ParsedLog Create(Match match)
        {
            var timestamp = DateTimeOffset.ParseExact(
                match.Groups["timestamp"].Value,
                "yyyy-MM-dd HH:mm:ss.fff zzz",
                CultureInfo.InvariantCulture);
            return new(timestamp, LevelName(match.Groups["level"].Value), match.Groups["source"].Value,
                match.Groups["trace"].Value, match.Groups["message"].Value);
        }

        public SystemLogEntry ToEntry()
        {
            var text = Message.ToString();
            var failure = ApiFailurePattern().Match(text);
            var request = failure.Success ? failure : RequestPattern().Match(text);
            var pathMatch = request.Success ? request : PathMessagePattern().Match(text);
            var path = pathMatch.Success ? NullIfEmpty(pathMatch.Groups["path"].Value) : null;
            var method = request.Success ? NullIfEmpty(request.Groups["method"].Value) : null;
            int? status = request.Success && int.TryParse(request.Groups["status"].Value, out var parsedStatus) ? parsedStatus : null;
            if (failure.Success) TraceId = NullIfEmpty(failure.Groups["trace"].Value) ?? TraceId;
            return new(Timestamp, Level, Source, TraceId, text, path, method, status,
                failure.Success ? NullValue(failure.Groups["tenant"].Value) : null,
                failure.Success ? NullValue(failure.Groups["user"].Value) : null,
                failure.Success ? NullValue(failure.Groups["exception"].Value) : null);
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string? NullValue(string? value) => string.IsNullOrWhiteSpace(value) || value is "(null)" or "null" ? null : value;
    private static string LevelName(string level) => level switch { "ERR" => "Error", "WRN" => "Warning", "FTL" => "Fatal", "DBG" => "Debug", "VRB" => "Verbose", _ => "Information" };

    [GeneratedRegex(@"^\[(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) (?<level>[A-Z]{3})\] \[(?<source>[^\]]*)\] \[Trace:(?<trace>[^\]]*)\] (?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();
    [GeneratedRegex(@"API failure (?<method>[A-Z]+) (?<path>\S+) (?<status>\d+) TraceId=(?<trace>\S+) TenantId=(?<tenant>\S*) UserId=(?<user>\S*) DurationMs=\S+ ExceptionType=(?<exception>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ApiFailurePattern();
    [GeneratedRegex(@"HTTP (?<method>[A-Z]+) (?<path>\S+) responded (?<status>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex RequestPattern();
    [GeneratedRegex(@"(?:Unhandled exception|Validation failed) for (?<path>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex PathMessagePattern();
}
