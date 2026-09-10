#pragma warning disable CA1725
using Microsoft.EntityFrameworkCore;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Suppliers;

namespace WhatsBiz.Infrastructure.Persistence;

public sealed class SupplierRepository(ApplicationDbContext db, ICurrentUserService currentUser) : ISupplierRepository
{
    private Guid Tenant => currentUser.TenantId ?? throw new UnauthorizedAccessException("Tenant context is required.");
    private IQueryable<Supplier> Scoped(bool tracking = true) =>
        (tracking ? db.Suppliers : db.Suppliers.AsNoTracking())
            .Where(x => !x.IsDeleted && EF.Property<Guid?>(x, "TenantId") == Tenant);

    public async Task<(IReadOnlyCollection<Supplier>, int)> SearchAsync(string? search, bool? active, string sort, bool desc, int page, int size, CancellationToken token)
    {
        var q = Scoped(false);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.SupplierCode.Contains(search) || x.SupplierName.Contains(search) || (x.GSTIN != null && x.GSTIN.Contains(search)) || (x.Mobile != null && x.Mobile.Contains(search)));
        if (active.HasValue) q = q.Where(x => x.IsActive == active);
        q = (sort.ToLowerInvariant(), desc) switch { ("suppliercode", false) => q.OrderBy(x => x.SupplierCode), ("suppliercode", true) => q.OrderByDescending(x => x.SupplierCode), ("creditlimit", false) => q.OrderBy(x => x.CreditLimit), ("creditlimit", true) => q.OrderByDescending(x => x.CreditLimit), (_, true) => q.OrderByDescending(x => x.SupplierName), _ => q.OrderBy(x => x.SupplierName) };
        var count = await q.CountAsync(token);
        return (await q.Skip((page - 1) * size).Take(size).ToArrayAsync(token), count);
    }

    public Task<Supplier?> GetAsync(Guid id, bool tracking, CancellationToken token)
    {
        var q = Scoped(tracking).Include(x => x.PaymentTerm).Include(x => x.Contacts).Include(x => x.Addresses).Include(x => x.BankAccounts).Include(x => x.Documents);
        return q.SingleOrDefaultAsync(x => x.SupplierId == id, token);
    }

    public Task<bool> DuplicateAsync(string code, string? gstin, string name, Guid? exclude, CancellationToken token) => Scoped(false).AnyAsync(x => (!exclude.HasValue || x.SupplierId != exclude) && (x.SupplierCode == code.Trim() || x.SupplierName == name.Trim() || (!string.IsNullOrWhiteSpace(gstin) && x.GSTIN == gstin.Trim())), token);
    public async Task<IReadOnlyCollection<SupplierPaymentTerm>> PaymentTermsAsync(CancellationToken token) => await db.SupplierPaymentTerms.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DueDays).ToArrayAsync(token);
    public void Add(Supplier supplier) => db.Suppliers.Add(supplier);
    public void RemoveChildren(Supplier supplier) { db.SupplierContacts.RemoveRange(supplier.Contacts); db.SupplierAddresses.RemoveRange(supplier.Addresses); db.SupplierBankAccounts.RemoveRange(supplier.BankAccounts); supplier.Contacts = []; supplier.Addresses = []; supplier.BankAccounts = []; }
    public Task<SupplierDocument?> DocumentAsync(Guid supplierId, Guid documentId, bool tracking, CancellationToken token) { var q = db.SupplierDocuments.Where(x => Scoped(false).Any(s => s.SupplierId == x.SupplierId) && x.SupplierId == supplierId && x.DocumentId == documentId && !x.IsDeleted); if (!tracking) q = q.AsNoTracking(); return q.SingleOrDefaultAsync(token); }
    public void Add(SupplierDocument document) => db.SupplierDocuments.Add(document);
    public Task SaveAsync(CancellationToken token) => db.SaveChangesAsync(token);
}
