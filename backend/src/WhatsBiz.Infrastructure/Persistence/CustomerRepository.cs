#pragma warning disable CA1725
using Microsoft.EntityFrameworkCore;
using BusinessRuleException = WhatsBiz.Application.Common.Exceptions.BusinessRuleException;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Application.Features.Customers;
using WhatsBiz.Domain.Customers;
namespace WhatsBiz.Infrastructure.Persistence;
public sealed class CustomerRepository(ApplicationDbContext db, ICurrentUserService currentUser) : ICustomerRepository
{
    private IQueryable<Customer> TenantCustomers => currentUser.TenantId is Guid tenant ? db.Customers.Where(x => x.TenantId == tenant) : db.Customers.Where(_ => false);
    public Task<(IReadOnlyCollection<Customer>, int)> Search(string? q, bool? active, string sort, bool desc, int page, int size, CancellationToken t) => Search(q, active, sort, desc, page, size, null, t);
    public async Task<(IReadOnlyCollection<Customer>, int)> Search(string? q, bool? active, string sort, bool desc, int page, int size, Guid? groupId, CancellationToken t) { var x = TenantCustomers.AsNoTracking().Include(c => c.CustomerGroup).Where(c => !c.IsDeleted); if (!string.IsNullOrWhiteSpace(q)) x = x.Where(c => c.CustomerCode.Contains(q) || c.CustomerName.Contains(q) || (c.GSTIN != null && c.GSTIN.Contains(q)) || (c.Mobile != null && c.Mobile.Contains(q)) || (c.Email != null && c.Email.Contains(q))); if (active.HasValue) x = x.Where(c => c.IsActive == active); if (groupId.HasValue) x = x.Where(c => c.CustomerGroupId == groupId); x = desc ? x.OrderByDescending(c => c.CustomerName) : x.OrderBy(c => c.CustomerName); var n = await x.CountAsync(t); return (await x.Skip(Math.Max(page - 1, 0) * Math.Clamp(size, 1, 200)).Take(Math.Clamp(size, 1, 200)).ToArrayAsync(t), n); }
    public Task<Customer?> GetById(Guid id, bool tracking, CancellationToken t) { var q = TenantCustomers.Include(x => x.PaymentTerm).Include(x => x.CustomerGroup).Include(x => x.Contacts).Include(x => x.Addresses).Include(x => x.BankAccounts).Include(x => x.Documents).Where(x => !x.IsDeleted); if (!tracking) q = q.AsNoTracking(); return q.SingleOrDefaultAsync(x => x.CustomerId == id, t); }
    public Task<bool> BelongsToCurrentTenant(Guid id, CancellationToken t) => TenantCustomers.AnyAsync(x => x.CustomerId == id && !x.IsDeleted, t);
    public Task<bool> Duplicate(string code, string? gst, string name, string? mobile, Guid? exclude, CancellationToken t)
    {
        var normalizedMobile = NormalizeMobile(mobile);
        return TenantCustomers.AnyAsync(x => !x.IsDeleted && (!exclude.HasValue || x.CustomerId != exclude) &&
            (x.CustomerCode == code.Trim() || x.CustomerName == name.Trim() ||
             (!string.IsNullOrWhiteSpace(gst) && x.GSTIN == gst.Trim()) ||
             (normalizedMobile != null && EF.Property<string?>(x, "MobileNormalized") == normalizedMobile)), t);
    }
    public async Task<IReadOnlyCollection<CustomerPaymentTerm>> Terms(CancellationToken t) => await db.CustomerPaymentTerms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DueDays).ToArrayAsync(t);
    public void Add(Customer c) { c.TenantId = currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required to create a customer."); db.Customers.Add(c); }
    public void RemoveChildren(Customer c) { db.CustomerContacts.RemoveRange(c.Contacts); db.CustomerAddresses.RemoveRange(c.Addresses); db.CustomerBankAccounts.RemoveRange(c.BankAccounts); c.Contacts = []; c.Addresses = []; c.BankAccounts = []; }
    public void SyncChildren(Customer c, CustomerInput input)
    {
        foreach (var incoming in input.Addresses)
        {
            var current = incoming.AddressId.HasValue ? c.Addresses.FirstOrDefault(x => x.AddressId == incoming.AddressId.Value) : null;
            if (current is null)
            {
                if (incoming.AddressId.HasValue)
                    throw new BusinessRuleException("The selected address does not belong to this customer.");
                var created = new CustomerAddress { AddressId = Guid.NewGuid(), CustomerId = c.CustomerId, AddressType = incoming.AddressType, AddressLine1 = incoming.AddressLine1, AddressLine2 = incoming.AddressLine2, City = incoming.City, District = incoming.District, State = incoming.State, Country = incoming.Country, PostalCode = incoming.PostalCode };
                c.Addresses.Add(created);
                db.Entry(created).State = EntityState.Added;
                continue;
            }
            current.AddressType = incoming.AddressType; current.AddressLine1 = incoming.AddressLine1; current.AddressLine2 = incoming.AddressLine2; current.City = incoming.City; current.District = incoming.District; current.State = incoming.State; current.Country = incoming.Country; current.PostalCode = incoming.PostalCode;
        }
        foreach (var incoming in input.Contacts)
        {
            var current = incoming.ContactId.HasValue ? c.Contacts.FirstOrDefault(x => x.ContactId == incoming.ContactId.Value) : null;
            if (current is null)
            {
                if (incoming.ContactId.HasValue)
                    throw new BusinessRuleException("The selected contact does not belong to this customer.");
                c.Contacts.Add(new CustomerContact { ContactId = Guid.NewGuid(), CustomerId = c.CustomerId, ContactPerson = incoming.ContactPerson, Designation = incoming.Designation, Department = incoming.Department, Mobile = incoming.Mobile, Email = incoming.Email, IsPrimary = incoming.IsPrimary });
                continue;
            }
            current.ContactPerson = incoming.ContactPerson; current.Designation = incoming.Designation; current.Department = incoming.Department; current.Mobile = incoming.Mobile; current.Email = incoming.Email; current.IsPrimary = incoming.IsPrimary;
        }
        foreach (var incoming in input.BankAccounts)
        {
            var current = incoming.BankAccountId.HasValue ? c.BankAccounts.FirstOrDefault(x => x.BankAccountId == incoming.BankAccountId.Value) : null;
            if (current is null)
            {
                if (incoming.BankAccountId.HasValue)
                    throw new BusinessRuleException("The selected bank account does not belong to this customer.");
                c.BankAccounts.Add(new CustomerBankAccount { BankAccountId = Guid.NewGuid(), CustomerId = c.CustomerId, BankName = incoming.BankName, Branch = incoming.Branch, AccountNumber = incoming.AccountNumber, IFSCCode = incoming.IFSCCode, UPIId = incoming.UPIId });
                continue;
            }
            current.BankName = incoming.BankName; current.Branch = incoming.Branch; current.AccountNumber = incoming.AccountNumber; current.IFSCCode = incoming.IFSCCode; current.UPIId = incoming.UPIId;
        }
    }
    public Task<CustomerDocument?> Document(Guid cid, Guid did, bool tracking, CancellationToken t) { var q = from d in db.CustomerDocuments join c in TenantCustomers on d.CustomerId equals c.CustomerId where d.CustomerId == cid && d.DocumentId == did && !d.IsDeleted select d; if (!tracking) q = q.AsNoTracking(); return q.SingleOrDefaultAsync(t); }
    public void Add(CustomerDocument d) { if (currentUser.TenantId is null) throw new UnauthorizedAccessException("A tenant context is required to add customer documents."); db.CustomerDocuments.Add(d); }
    public Task Save(CancellationToken t)
    {
        db.ChangeTracker.DetectChanges();
        foreach (var entry in db.ChangeTracker.Entries<Customer>().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.State == EntityState.Added || entry.Property(nameof(Customer.Mobile)).IsModified)
                entry.Property<string?>("MobileNormalized").CurrentValue = NormalizeMobile(entry.Entity.Mobile);
        }
        return db.SaveChangesAsync(t);
    }

    private static string? NormalizeMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return null;
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("91", StringComparison.Ordinal) && digits.Length == 12) digits = digits[2..];
        return digits.Length == 10 && digits[0] is >= '6' and <= '9' ? digits : null;
    }
}
