#pragma warning disable CA1725
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Customers;
namespace WhatsBiz.Infrastructure.Persistence;
public sealed class CustomerRepository(ApplicationDbContext db, ICurrentUserService currentUser) : ICustomerRepository
{
    private IQueryable<Customer> TenantCustomers => currentUser.TenantId is Guid tenant ? db.Customers.Where(x => x.TenantId == tenant) : db.Customers.Where(_ => false);
    public Task<(IReadOnlyCollection<Customer>, int)> Search(string? q, bool? active, string sort, bool desc, int page, int size, CancellationToken t) => Search(q, active, sort, desc, page, size, null, t);
    public async Task<(IReadOnlyCollection<Customer>, int)> Search(string? q, bool? active, string sort, bool desc, int page, int size, Guid? groupId, CancellationToken t) { var x = TenantCustomers.AsNoTracking().Include(c => c.CustomerGroup).Where(c => !c.IsDeleted); if (!string.IsNullOrWhiteSpace(q)) x = x.Where(c => c.CustomerCode.Contains(q) || c.CustomerName.Contains(q) || (c.GSTIN != null && c.GSTIN.Contains(q)) || (c.Mobile != null && c.Mobile.Contains(q)) || (c.Email != null && c.Email.Contains(q))); if (active.HasValue) x = x.Where(c => c.IsActive == active); if (groupId.HasValue) x = x.Where(c => c.CustomerGroupId == groupId); x = desc ? x.OrderByDescending(c => c.CustomerName) : x.OrderBy(c => c.CustomerName); var n = await x.CountAsync(t); return (await x.Skip(Math.Max(page - 1, 0) * Math.Clamp(size, 1, 200)).Take(Math.Clamp(size, 1, 200)).ToArrayAsync(t), n); }
    public Task<Customer?> GetById(Guid id, bool tracking, CancellationToken t) { var q = TenantCustomers.Include(x => x.PaymentTerm).Include(x => x.CustomerGroup).Include(x => x.Contacts).Include(x => x.Addresses).Include(x => x.BankAccounts).Include(x => x.Documents).Where(x => !x.IsDeleted); if (!tracking) q = q.AsNoTracking(); return q.SingleOrDefaultAsync(x => x.CustomerId == id, t); }
    public Task<bool> BelongsToCurrentTenant(Guid id, CancellationToken t) => TenantCustomers.AnyAsync(x => x.CustomerId == id && !x.IsDeleted, t);
    public Task<bool> Duplicate(string code, string? gst, string name, Guid? exclude, CancellationToken t) => TenantCustomers.AnyAsync(x => !x.IsDeleted && (!exclude.HasValue || x.CustomerId != exclude) && (x.CustomerCode == code.Trim() || x.CustomerName == name.Trim() || (!string.IsNullOrWhiteSpace(gst) && x.GSTIN == gst.Trim())), t);
    public async Task<IReadOnlyCollection<CustomerPaymentTerm>> Terms(CancellationToken t) => await db.CustomerPaymentTerms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DueDays).ToArrayAsync(t);
    public void Add(Customer c) { c.TenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required to create a customer."); db.Customers.Add(c); }
    public void RemoveChildren(Customer c) { db.CustomerContacts.RemoveRange(c.Contacts); db.CustomerAddresses.RemoveRange(c.Addresses); db.CustomerBankAccounts.RemoveRange(c.BankAccounts); c.Contacts = []; c.Addresses = []; c.BankAccounts = []; }
    public Task<CustomerDocument?> Document(Guid cid, Guid did, bool tracking, CancellationToken t) { var q = from d in db.CustomerDocuments join c in TenantCustomers on d.CustomerId equals c.CustomerId where d.CustomerId == cid && d.DocumentId == did && !d.IsDeleted select d; if (!tracking) q = q.AsNoTracking(); return q.SingleOrDefaultAsync(t); }
    public void Add(CustomerDocument d) { if (currentUser.TenantId is null) throw new UnauthorizedAccessException("A tenant context is required to add customer documents."); db.CustomerDocuments.Add(d); }
    public Task Save(CancellationToken t) => db.SaveChangesAsync(t);
}
