namespace WhatsBiz.Domain.Common;

public abstract class BaseEntity : IAuditableEntity
{
    private readonly List<DomainEvent> _domainEvents = [];
    public Guid Id { get; protected init; } = Guid.NewGuid();
    public DateTimeOffset CreatedOnUtc { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTimeOffset? LastModifiedOnUtc { get; private set; }
    public string? LastModifiedBy { get; private set; }
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
    public void SetAuditValues(DateTimeOffset timestamp, string? actor, bool isNew)
    {
        if (isNew) { CreatedOnUtc = timestamp; CreatedBy = actor; }
        else { LastModifiedOnUtc = timestamp; LastModifiedBy = actor; }
    }
}
