import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { finalize } from 'rxjs';
import { CustomerApiService } from '../customers/customer-api.service';
import { CustomerDropdown } from '../customers/customer.models';

interface SharePreview {
  productName: string; sellingPrice: number; priceLabel: string; description: string; storeName: string;
  productUrl: string; imageUrl: string | null; message: string; whatsAppUrl: string;
  apiSendAvailable: boolean; apiSendUnavailableReason: string | null; linkPreviewUrl: string | null;
}

@Component({
  selector: 'app-product-share-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule],
  templateUrl: './product-share-dialog.component.html',
  styleUrl: './product-share-dialog.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductShareDialogComponent {
  private readonly http = inject(HttpClient);
  private readonly customerApi = inject(CustomerApiService);
  private readonly destroy = inject(DestroyRef);
  private readonly data = inject<{ productId: string }>(MAT_DIALOG_DATA);
  private readonly url = `/api/products/${this.data.productId}/whatsapp-share`;
  private requestId = crypto.randomUUID();
  readonly preview = signal<SharePreview | null>(null);
  readonly loading = signal(false);
  readonly sending = signal(false);
  readonly customersLoading = signal(false);
  readonly customers = signal<CustomerDropdown[]>([]);
  readonly error = signal('');
  readonly customerError = signal('');
  readonly result = signal('');
  readonly attempted = signal(false);
  readonly imageFailed = signal(false);
  customerId = ''; customerSearch = ''; consent = false; comment = '';

  normalizedComment(): string {
    return this.comment.replace(/\r\n?/g, '\n').replace(/<[^>]*>/g, ' ').replace(/https?:\/\/\S+/gi, '')
      .split('\n').map(line => line.replace(/\s+/g, ' ').trim()).join('\n').trim();
  }
  manualMessage(): string {
    const item = this.preview(); if (!item) return '';
    const lines = item.message.split('\n'); const link = lines.pop() ?? '';
    return [...lines, this.normalizedComment(), link].filter(Boolean).join('\n');
  }
  manualWhatsAppUrl(): string { return 'https://wa.me/?text=' + encodeURIComponent(this.manualMessage()); }

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true); this.error.set('');
    this.http.get<SharePreview>(this.url).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.loading.set(false)))
      .subscribe({ next: value => this.preview.set(value), error: error => this.error.set(this.failure(error, 'Unable to load a shareable public product.')) });
  }
  copy(): void {
    const url = this.preview()?.linkPreviewUrl;
    if (!url) { this.error.set('Product preview link is unavailable. Reload after the sharing API is updated.'); return; }
    if (url) void navigator.clipboard.writeText(url).then(() => this.result.set('Product link copied.'), () => this.error.set('Copy is unavailable. Select and copy the product link below.'));
  }
  findCustomers(): void {
    if (this.customersLoading() || this.sending() || this.attempted()) return;
    this.customersLoading.set(true); this.customerError.set(''); this.customerId = ''; this.consent = false;
    this.customerApi.dropdown(this.customerSearch.trim()).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.customersLoading.set(false)))
      .subscribe({ next: value => this.customers.set(value), error: () => this.customerError.set('Unable to load authorized customers. Retry Search or use manual sharing.') });
  }
  send(): void {
    if (this.sending() || this.attempted() || !this.customerId || !this.consent || !this.preview()?.apiSendAvailable || this.normalizedComment().length > 300) return;
    this.sending.set(true); this.error.set(''); this.result.set('');
    this.http.post<{ status: string; providerMessageId: string | null; message: string }>(this.url,
      { customerId: this.customerId, requestId: this.requestId, consentConfirmed: this.consent, comment: this.normalizedComment() })
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.sending.set(false)))
      .subscribe({ next: value => { this.attempted.set(true); this.result.set(`${value.status}: ${value.message}${value.providerMessageId ? ` Reference: ${value.providerMessageId}` : ''}`); },
        error: error => {
          // Keep the request ID on network/5xx retries: the server's durable claim prevents duplicates.
          this.error.set(this.failure(error, 'Send could not be confirmed. Check message status before retrying this same request.'));
        } });
  }
  checkStatus(): void {
    if (this.sending() || !this.attempted()) return;
    this.sending.set(true);
    this.http.get<{ status: string; message: string }>(`${this.url}/${this.requestId}`)
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.sending.set(false)))
      .subscribe({ next: value => this.result.set(`${value.status}: ${value.message}`), error: () => this.error.set('Unable to check status. No message was resent.') });
  }
  private failure(error: unknown, fallback: string): string {
    return error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500
      ? error.error?.message ?? error.error?.detail ?? fallback : fallback;
  }
}
