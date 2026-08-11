namespace WhatsBiz.Infrastructure.Notifications;

public sealed class FeatureOptions
{
    public const string SectionName = "Features";
    public WhatsAppFeatureOptions WhatsApp { get; set; } = new();
    public SmsFeatureOptions Sms { get; set; } = new();
}

public sealed class WhatsAppFeatureOptions
{
    public bool Enabled { get; set; }
}

public sealed class SmsFeatureOptions
{
    public bool Enabled { get; set; }
}

public sealed record ClientFeatureState(bool WhatsAppEnabled, bool SmsEnabled);
