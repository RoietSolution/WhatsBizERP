import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { FollowUp, ReceivablesApiService } from './receivables-api.service';
@Component({
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './collection-followup.component.html',
  styles: [
    `
      .grid {
        display: grid;
        grid-template-columns: repeat(4, 1fr);
        gap: 1rem;
      }
      .wide {
        grid-column: span 4;
      }
      .table {
        overflow: auto;
      }
      table {
        width: 100%;
        border-collapse: collapse;
        min-width: 700px;
      }
      th,
      td {
        padding: 0.65rem;
        border-bottom: 1px solid #ddd;
        text-align: left;
      }
      @media (max-width: 800px) {
        .grid {
          grid-template-columns: 1fr;
        }
        .wide {
          grid-column: auto;
        }
      }
    `,
  ],
})
export class CollectionFollowUpComponent {
  readonly customers = signal<any[]>([]);
  readonly rows = signal<FollowUp[]>([]);
  customerId = '';
  mode = 'PHONE';
  nextDate = '';
  commitment = '';
  notes = '';
  constructor(
    private api: ReceivablesApiService,
    private snack: MatSnackBar,
  ) {
    api.customers().subscribe((x) => this.customers.set(x));
    this.load();
  }
  load() {
    this.api.followUps(this.customerId || undefined).subscribe((x) => this.rows.set(x));
  }
  save() {
    if (!this.customerId || !this.notes) {
      this.snack.open('Customer and notes are required.', 'Close', { duration: 2500 });
      return;
    }
    this.api
      .saveFollowUp({
        customerId: this.customerId,
        invoiceId: null,
        followUpDate: new Date().toISOString(),
        nextFollowUpDate: this.nextDate ? new Date(this.nextDate).toISOString() : null,
        paymentCommitmentDate: this.commitment ? new Date(this.commitment).toISOString() : null,
        communicationMode: this.mode,
        notes: this.notes,
        outcome: null,
      })
      .subscribe(() => {
        this.snack.open('Follow-up saved.', undefined, { duration: 2500 });
        this.notes = '';
        this.load();
      });
  }
}
