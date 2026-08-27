import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { finalize } from 'rxjs';
import { ProductApiService } from './product-api.service';
import { ProductHistory, ProductListItem } from './product.models';

@Component({
  selector: 'app-product-history-dialog',
  imports: [DatePipe, MatButtonModule, MatDialogModule, MatProgressSpinnerModule],
  template: `
    <h2 mat-dialog-title>Product history</h2>
    <mat-dialog-content>
      <header>
        <strong>{{ product.productName }}</strong>
        <span>{{ product.productCode }}</span>
      </header>
      @if (loading()) {
        <div class="state"><mat-spinner diameter="36" /><span>Loading history...</span></div>
      } @else if (error()) {
        <div class="state error"><span class="material-symbols-rounded">error</span>{{ error() }}</div>
      } @else {
        <ol class="timeline">
          @for (item of history(); track item.id + '-' + item.occurredOn) {
            <li>
              <span class="marker material-symbols-rounded">{{ icon(item.action) }}</span>
              <div>
                <div class="event-title">
                  <strong>{{ label(item.action) }}</strong>
                  <span [class.failed]="!item.succeeded">{{ item.succeeded ? 'Completed' : 'Failed' }}</span>
                </div>
                <p>{{ item.details }}</p>
                <small>{{ item.occurredOn | date: 'medium' }} · {{ item.userName || 'System' }}</small>
              </div>
            </li>
          } @empty {
            <li class="empty">No history is available for this product.</li>
          }
        </ol>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Close</button></mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { width: min(620px, 82vw); min-height: 180px; }
    header { display: flex; flex-direction: column; padding: 4px 0 16px; border-bottom: 1px solid var(--wb-border); }
    header span, small, p { color: var(--wb-text-secondary); }
    .state { min-height: 150px; display: flex; align-items: center; justify-content: center; gap: 12px; }
    .error { color: var(--wb-danger); }
    .timeline { list-style: none; margin: 0; padding: 16px 0 0; }
    .timeline li { display: grid; grid-template-columns: 36px 1fr; gap: 10px; padding-bottom: 18px; }
    .marker { display: grid; width: 32px; height: 32px; place-items: center; color: var(--wb-primary); background: var(--wb-primary-soft); border-radius: 50%; font-size: 18px; }
    .event-title { display: flex; justify-content: space-between; gap: 16px; }
    .event-title span { color: var(--wb-success); font-size: 12px; }
    .event-title span.failed { color: var(--wb-danger); }
    p { margin: 3px 0; }
    .empty { display: block !important; color: var(--wb-text-secondary); text-align: center; padding: 36px !important; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductHistoryDialogComponent {
  readonly product = inject<ProductListItem>(MAT_DIALOG_DATA);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly history = signal<ProductHistory[]>([]);

  constructor(api: ProductApiService) {
    api.history(this.product.productId)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (history) => this.history.set(history),
        error: () => this.error.set('Product history could not be loaded.'),
      });
  }

  label(action: string): string {
    return action.replaceAll('_', ' ').toLowerCase().replace(/^./, (value) => value.toUpperCase());
  }

  icon(action: string): string {
    if (action === 'CREATED') return 'add_circle';
    if (action === 'UPDATED') return 'edit';
    if (action === 'IMAGE_ADDED') return 'add_photo_alternate';
    if (action === 'IMAGE_DELETED') return 'hide_image';
    if (action === 'DELETED') return 'delete';
    return 'history';
  }
}
