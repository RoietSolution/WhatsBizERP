namespace WhatsBiz.Application.Common.Features;

public static class FeatureKeys
{
    public const string WhatsAppCommerce = "WHATSAPP_COMMERCE";
    public const string AdvancedWarehouse = "ADVANCED_WAREHOUSE";
    public const string AiAssistant = "AI_ASSISTANT";
    public const string Integrations = "INTEGRATIONS";

    public static readonly IReadOnlyCollection<string> All =
        [WhatsAppCommerce, AdvancedWarehouse, AiAssistant, Integrations];
}
