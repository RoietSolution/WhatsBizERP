using WhatsBiz.Domain.Customers;
namespace WhatsBiz.Application.Common.Interfaces;
public interface ICustomerGroupRepository
{
    Task<IReadOnlyCollection<CustomerGroup>> List(CancellationToken token);
    Task<bool> Exists(string code, string name, CancellationToken token);
    void Add(CustomerGroup group);
    Task Save(CancellationToken token);
}
