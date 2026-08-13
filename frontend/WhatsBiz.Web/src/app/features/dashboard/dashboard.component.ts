import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { CurrentUserService } from '../../core/services/current-user.service';
import {
  ChartComponent,
  DashboardChartConfig,
} from '../../shared/components/chart/chart.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingOverlayComponent } from '../../shared/components/loading-overlay/loading-overlay.component';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatisticsCardComponent } from '../../shared/components/statistics-card/statistics-card.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { SummaryCardComponent } from '../../shared/components/summary-card/summary-card.component';
import {
  CustomerAnalytics,
  DashboardApiService,
  Finance,
  Inventory,
  Notification,
  Point,
  PurchaseAnalytics,
  SalesAnalytics,
  Summary,
  SupplierAnalytics,
} from './dashboard-api.service';

type SalesPeriod = 'hourly' | 'daily' | 'monthly' | 'yearly';
interface Kpi {
  label: string;
  value: number;
  icon: string;
  tone: 'primary' | 'success' | 'warning' | 'danger' | 'info';
  comparison: string;
  positive: boolean;
  sparkline: number[];
}
interface Activity {
  id: string;
  icon: string;
  title: string;
  detail: string;
  amount?: number;
  date: string;
  status: string;
}

@Component({
  selector: 'app-dashboard',
  imports: [
    CurrencyPipe,
    DatePipe,
    FormsModule,
    MatButtonModule,
    RouterLink,
    ChartComponent,
    EmptyStateComponent,
    LoadingOverlayComponent,
    PageContainerComponent,
    PageHeaderComponent,
    StatisticsCardComponent,
    StatusChipComponent,
    SummaryCardComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardComponent {
  private readonly api = inject(DashboardApiService);
  private readonly currentUser = inject(CurrentUserService);
  readonly summary = signal<Summary | null>(null);
  readonly inventory = signal<Inventory | null>(null);
  readonly finance = signal<Finance | null>(null);
  readonly customers = signal<CustomerAnalytics | null>(null);
  readonly suppliers = signal<SupplierAnalytics | null>(null);
  readonly notifications = signal<Notification[]>([]);
  readonly sales = signal<SalesAnalytics | null>(null);
  readonly purchase = signal<PurchaseAnalytics | null>(null);
  readonly coreLoading = signal(false);
  readonly analyticsLoading = signal(false);
  readonly salesPeriod = signal<SalesPeriod>('hourly');
  readonly periods: SalesPeriod[] = ['hourly', 'daily', 'monthly', 'yearly'];
  readonly today = new Date();
  readonly placeholders = Array.from({ length: 8 }, (_, index) => index);
  from = new Date().toISOString().slice(0, 10);
  to = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10);
  readonly quickActions = [
    { label: 'New Sale', icon: 'point_of_sale', route: '/pos' },
    { label: 'New Purchase', icon: 'shopping_cart', route: '/purchases/create' },
    { label: 'Add Product', icon: 'add_box', route: '/products/new' },
    { label: 'Add Customer', icon: 'person_add', route: '/customers/new' },
    { label: 'Stock Adjustment', icon: 'tune', route: '/inventory/adjustment' },
    { label: 'Reports', icon: 'analytics', route: '/analytics/sales' },
  ];
  readonly userName = computed(() => this.currentUser.user()?.username ?? 'User');
  readonly greeting = computed(() => {
    const hour = new Date().getHours();
    return hour < 12 ? 'Good Morning' : hour < 17 ? 'Good Afternoon' : 'Good Evening';
  });
  readonly businessSummary = computed(() => {
    const value = this.summary();
    return value
      ? `Today’s sales are ${this.money(value.todaySales)} with net collections of ${this.money(value.netCollection)}.`
      : 'Your latest sales, purchases, inventory and cash position in one place.';
  });
  readonly kpis = computed<Kpi[]>(() => {
    const s = this.summary(),
      f = this.finance(),
      i = this.inventory(),
      c = this.customers(),
      p = this.suppliers();
    if (!s || !f || !i || !c || !p) return [];
    const salesSpark = this.sparkline(this.sales()?.hourly ?? []);
    const collectionSpark = this.sparkValues([
      s.cashCollection,
      s.upiCollection,
      s.cardCollection,
      s.netCollection,
    ]);
    return [
      {
        label: "Today's Sales",
        value: s.todaySales,
        icon: 'point_of_sale',
        tone: 'primary',
        comparison: `Net ${this.money(s.netCollection)}`,
        positive: s.todaySales >= s.todayPurchase,
        sparkline: salesSpark,
      },
      {
        label: "Today's Purchase",
        value: s.todayPurchase,
        icon: 'shopping_cart',
        tone: 'info',
        comparison: `Expense ${this.money(s.todayExpense)}`,
        positive: true,
        sparkline: this.sparkline(this.purchase()?.daily ?? []),
      },
      {
        label: 'Cash Balance',
        value: f.cashBalance,
        icon: 'payments',
        tone: 'success',
        comparison: `Collected ${this.money(s.cashCollection)}`,
        positive: f.cashBalance >= 0,
        sparkline: collectionSpark,
      },
      {
        label: 'Bank Balance',
        value: f.bankBalance,
        icon: 'account_balance',
        tone: 'primary',
        comparison: `UPI ${this.money(s.upiCollection)}`,
        positive: f.bankBalance >= 0,
        sparkline: collectionSpark.slice().reverse(),
      },
      {
        label: 'Customer Outstanding',
        value: c.customerOutstanding,
        icon: 'request_quote',
        tone: 'warning',
        comparison: `${c.newCustomers} recent customers`,
        positive: c.customerOutstanding === 0,
        sparkline: this.sparkline(c.topCustomers),
      },
      {
        label: 'Supplier Outstanding',
        value: p.supplierOutstanding,
        icon: 'receipt',
        tone: 'danger',
        comparison: `${p.pendingPayments} pending payments`,
        positive: p.supplierOutstanding === 0,
        sparkline: this.sparkline(p.topSuppliers),
      },
      {
        label: 'Inventory Value',
        value: i.totalInventoryValue,
        icon: 'inventory_2',
        tone: 'info',
        comparison: `${i.lowStockItems} low stock`,
        positive: i.lowStockItems === 0,
        sparkline: this.sparkline(i.attentionItems),
      },
      {
        label: 'Net Profit Today',
        value: f.profitToday,
        icon: 'trending_up',
        tone: 'success',
        comparison: `Expense ${this.money(s.todayExpense)}`,
        positive: f.profitToday >= 0,
        sparkline: salesSpark.slice().reverse(),
      },
    ];
  });
  readonly salesPoints = computed(() => this.sales()?.[this.salesPeriod()] ?? []);
  readonly salesChart = computed<DashboardChartConfig>(() =>
    this.axisChart(
      this.salesPeriod() === 'hourly' ? 'area' : 'bar',
      'Sales',
      this.salesPoints(),
      true,
    ),
  );
  readonly paymentChart = computed<DashboardChartConfig>(() =>
    this.donutChart(this.sales()?.byPaymentMode ?? []),
  );
  readonly purchaseChart = computed<DashboardChartConfig>(() =>
    this.axisChart('area', 'Purchases', this.purchase()?.monthly ?? [], true),
  );
  readonly supplierChart = computed<DashboardChartConfig>(() => ({
    ...this.axisChart(
      'bar',
      'Purchase Value',
      (this.purchase()?.bySupplier ?? []).slice(0, 6),
      true,
    ),
    horizontal: true,
    height: 300,
  }));
  readonly topProductLabel = computed(() => this.sales()?.topProducts?.[0]?.label ?? '—');
  readonly slowProductLabel = computed(() => this.sales()?.leastProducts?.[0]?.label ?? '—');
  readonly activities = computed<Activity[]>(() => {
    const s = this.summary();
    if (!s) return [];
    const sales = s.recentSales.map((item) => ({
      id: `sale-${item.id}`,
      icon: 'receipt_long',
      title: 'Recent Sale',
      detail: item.number,
      amount: item.amount,
      date: item.date,
      status: item.status,
    }));
    const purchases = s.recentPurchases.map((item) => ({
      id: `purchase-${item.id}`,
      icon: 'shopping_bag',
      title: 'Recent Purchase',
      detail: item.number,
      amount: item.amount,
      date: item.date,
      status: item.status,
    }));
    const system = this.notifications()
      .filter((item) => /payment|receipt|adjust/i.test(`${item.type} ${item.title}`))
      .map((item) => ({
        id: `notice-${item.id}`,
        icon: this.notificationIcon(item),
        title: item.title,
        detail: item.message ?? item.type,
        date: item.generatedOn,
        status: item.severity,
      }));
    return [...sales, ...purchases, ...system]
      .sort((a, b) => +new Date(b.date) - +new Date(a.date))
      .slice(0, 8);
  });

  constructor() {
    this.load();
  }
  load(refresh = false): void {
    this.coreLoading.set(true);
    const query = this.query(refresh);
    forkJoin({
      summary: this.api.summary(query),
      inventory: this.api.inventory(refresh),
      finance: this.api.finance(query),
      customers: this.api.customers(query),
      suppliers: this.api.suppliers(query),
      notifications: this.api.notifications(refresh),
    }).subscribe({
      next: (data) => {
        this.summary.set(data.summary);
        this.inventory.set(data.inventory);
        this.finance.set(data.finance);
        this.customers.set(data.customers);
        this.suppliers.set(data.suppliers);
        this.notifications.set(data.notifications);
        this.coreLoading.set(false);
      },
      error: () => this.coreLoading.set(false),
    });
    this.analyticsLoading.set(true);
    forkJoin({
      sales: this.api.sales(query).pipe(catchError(() => of(null))),
      purchase: this.api.purchase(query).pipe(catchError(() => of(null))),
    }).subscribe(({ sales, purchase }) => {
      this.sales.set(sales);
      this.purchase.set(purchase);
      this.analyticsLoading.set(false);
    });
  }
  notificationIcon(item: Notification): string {
    const value = `${item.type} ${item.title}`.toLowerCase();
    if (value.includes('stock')) return 'inventory';
    if (value.includes('payment')) return 'payments';
    if (value.includes('collection')) return 'request_quote';
    if (value.includes('expir')) return 'event_busy';
    return item.severity.toLowerCase() === 'critical' ? 'error' : 'notifications';
  }
  private query(refresh: boolean): object {
    return {
      from: new Date(this.from).toISOString(),
      to: new Date(this.to).toISOString(),
      refresh,
    };
  }
  private axisChart(
    type: 'area' | 'bar',
    name: string,
    points: Point[],
    currency: boolean,
  ): DashboardChartConfig {
    return {
      type,
      series: [{ name, data: points.map((point) => point.value) }],
      categories: points.map((point) => point.label),
      colors: ['#1D4ED8'],
      currency,
      height: 310,
    };
  }
  private donutChart(points: Point[]): DashboardChartConfig {
    return {
      type: 'donut',
      series: points.map((point) => point.value),
      labels: points.map((point) => point.label),
      currency: true,
      height: 310,
    };
  }
  private sparkline(points: Point[]): number[] {
    if (!points.length) return [];
    const values = points.slice(-8).map((point) => Math.abs(point.value));
    const max = Math.max(...values, 1);
    return values.map((value) => Math.max(8, Math.round((value / max) * 100)));
  }
  private sparkValues(values: number[]): number[] {
    const absolute = values.map((value) => Math.abs(value));
    const max = Math.max(...absolute, 1);
    return absolute.map((value) => Math.max(8, Math.round((value / max) * 100)));
  }
  private money(value: number): string {
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      maximumFractionDigits: 0,
    }).format(value);
  }
}
