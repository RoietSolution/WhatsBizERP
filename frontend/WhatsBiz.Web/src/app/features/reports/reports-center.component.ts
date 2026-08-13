import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { forkJoin } from 'rxjs';
import { DashboardApiService } from '../dashboard/dashboard-api.service';
import { GstApiService } from '../gst/gst-api.service';
@Component({
  imports: [
    RouterLink,
    CurrencyPipe,
    MatButtonModule,
    OperationsWorkspaceComponent,
    FilterPanelComponent,
    StatusChipComponent,
  ],
  templateUrl: './reports-center.component.html',
  styles: [
    `
      .actions,
      .filters {
        display: flex;
        flex-wrap: wrap;
        gap: 5px;
      }
      .category-grid {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 12px;
      }
      .category-grid article {
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        transition: 200ms;
      }
      .category-grid article:hover {
        border-color: var(--wb-primary);
        box-shadow: var(--wb-shadow-md);
        transform: translateY(-2px);
      }
      article > div {
        display: flex;
        align-items: center;
        justify-content: space-between;
      }
      article > div > .material-symbols-rounded {
        display: grid;
        width: 42px;
        height: 42px;
        color: var(--wb-primary);
        background: var(--wb-primary-soft);
        border-radius: 10px;
        place-items: center;
      }
      article h3 {
        margin: 12px 0 5px;
      }
      article p {
        min-height: 38px;
        margin: 0 0 10px;
        color: var(--wb-text-secondary);
        font-size: 12px;
      }
      article nav {
        display: flex;
        flex-direction: column;
      }
      article nav a {
        display: flex;
        padding: 7px 0;
        color: var(--wb-text-primary);
        text-decoration: none;
        border-top: 1px solid var(--wb-border);
        align-items: center;
        justify-content: space-between;
      }
      article nav a:hover {
        color: var(--wb-primary);
      }
      article nav .material-symbols-rounded {
        font-size: 17px;
      }
      .insight {
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
      }
      .insight h3 {
        margin-top: 0;
      }
      dl {
        display: grid;
        grid-template-columns: 1fr auto;
        gap: 10px;
      }
      dd {
        margin: 0;
        font-weight: 700;
      }
      .export-actions {
        display: grid;
        grid-template-columns: repeat(2, 1fr);
        gap: 6px;
        margin-bottom: 12px;
      }
      @media (max-width: 767px) {
        .category-grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportsCenterComponent {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly gstApi = inject(GstApiService);
  readonly favorites = signal<string[]>([]);
  readonly loading = signal(true);
  readonly lastRefreshed = signal<Date | null>(null);
  readonly reportMetrics = signal({ sales: 0, purchases: 0, inventory: 0, gst: 0 });
  readonly summaries = computed(() => [
    {
      label: 'Current month sales',
      value: this.reportMetrics().sales,
      subtitle: 'Sales performance',
      icon: 'point_of_sale',
      tone: 'primary' as const,
    },
    {
      label: 'Current month purchases',
      value: this.reportMetrics().purchases,
      subtitle: 'Purchase analytics',
      icon: 'shopping_cart',
      tone: 'info' as const,
    },
    {
      label: 'Inventory value',
      value: this.reportMetrics().inventory,
      subtitle: 'Stock valuation',
      icon: 'inventory_2',
      tone: 'success' as const,
    },
    {
      label: 'GST liability',
      value: this.reportMetrics().gst,
      subtitle: 'Tax center',
      icon: 'percent',
      tone: 'warning' as const,
    },
  ]);
  readonly categories = [
    {
      title: 'Sales Reports',
      icon: 'trending_up',
      description: 'Sales summaries, invoice history, trends, and product performance.',
      reports: [
        { label: 'Sales Analytics', route: '/analytics/sales' },
        { label: 'Invoice History', route: '/pos/history' },
      ],
    },
    {
      title: 'Purchase Reports',
      icon: 'shopping_bag',
      description: 'Purchase value, supplier invoices, outstanding orders, and trends.',
      reports: [
        { label: 'Purchase Analytics', route: '/analytics/purchase' },
        { label: 'Purchase History', route: '/purchases' },
      ],
    },
    {
      title: 'Inventory Reports',
      icon: 'inventory_2',
      description: 'Stock valuation, movement, alerts, and warehouse availability.',
      reports: [
        { label: 'Inventory Analytics', route: '/analytics/inventory' },
        { label: 'Stock Balance', route: '/inventory/balance' },
      ],
    },
    {
      title: 'Finance Reports',
      icon: 'account_balance',
      description: 'Cash flow, books, ledgers, receivables, and payables.',
      reports: [
        { label: 'Finance Analytics', route: '/analytics/finance' },
        { label: 'Day Book', route: '/finance/daybook' },
      ],
    },
    {
      title: 'GST Reports',
      icon: 'percent',
      description: 'GST registers, HSN summary, liability, GSTR-1, and GSTR-3B.',
      reports: [
        { label: 'GST Center', route: '/gst' },
        { label: 'Tax Summary', route: '/gst/tax-summary' },
      ],
    },
    {
      title: 'Customer Reports',
      icon: 'groups',
      description: 'Customer balances, ageing, collections, and ledger history.',
      reports: [
        { label: 'Customer Outstanding', route: '/finance/customer-outstanding' },
        { label: 'Customer Ledger', route: '/finance/customer-ledger' },
      ],
    },
    {
      title: 'Supplier Reports',
      icon: 'local_shipping',
      description: 'Supplier balances, ageing, payment history, and purchases.',
      reports: [
        { label: 'Supplier Outstanding', route: '/finance/supplier-outstanding' },
        { label: 'Supplier Ledger', route: '/finance/supplier-ledger' },
      ],
    },
    {
      title: 'Administration Reports',
      icon: 'admin_panel_settings',
      description: 'Audit history, company settings, and operational administration.',
      reports: [
        { label: 'Audit History', route: '/admin/audit' },
        { label: 'Company Profile', route: '/admin/company' },
      ],
    },
  ];
  constructor() {
    this.load();
  }
  load(): void {
    this.loading.set(true);
    const today = new Date();
    const from = new Date(today.getFullYear(), today.getMonth(), 1).toISOString();
    const to = new Date(today.getFullYear(), today.getMonth() + 1, 1).toISOString();
    forkJoin({
      summary: this.dashboardApi.summary({ from, to, refresh: true }),
      inventory: this.dashboardApi.inventory(true),
      gst: this.gstApi.report('tax-summary', { from, to }),
    }).subscribe({
      next: ({ summary, inventory, gst }) => {
        this.reportMetrics.set({
          sales: summary.todaySales,
          purchases: summary.todayPurchase,
          inventory: inventory.totalInventoryValue,
          gst: gst.reduce((total, row) => total + (Number(row.totalTax) || 0), 0),
        });
        this.lastRefreshed.set(new Date());
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
  favorite(title: string) {
    this.favorites.update((x) =>
      x.includes(title) ? x.filter((v) => v !== title) : [...x, title],
    );
  }
}
