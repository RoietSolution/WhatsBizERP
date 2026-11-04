namespace WhatsBiz.Application.Common.Interfaces;

public interface IOutboundSafetyPolicy
{
    bool AllowsExternalDelivery { get; }
    void EnsureExternalDeliveryAllowed(string integration);
}
