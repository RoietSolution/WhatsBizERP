import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-application-owner-hub',
  imports: [RouterLink],
  template: `
    <section class="owner-page">
      <header><p>Platform administration</p><h1>Application Owner Center</h1><span>Manage application-wide configuration and retailer access from one restricted workspace.</span></header>
      <div class="owner-grid">
        @for (item of items; track item.route) {
          <a [routerLink]="item.route"><span class="material-symbols-rounded">{{ item.icon }}</span><div><strong>{{ item.title }}</strong><p>{{ item.description }}</p></div><span class="material-symbols-rounded arrow">arrow_forward</span></a>
        }
      </div>
    </section>`,
  styles: [`
    .owner-page{max-width:1100px;margin:auto;padding:28px}.owner-page header{margin-bottom:24px}.owner-page header p{margin:0;color:var(--wb-primary);font-weight:700;text-transform:uppercase;letter-spacing:.08em;font-size:.75rem}.owner-page h1{margin:6px 0 8px}.owner-page header span{color:var(--wb-text-secondary)}.owner-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}.owner-grid a{display:flex;align-items:flex-start;gap:14px;padding:20px;color:inherit;text-decoration:none;background:var(--wb-surface,#fff);border:1px solid var(--wb-border,#dde4eb);border-radius:14px}.owner-grid a:hover{border-color:var(--wb-primary)}.owner-grid a>span:first-child{color:var(--wb-primary);font-size:28px}.owner-grid strong{display:block}.owner-grid p{margin:6px 0 0;color:var(--wb-text-secondary);line-height:1.45}.arrow{margin-left:auto;font-size:20px}@media(max-width:700px){.owner-page{padding:18px}.owner-grid{grid-template-columns:1fr}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationOwnerHubComponent {
  readonly items = [
    { title: 'Tenant Features', description: 'Enroll retailers and control plan feature access.', icon: 'account_tree', route: '/admin/features' },
    { title: 'Demo Requests', description: 'Review website leads and manage their sales status.', icon: 'campaign', route: '/admin/demo-requests' },
    { title: 'WhatsApp Platform', description: 'Configure the shared Meta app and inspect retailer connections.', icon: 'hub', route: '/admin/whatsapp-platform' },
    { title: 'WhatsApp Business', description: 'Configure and validate a selected retailer WhatsApp connection.', icon: 'chat', route: '/application-owner/whatsapp-business' },
    { title: 'WhatsApp Ecommerce Demo', description: 'Run the commerce preview explicitly for a selected retailer.', icon: 'forum', route: '/application-owner/whatsapp-demo' },
    { title: 'Backup & Restore', description: 'Protect and recover the application database.', icon: 'backup', route: '/admin/backup' },
    { title: 'Audit Log', description: 'Review application-wide configuration and security events.', icon: 'history', route: '/admin/audit' },
    { title: 'Login History', description: 'Monitor successful and failed account access.', icon: 'login', route: '/admin/login-history' },
    { title: 'System Logs & Errors', description: 'Search API logs by date, page, severity, status, and reference ID.', icon: 'troubleshoot', route: '/admin/system-logs' },
  ];
}
