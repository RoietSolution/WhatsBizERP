using WhatsBiz.Application.Common.Interfaces;

namespace WhatsBiz.Infrastructure.Outbound;

public sealed class DevelopmentOutboundBlockingHandler(IOutboundSafetyPolicy policy) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        policy.EnsureExternalDeliveryAllowed("HTTP delivery");
        return base.SendAsync(request, cancellationToken);
    }
}
