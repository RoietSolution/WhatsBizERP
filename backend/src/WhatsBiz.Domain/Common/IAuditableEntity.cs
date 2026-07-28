namespace WhatsBiz.Domain.Common;

public interface IAuditableEntity
{
    DateTimeOffset CreatedOnUtc { get; }
    string? CreatedBy { get; }
    DateTimeOffset? LastModifiedOnUtc { get; }
    string? LastModifiedBy { get; }
}
