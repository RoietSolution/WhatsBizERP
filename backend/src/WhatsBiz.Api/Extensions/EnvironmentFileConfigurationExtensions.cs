using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace WhatsBiz.Api.Extensions;

public static partial class EnvironmentFileConfigurationExtensions
{
    public const string EnvironmentFileVariable = "KHATADHARI_ENV_FILE";
    public const string LinuxDefaultPath = "/etc/khatadhari/khatadhari.env";

    public static IConfigurationBuilder AddKhataDhariEnvironmentFile(this IConfigurationBuilder configuration, string? path = null)
    {
        path ??= Environment.GetEnvironmentVariable(EnvironmentFileVariable);
        if (string.IsNullOrWhiteSpace(path) && OperatingSystem.IsLinux()) path = LinuxDefaultPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return configuration;

        var values = Parse(File.ReadLines(path), path);
        configuration.AddInMemoryCollection(values);

        // Real process variables remain authoritative when systemd, Docker or a
        // secret manager already injected them.
        configuration.AddEnvironmentVariables();
        return configuration;
    }

    public static IReadOnlyDictionary<string, string?> Parse(IEnumerable<string> lines, string sourceName = "environment file")
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

            var separator = line.IndexOf('=');
            if (separator <= 0) throw new FormatException($"Invalid entry in {sourceName} at line {lineNumber}.");
            var key = line[..separator].Trim();
            if (!EnvironmentVariableName().IsMatch(key))
                throw new FormatException($"Invalid variable name in {sourceName} at line {lineNumber}.");

            values[key.Replace("__", ":", StringComparison.Ordinal)] = Unquote(line[(separator + 1)..].Trim());
        }
        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != value[^1] || value[0] is not ('\'' or '"')) return value;
        var content = value[1..^1];
        return value[0] == '\'' ? content : content.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvironmentVariableName();
}
