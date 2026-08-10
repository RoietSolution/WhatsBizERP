import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { AdminApiService, CustomerNotificationHistory, CustomerNotificationSettings, NotificationConfigurationStatus } from './admin-api.service';

const defaults: CustomerNotificationSettings = {
  enabled: false, whatsAppEnabled: false, smsEnabled: false, successfulSale: true, successfulPayment: true,
  whatsAppTemplate: 'Thank you for shopping with {{company_name}}!\n\nInvoice: {{invoice_no}}\nAmount: {{currency}}{{total_amount}}\n\nWe appreciate your business.\nVisit us again!',
  smsTemplate: 'Thank you for shopping with {{company_name}}. Invoice {{invoice_no}}, Amount {{currency}}{{total_amount}}. We appreciate your business.',
};

@Component({
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, PageContainerComponent, PageHeaderComponent, StatusChipComponent],
  templateUrl: './customer-notifications.component.html',
  styles: [`
    .grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}.card{padding:18px;background:var(--wb-surface);border:1px solid var(--wb-border);border-radius:var(--wb-radius-md)}
    .card h2{margin:0 0 6px}.muted,.hint{color:var(--wb-text-secondary);font-size:12px}.toggles{display:grid;gap:12px;margin-top:16px}.full{width:100%}.templates{margin-top:14px}.actions{display:flex;flex-wrap:wrap;gap:10px;margin-top:14px}
    .history{margin-top:16px;overflow:auto}.history table{width:100%;border-collapse:collapse}.history th,.history td{padding:10px;border-bottom:1px solid var(--wb-border);text-align:left;white-space:nowrap}.error{max-width:260px;white-space:normal;color:var(--wb-danger)}
    @media(max-width:800px){.grid{grid-template-columns:1fr}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomerNotificationsComponent {
  readonly settings = signal<CustomerNotificationSettings>({ ...defaults });
  readonly original = signal<CustomerNotificationSettings>({ ...defaults });
  readonly history = signal<CustomerNotificationHistory[]>([]);
  readonly status = signal<NotificationConfigurationStatus | null>(null);
  readonly saved = signal(false);
  readonly variables = '{{company_name}}, {{customer_name}}, {{invoice_no}}, {{invoice_date}}, {{total_amount}}, {{paid_amount}}, {{balance_amount}}, {{payment_method}}, {{currency}}, {{store_phone}}, {{store_address}}';
  constructor(private api: AdminApiService) { this.reload(); }
  reload() { this.api.customerNotificationSettings().subscribe(x => { this.settings.set({ ...x }); this.original.set({ ...x }); }); this.api.customerNotificationHistory().subscribe(x => this.history.set(x)); }
  save() { this.api.saveCustomerNotificationSettings(this.settings()).subscribe(() => { this.original.set({ ...this.settings() }); this.saved.set(true); }); }
  reset() { this.settings.set({ ...this.original() }); this.saved.set(false); }
  check() { this.api.customerNotificationConfigurationStatus().subscribe(x => this.status.set(x)); }
  retry(id: string) { this.api.retryCustomerNotification(id).subscribe(() => this.reload()); }
}
