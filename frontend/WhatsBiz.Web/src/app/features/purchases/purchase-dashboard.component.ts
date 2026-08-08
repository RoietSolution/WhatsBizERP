import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { PurchaseApiService } from './purchase-api.service';
import { PurchaseDashboard } from './purchase.models';
@Component({
  imports: [RouterLink, MatButtonModule, OperationsWorkspaceComponent, StatusChipComponent],
  templateUrl: './purchase-dashboard.component.html',
  styles: [
    `
      .actions {
        display: flex;
        flex-wrap: wrap;
      }
      .operations {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 12px;
      }
      .operations a {
        display: grid;
        min-height: 140px;
        padding: 20px;
        color: var(--wb-text-primary);
        text-decoration: none;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        transition: 200ms;
      }
      .operations a:hover {
        border-color: var(--wb-primary);
        box-shadow: var(--wb-shadow-md);
        transform: translateY(-2px);
      }
      .operations .material-symbols-rounded {
        color: var(--wb-primary);
        font-size: 30px;
      }
      .operations strong {
        margin-top: 14px;
      }
      .operations small,
      .context-card p,
      .context-card small {
        color: var(--wb-text-secondary);
      }
      .context-card {
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
      }
      .context-card h3 {
        margin-top: 0;
      }
      .context-card > strong {
        display: block;
        color: var(--wb-primary);
        font-size: 28px;
      }
      @media (max-width: 767px) {
        .operations {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PurchaseDashboardComponent {
  readonly data = signal<PurchaseDashboard | null>(null);
  readonly summaries = computed(() => {
    const x = this.data();
    return [
      {
        label: 'Purchase today',
        value: x?.todayPurchases ?? 0,
        subtitle: 'Today',
        icon: 'shopping_cart',
        tone: 'primary' as const,
      },
      {
        label: 'Pending orders',
        value: x?.todayCount ?? 0,
        subtitle: 'Invoices today',
        icon: 'pending_actions',
        tone: 'warning' as const,
      },
      {
        label: 'Outstanding',
        value: x?.outstanding ?? 0,
        subtitle: 'Supplier payable',
        icon: 'account_balance_wallet',
        tone: 'danger' as const,
      },
      {
        label: 'Month purchases',
        value: x?.monthPurchases ?? 0,
        subtitle: 'Current month',
        icon: 'calendar_month',
        tone: 'info' as const,
      },
    ];
  });
  constructor(api: PurchaseApiService) {
    api.dashboard().subscribe((x) => this.data.set(x));
  }
}
