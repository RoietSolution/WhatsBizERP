import { Component, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { PurchaseApiService } from './purchase-api.service';
import { Purchase } from './purchase.models';
@Component({
  imports: [RouterLink, MatButtonModule, MatCardModule],
  templateUrl: './purchase-details.component.html',
  styles: [
    `
      header,
      section {
        display: flex;
        justify-content: space-between;
        gap: 1rem;
      }
      mat-card {
        padding: 1rem;
        flex: 1;
      }
      .table {
        overflow: auto;
      }
      table {
        width: 100%;
        border-collapse: collapse;
      }
      th,
      td {
        padding: 0.7rem;
        border-bottom: 1px solid #ddd;
        text-align: left;
      }
    `,
  ],
})
export class PurchaseDetailsComponent {
  p = signal<Purchase | null>(null);
  constructor(api: PurchaseApiService, route: ActivatedRoute) {
    api.get(route.snapshot.paramMap.get('id')!).subscribe((x) => this.p.set(x));
  }
}
