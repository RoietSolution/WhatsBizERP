namespace WhatsBiz.Domain.Common;

public abstract record DomainEvent(DateTimeOffset OccurredOnUtc);
