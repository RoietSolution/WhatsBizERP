namespace WhatsBiz.Api.Configuration;

public sealed class LocalFileLoggingOptions
{
    public const string SectionName = "LocalFileLogging";

    public bool Enabled { get; init; } = true;
    public string Path { get; init; } = "Logs/khatadhari-api-.log";
    public int RetainedFileCountLimit { get; init; } = 30;
    public long FileSizeLimitBytes { get; init; } = 100 * 1024 * 1024;
}
