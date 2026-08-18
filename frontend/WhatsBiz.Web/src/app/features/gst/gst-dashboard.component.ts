import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { forkJoin } from 'rxjs';
import { GstApiService } from './gst-api.service';
@Component({
  imports: [RouterLink, MatButtonModule, OperationsWorkspaceComponent, StatusChipComponent],
  templateUrl: './gst-dashboard.component.html',
  styles: [
    `
      .gst-grid {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 12px;
      }
      .gst-grid article {
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        transition: 200ms;
      }
      .gst-grid article:hover {
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
      article h2 {
        margin: 12px 0 5px;
        font-size: 16px;
      }
      article p,
      .insight p {
        color: var(--wb-text-secondary);
      }
      article a {
        display: flex;
        justify-content: space-between;
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
      .insight nav {
        display: flex;
        flex-direction: column;
      }
      .insight nav a {
        padding: 8px 0;
        color: var(--wb-primary);
        text-decoration: none;
        border-bottom: 1px solid var(--wb-border);
      }
      @media (max-width: 700px) {
        .gst-grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GstDashboardComponent {
  private readonly api = inject(GstApiService);
  readonly loading = signal(true);
  readonly loaded = signal(false);
  readonly metrics = signal({ sales: 0, purchases: 0, gstr1: 0, gstr3b: 0 });
  readonly summaries = computed(() => [
    {
      label: 'Sales Register',
      value: this.metrics().sales,
      subtitle: 'Outward supplies',
      icon: 'receipt_long',
      tone: 'primary' as const,
    },
    {
      label: 'Purchase Register',
      value: this.metrics().purchases,
      subtitle: 'Input supplies',
      icon: 'shopping_cart',
      tone: 'info' as const,
    },
    {
      label: 'GSTR-1',
      value: this.metrics().gstr1,
      subtitle: 'Return summary',
      icon: 'description',
      tone: 'success' as const,
    },
    {
      label: 'GSTR-3B',
      value: this.metrics().gstr3b,
      subtitle: 'Tax liability',
      icon: 'account_balance',
      tone: 'warning' as const,
    },
  ]);
  readonly reports = [
    {
      title: 'Sales Register',
      path: '/gst/sales-register',
      text: 'B2B and B2C outward supplies with tax breakup.',
      icon: 'point_of_sale',
    },
    {
      title: 'Purchase Register',
      path: '/gst/purchase-register',
      text: 'Input supplies and available input tax credit.',
      icon: 'shopping_bag',
    },
    {
      title: 'HSN Summary',
      path: '/gst/hsn-summary',
      text: 'Quantity and tax values grouped by HSN and GST rate.',
      icon: 'category',
    },
    {
      title: 'GSTR-1',
      path: '/gst/gstr1',
      text: 'B2B and B2C outward-supply summary.',
      icon: 'description',
    },
    {
      title: 'GSTR-3B',
      path: '/gst/gstr3b',
      text: 'Output liability, eligible ITC and net payable.',
      icon: 'account_balance',
    },
    {
      title: 'Tax Summary',
      path: '/gst/tax-summary',
      text: 'CGST, SGST, IGST and CESS reconciliation.',
      icon: 'percent',
    },
  ];
  constructor() {
    const now = new Date();
    const filter = {
      from: new Date(now.getFullYear(), now.getMonth(), 1).toISOString(),
      to: new Date(now.getFullYear(), now.getMonth() + 1, 1).toISOString(),
    };
    forkJoin({
      sales: this.api.report('sales-register', filter),
      purchases: this.api.report('purchase-register', filter),
      gstr1: this.api.report('gstr1', filter),
      gstr3b: this.api.report('gstr3b', filter),
    }).subscribe({
      next: (rows) => {
        const tax = (values: typeof rows.sales) =>
          values.reduce((sum, row) => sum + (Number(row.totalTax) || 0), 0);
        this.metrics.set({
          sales: rows.sales.reduce((sum, row) => sum + (Number(row.taxableAmount) || 0), 0),
          purchases: rows.purchases.reduce((sum, row) => sum + (Number(row.taxableAmount) || 0), 0),
          gstr1: tax(rows.gstr1),
          gstr3b: rows.gstr3b.reduce((sum, row) => sum + (Number(row.netTaxPayable) || 0), 0),
        });
        this.loaded.set(true);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
