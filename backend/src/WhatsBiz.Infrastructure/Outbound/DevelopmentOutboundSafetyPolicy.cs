using Microsoft.Extensions.Hosting;
using WhatsBiz.Application.Common.Exceptions;
using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Outbound;

public sealed class DevelopmentOutboundSafetyPolicy(IHostEnvironment environment) : IOutboundSafetyPolicy
{
    public bool AllowsExternalDelivery => !environment.IsDevelopment();

    public void EnsureExternalDeliveryAllowed(string integration)
    {
        if (!AllowsExternalDelivery) throw new ExternalDeliveryBlockedException(integration);
    }
}
