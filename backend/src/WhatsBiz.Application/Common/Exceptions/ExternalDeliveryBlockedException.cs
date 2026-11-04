namespace WhatsBiz.Application.Common.Exceptions;

public sealed class ExternalDeliveryBlockedException(string integration)
    : HttpRequestException($"Outbound integration '{integration}' is blocked in Development.");
