using WhatsBiz.Domain.Customers;
namespace WhatsBiz.Application.Common.Interfaces;
public interface ICustomerGroupRepository
{
    Task<IReadOnlyCollection<CustomerGroup>> List(CancellationToken token);
    Task<bool> Exists(string code, string name, CancellationToken token);
    Task<CustomerGroup?> Find(Guid id, bool tracking, CancellationToken token);
    Task<bool> IsReferenced(Guid id, CancellationToken token);
    void Add(CustomerGroup group);
    void Remove(CustomerGroup group);
    Task Save(CancellationToken token);
}
