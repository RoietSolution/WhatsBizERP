namespace WhatsBiz.Application.Common.Interfaces;

public enum EntityCodeKind
{
    Customer,
    Supplier,
    Product
}

public interface IEntityCodeService
{
    Task<string> NextAsync(EntityCodeKind kind, CancellationToken cancellationToken = default);
    Task<string> PreviewAsync(EntityCodeKind kind, CancellationToken cancellationToken = default);
}
