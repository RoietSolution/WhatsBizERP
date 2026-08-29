#pragma warning disable CA1725,CA1861
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using WhatsBiz.Application.Common.Interfaces;
using WhatsBiz.Domain.Customers;
using WhatsBiz.Domain.POS;
using WhatsBiz.Domain.Products;
namespace WhatsBiz.Infrastructure.Persistence;
public sealed class POSRepository(ApplicationDbContext db, ICurrentUserService currentUser) : IPOSRepository
{
    private IQueryable<Product> TenantProducts => currentUser.TenantId is Guid tenant ? db.Products.Where(x => x.TenantId == tenant) : db.Products.Where(_ => false);
    private IQueryable<Customer> TenantCustomers => currentUser.TenantId is Guid tenant ? db.Customers.Where(x => x.TenantId == tenant) : db.Customers.Where(_ => false);
    public async Task<IReadOnlyDictionary<Guid,string>> SourceChannels(IReadOnlyCollection<Guid> invoiceIds, CancellationToken token) { if (invoiceIds.Count == 0) return new Dictionary<Guid,string>(); var connection=(SqlConnection)db.Database.GetDbConnection(); var opened=connection.State!=System.Data.ConnectionState.Open; if(opened)await connection.OpenAsync(token); try { await using var command=connection.CreateCommand(); var names=invoiceIds.Select((id,index)=>{var name=$"@p{index}";command.Parameters.AddWithValue(name,id);return name;}); command.CommandText=$"SELECT InvoiceId,SourceChannel FROM integration.WhatsAppCommerceOrders WHERE InvoiceId IN ({string.Join(',',names)});"; var result=new Dictionary<Guid,string>(); await using var reader=await command.ExecuteReaderAsync(token); while(await reader.ReadAsync(token))result[reader.GetGuid(0)]=reader.GetString(1); return result; } finally { if(opened)await connection.CloseAsync(); } }
    public async Task<POSCoinSummary> CoinSummary(Guid invoiceId, CancellationToken token) { var connection=(SqlConnection)db.Database.GetDbConnection(); var opened=connection.State!=System.Data.ConnectionState.Open; if(opened)await connection.OpenAsync(token); try { await using var command=connection.CreateCommand(); command.CommandText="SELECT EarnedCoins,RedeemedCoins,RedemptionDiscount FROM loyalty.OrderCoins WHERE OrderId=@order;"; command.Parameters.AddWithValue("@order",invoiceId); await using var reader=await command.ExecuteReaderAsync(token); return await reader.ReadAsync(token) ? new(reader.GetInt32(0),reader.GetInt32(1),reader.GetDecimal(2)) : new(0,0,0); } finally { if(opened)await connection.CloseAsync(); } }
    public async Task<IReadOnlyCollection<POSProductLookup>> Products(string? search, string? barcode, Guid? warehouseId, int size, CancellationToken token)
    {
        var exactBarcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode;
        var q = TenantProducts.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted);
        if (exactBarcode is not null)
            q = q.Where(x => x.Barcode == exactBarcode || db.ProductBarcodes.Any(b => b.TenantId == x.TenantId && b.ProductId == x.ProductId && b.Barcode == exactBarcode && b.IsActive && !b.IsDeleted));
        else if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(x => x.ProductCode.Contains(term) || x.ProductName.Contains(term) || (x.Barcode != null && x.Barcode.Contains(term)));
        }

        var products = await q.OrderBy(x => x.ProductName).Take(Math.Clamp(size, 1, 100)).ToArrayAsync(token);
        if (!warehouseId.HasValue || products.Length == 0)
            return products.Select(x => new POSProductLookup(x, exactBarcode ?? x.Barcode, null, false)).ToArray();

        var ids = products.Select(x => x.ProductId).ToArray();
        var balances = await db.InventoryBalances.AsNoTracking()
            .Where(x => ids.Contains(x.ProductId) && x.WarehouseId == warehouseId && x.ZoneId == null && x.BinId == null && x.BatchNo == null && x.SerialNo == null)
            .Select(x => new { x.ProductId, Available = x.QuantityOnHand - x.QuantityReserved })
            .ToArrayAsync(token);
        var available = balances.GroupBy(x => x.ProductId).ToDictionary(x => x.Key, x => x.First().Available);
        var negativeStockAllowed = await db.InventorySettings.AsNoTracking().Select(x => x.NegativeStockAllowed).FirstOrDefaultAsync(token);
        return products.Select(x => new POSProductLookup(x, exactBarcode ?? x.Barcode, available.GetValueOrDefault(x.ProductId), negativeStockAllowed)).ToArray();
    }
    public async Task<IReadOnlyCollection<Customer>> Customers(string? search, int size, CancellationToken token) { var q=TenantCustomers.AsNoTracking().Where(x=>x.IsActive&&!x.IsDeleted); if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.CustomerCode.Contains(search)||x.CustomerName.Contains(search)||(x.Mobile!=null&&x.Mobile.Contains(search))); return await q.OrderBy(x=>x.CustomerName).Take(Math.Clamp(size,1,100)).ToArrayAsync(token); }
    public Task<SalesInvoice?> Invoice(Guid id, CancellationToken token) => db.SalesInvoices.AsNoTracking().Include(x=>x.Customer).Where(x=>x.Customer == null || TenantCustomers.Any(c=>c.CustomerId==x.CustomerId)).Include(x=>x.Warehouse).Include(x=>x.Items).ThenInclude(x=>x.Product).Include(x=>x.Payments).ThenInclude(x=>x.PaymentMethod).Include(x=>x.Taxes).Include(x=>x.Discounts).SingleOrDefaultAsync(x=>x.InvoiceId==id,token);
    public async Task<(IReadOnlyCollection<SalesInvoice>,int)> Invoices(string? search,string? status,DateTimeOffset? from,DateTimeOffset? toDate,int page,int size,CancellationToken token) { var q=db.SalesInvoices.AsNoTracking().Include(x=>x.Customer).Where(x=>x.Customer==null||TenantCustomers.Any(c=>c.CustomerId==x.CustomerId)); if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.InvoiceNumber.Contains(search)||(x.Customer!=null&&x.Customer.CustomerName.Contains(search))); if(!string.IsNullOrWhiteSpace(status))q=q.Where(x=>x.Status==status); if(from.HasValue)q=q.Where(x=>x.InvoiceDate>=from); if(toDate.HasValue)q=q.Where(x=>x.InvoiceDate<=toDate); var count=await q.CountAsync(token); return (await q.OrderByDescending(x=>x.InvoiceDate).Skip((Math.Max(page,1)-1)*Math.Clamp(size,1,200)).Take(Math.Clamp(size,1,200)).ToArrayAsync(token),count); }
    public async Task<IReadOnlyCollection<PaymentMethod>> PaymentMethods(CancellationToken token)=>await db.PaymentMethods.AsNoTracking().Where(x=>x.IsActive).OrderBy(x=>x.DisplayOrder).ToArrayAsync(token);
    public async Task<POSTodaySummary> Today(CancellationToken token) { var today=DateTimeOffset.Now.Date; var tomorrow=today.AddDays(1); var invoices=db.SalesInvoices.AsNoTracking().Where(x=>x.InvoiceDate>=today&&x.InvoiceDate<tomorrow&&new[]{"COMPLETED","PARTIALLY_RETURNED","RETURNED"}.Contains(x.Status)); var gross=await invoices.SumAsync(x=>(decimal?)x.GrandTotal,token)??0; var collections=await invoices.SumAsync(x=>(decimal?)x.PaidAmount,token)??0; var count=await invoices.CountAsync(token); var payments=db.SalesPayments.AsNoTracking().Where(x=>x.PaymentDate>=today&&x.PaymentDate<tomorrow&&x.Status=="COMPLETED"); var cash=await payments.Where(x=>x.PaymentMethod.MethodCode=="CASH").SumAsync(x=>(decimal?)x.Amount,token)??0; var upi=await payments.Where(x=>x.PaymentMethod.MethodCode=="UPI").SumAsync(x=>(decimal?)x.Amount,token)??0; var card=await payments.Where(x=>x.PaymentMethod.MethodCode=="CARD").SumAsync(x=>(decimal?)x.Amount,token)??0; return new(gross,collections,count,cash,upi,card); }
}
