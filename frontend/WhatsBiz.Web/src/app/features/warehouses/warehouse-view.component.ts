import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { WarehouseApiService } from './warehouse-api.service';
import { Warehouse } from './warehouse.models';
@Component({
  imports: [RouterLink, MatButtonModule, MatTabsModule],
  templateUrl: './warehouse-view.component.html',
  styles: [
    `
      header {
        display: flex;
        justify-content: space-between;
      }
      mat-tab-group {
        margin-top: 1rem;
      }
      dl {
        display: grid;
        grid-template-columns: 150px 1fr;
        gap: 1rem;
        padding: 1rem;
      }
      dd {
        margin: 0;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WarehouseViewComponent {
  readonly warehouse = signal<Warehouse | null>(null);
  constructor(api: WarehouseApiService, route: ActivatedRoute) {
    api.get(route.snapshot.paramMap.get('id') ?? '').subscribe((x) => this.warehouse.set(x));
  }
}
