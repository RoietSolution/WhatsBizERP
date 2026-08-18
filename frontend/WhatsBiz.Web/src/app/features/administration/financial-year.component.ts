import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { AdminApiService, FinancialYear } from './admin-api.service';
@Component({
  imports: [FormsModule, MatButtonModule],
  templateUrl: './financial-year.component.html',
  styles: [
    `
      form {
        display: flex;
        gap: 0.7rem;
        flex-wrap: wrap;
        background: #fff;
        padding: 1rem;
      }
      input,
      select {
        padding: 0.6rem;
      }
      table {
        width: 100%;
        margin-top: 1rem;
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
export class FinancialYearComponent {
  years = signal<FinancialYear[]>([]);
  model = { code: '', startDate: '', endDate: '', status: 'OPEN', isDefault: false };
  constructor(private api: AdminApiService) {
    this.load();
  }
  load() {
    this.api.years().subscribe((x) => this.years.set(x));
  }
  save() {
    this.api.saveYear(this.model).subscribe(() => this.load());
  }
}
