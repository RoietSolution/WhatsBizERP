namespace WhatsBiz.Application.Common.Features;

public sealed record TenantEnrollmentResult(
    Guid TenantId,
    string TenantKey,
    string TenantName,
    string PlanKey,
    string AdministratorUsername,
    string AdministratorEmail,
    int ConfiguredFeatureCount);
