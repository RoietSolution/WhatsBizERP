using WhatsBiz.Application.Features.Dashboard;
namespace WhatsBiz.Application.Common.Interfaces;
public static class DashboardRepositoryTenantExtensions
{
 public static Task<DashboardSummaryDto> Summary(this IDashboardRepository repository, Guid _, DateTimeOffset f, DateTimeOffset t, bool refresh, CancellationToken token) => repository.Summary(f,t,refresh,token);
 public static Task<SalesAnalyticsDto> Sales(this IDashboardRepository repository, Guid _, DateTimeOffset f, DateTimeOffset t, bool refresh, CancellationToken token) => repository.Sales(f,t,refresh,token);
 public static Task<PurchaseAnalyticsDto> Purchase(this IDashboardRepository repository, Guid _, DateTimeOffset f, DateTimeOffset t, bool refresh, CancellationToken token) => repository.Purchase(f,t,refresh,token);
 public static Task<InventoryAnalyticsDto> Inventory(this IDashboardRepository repository, Guid _, bool refresh, CancellationToken token) => repository.Inventory(refresh,token);
 public static Task<CustomerAnalyticsDto> Customers(this IDashboardRepository repository, Guid _, DateTimeOffset f, DateTimeOffset t, bool refresh, CancellationToken token) => repository.Customers(f,t,refresh,token);
 public static Task<SupplierAnalyticsDto> Suppliers(this IDashboardRepository repository, Guid _, DateTimeOffset f, DateTimeOffset t, bool refresh, CancellationToken token) => repository.Suppliers(f,t,refresh,token);
 public static Task<FinanceAnalyticsDto> Finance(this IDashboardRepository repository, Guid _, DateTimeOffset f, DateTimeOffset t, bool refresh, CancellationToken token) => repository.Finance(f,t,refresh,token);
 public static Task<IReadOnlyCollection<DashboardNotificationDto>> Notifications(this IDashboardRepository repository, Guid _, bool refresh, CancellationToken token) => repository.Notifications(refresh,token);
}
