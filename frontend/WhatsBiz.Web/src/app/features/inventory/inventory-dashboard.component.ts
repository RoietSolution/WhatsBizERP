import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { FilterPanelComponent } from '../../shared/components/filter-panel/filter-panel.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { InventoryApiService } from './inventory-api.service';
import { InventorySummary, WarehouseOption } from './inventory.models';
@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    OperationsWorkspaceComponent,
    FilterPanelComponent,
    StatusChipComponent,
  ],
  templateUrl: './inventory-dashboard.component.html',
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
      .context-card p {
        color: var(--wb-text-secondary);
      }
      .context-card {
        display: flex;
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        flex-direction: column;
        gap: 8px;
      }
      .context-card h3,
      .context-card p {
        margin: 0;
      }
      .context-card app-status-chip {
        margin-bottom: 4px;
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
export class InventoryDashboardComponent {
  readonly summary = signal<InventorySummary | null>(null);
  readonly warehouses = signal<WarehouseOption[]>([]);
  readonly summaries = computed(() => {
    const x = this.summary();
    return [
      {
        label: 'Inventory value',
        value: x?.totalStockValue ?? 0,
        subtitle: 'Current stock',
        icon: 'currency_rupee',
        tone: 'primary' as const,
      },
      {
        label: 'Low stock',
        value: x?.lowStockProducts ?? 0,
        subtitle: 'Needs attention',
        icon: 'warning',
        tone: 'warning' as const,
      },
      {
        label: 'Out of stock',
        value: x?.outOfStockProducts ?? 0,
        subtitle: 'Unavailable',
        icon: 'remove_shopping_cart',
        tone: 'danger' as const,
      },
      {
        label: 'Reserved stock',
        value: x?.reservedStock ?? 0,
        subtitle: 'Committed',
        icon: 'lock',
        tone: 'info' as const,
      },
    ];
  });
  warehouseId = '';
  constructor(private api: InventoryApiService) {
    api.warehouses().subscribe((x) => this.warehouses.set(x));
    this.load();
  }
  load() {
    this.api.summary(this.warehouseId || undefined).subscribe((x) => this.summary.set(x));
  }
}
