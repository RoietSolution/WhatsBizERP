import { Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { POSApiService } from './pos-api.service';
import { TodaySales } from './pos.models';
@Component({
  imports: [MatCardModule, MatButtonModule],
  templateUrl: './today-sales.component.html',
  styles: [
    `
      header {
        display: flex;
        justify-content: space-between;
      }
      .cards {
        display: grid;
        grid-template-columns: repeat(3, 1fr);
        gap: 1rem;
      }
      mat-card {
        padding: 1rem;
      }
      @media (max-width: 700px) {
        .cards {
          grid-template-columns: 1fr 1fr;
        }
      }
    `,
  ],
})
export class TodaySalesComponent {
  readonly data = signal<TodaySales | null>(null);
  constructor(private readonly api: POSApiService) {
    api.today().subscribe((x) => this.data.set(x));
  }
  export() {
    this.api.export().subscribe((b) => {
      const u = URL.createObjectURL(b);
      const a = document.createElement('a');
      a.href = u;
      a.download = 'daily-sales.xlsx';
      a.click();
      URL.revokeObjectURL(u);
    });
  }
}
