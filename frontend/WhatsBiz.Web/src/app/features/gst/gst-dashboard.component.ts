import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
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
  readonly summaries = [
    {
      label: 'Sales Register',
      value: 'GST Ready',
      subtitle: 'Outward supplies',
      icon: 'receipt_long',
      tone: 'primary' as const,
    },
    {
      label: 'Purchase Register',
      value: 'GST Ready',
      subtitle: 'Input supplies',
      icon: 'shopping_cart',
      tone: 'info' as const,
    },
    {
      label: 'GSTR-1',
      value: 'Generated on demand',
      subtitle: 'Return summary',
      icon: 'description',
      tone: 'success' as const,
    },
    {
      label: 'GSTR-3B',
      value: 'Generated on demand',
      subtitle: 'Tax liability',
      icon: 'account_balance',
      tone: 'warning' as const,
    },
  ];
  readonly reports = [
    {
      title: 'Sales Register',
      path: '/gst/sales-register',
      text: 'B2B and B2C outward supplies with tax breakup.',
      icon: 'point_of_sale',
      status: 'GST Ready',
    },
    {
      title: 'Purchase Register',
      path: '/gst/purchase-register',
      text: 'Input supplies and available input tax credit.',
      icon: 'shopping_bag',
      status: 'GST Ready',
    },
    {
      title: 'HSN Summary',
      path: '/gst/hsn-summary',
      text: 'Quantity and tax values grouped by HSN and GST rate.',
      icon: 'category',
      status: 'Generated',
    },
    {
      title: 'GSTR-1',
      path: '/gst/gstr1',
      text: 'B2B and B2C outward-supply summary.',
      icon: 'description',
      status: 'GST Ready',
    },
    {
      title: 'GSTR-3B',
      path: '/gst/gstr3b',
      text: 'Output liability, eligible ITC and net payable.',
      icon: 'account_balance',
      status: 'GST Ready',
    },
    {
      title: 'Tax Summary',
      path: '/gst/tax-summary',
      text: 'CGST, SGST, IGST and CESS reconciliation.',
      icon: 'percent',
      status: 'Generated',
    },
  ];
}
