using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Customers;
namespace WhatsBiz.Infrastructure.Persistence;
public sealed class CustomerGroupRepository(ApplicationDbContext db, ICurrentUserService currentUser) : ICustomerGroupRepository
{
    private IQueryable<CustomerGroup> TenantGroups => currentUser.TenantId is Guid tenant ? db.CustomerGroups.Where(x => x.TenantId == tenant) : db.CustomerGroups.Where(_ => false);
    public async Task<IReadOnlyCollection<CustomerGroup>> List(CancellationToken token) => await TenantGroups.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.GroupName).ToArrayAsync(token);
    public Task<bool> Exists(string code, string name, CancellationToken token) => TenantGroups.AnyAsync(x => x.GroupCode == code.Trim() || x.GroupName == name.Trim(), token);
    public void Add(CustomerGroup group) { group.TenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required to create a customer group."); db.CustomerGroups.Add(group); }
    public Task Save(CancellationToken token) => db.SaveChangesAsync(token);
}
