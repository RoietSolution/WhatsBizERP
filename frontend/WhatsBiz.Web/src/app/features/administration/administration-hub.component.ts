import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { OperationsWorkspaceComponent } from '../../shared/components/operations-workspace/operations-workspace.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
type SettingLink = {
  title: string;
  description: string;
  icon: string;
  route?: string;
  planned?: boolean;
};
type SettingGroup = { title: string; icon: string; items: SettingLink[] };
@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    OperationsWorkspaceComponent,
    StatusChipComponent,
  ],
  templateUrl: './administration-hub.component.html',
  styles: [
    `
      .search {
        width: min(420px, 100%);
      }
      .search mat-form-field {
        width: 100%;
      }
      .category-nav {
        display: flex;
        overflow: auto;
        margin-bottom: 14px;
        padding: 6px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        gap: 4px;
      }
      .category-nav button {
        display: flex;
        min-width: max-content;
        padding: 9px 12px;
        color: var(--wb-text-secondary);
        background: transparent;
        border: 0;
        border-radius: 7px;
        align-items: center;
        gap: 6px;
        font: inherit;
        cursor: pointer;
      }
      .category-nav button:hover,
      .category-nav button.active {
        color: var(--wb-primary);
        background: var(--wb-primary-soft);
      }
      .category-nav .material-symbols-rounded {
        font-size: 19px;
      }
      .settings-grid {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 10px;
      }
      .settings-grid > a,
      .settings-grid > article {
        display: grid;
        grid-template-columns: auto 1fr auto;
        min-height: 92px;
        padding: 16px;
        color: var(--wb-text-primary);
        text-decoration: none;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        align-items: start;
        gap: 12px;
        transition: 200ms;
      }
      .settings-grid > a:hover,
      .settings-grid > a:focus-visible,
      .settings-grid > article:focus-visible {
        border-color: var(--wb-primary);
        box-shadow: var(--wb-shadow-md);
        transform: translateY(-2px);
        outline: none;
      }
      .setting-icon {
        display: grid;
        width: 40px;
        height: 40px;
        color: var(--wb-primary);
        background: var(--wb-primary-soft);
        border-radius: 10px;
        place-items: center;
      }
      .settings-grid strong {
        display: block;
      }
      .settings-grid p,
      .context p {
        margin: 4px 0;
        color: var(--wb-text-secondary);
        font-size: 12px;
      }
      .arrow {
        color: var(--wb-text-secondary);
      }
      .context {
        display: flex;
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        flex-direction: column;
        align-items: flex-start;
        gap: 8px;
      }
      .context h3 {
        margin: 0;
      }
      .hero-icon {
        color: var(--wb-primary);
        font-size: 36px;
      }
      @media (max-width: 767px) {
        .settings-grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdministrationHubComponent {
  query = '';
  readonly category = signal('Organization');
  readonly selected = signal<SettingLink | null>(null);
  readonly summaries = [
    {
      label: 'Organization',
      value: 'Configured',
      subtitle: 'Company and branches',
      icon: 'business',
      tone: 'primary' as const,
    },
    {
      label: 'System Health',
      value: 'Online',
      subtitle: 'Application available',
      icon: 'monitor_heart',
      tone: 'success' as const,
    },
    {
      label: 'Backup',
      value: 'Available',
      subtitle: 'Manual and restore',
      icon: 'backup',
      tone: 'info' as const,
    },
    {
      label: 'Audit Logs',
      value: 'Active',
      subtitle: 'Activity monitoring',
      icon: 'policy',
      tone: 'warning' as const,
    },
  ];
  readonly groups: SettingGroup[] = [
    {
      title: 'Organization',
      icon: 'business',
      items: [
        {
          title: 'Company Profile',
          description: 'Legal, tax, contact, banking, and invoice identity.',
          icon: 'domain',
          route: '/admin/company',
        },
        {
          title: 'Branches',
          description: 'Manage company branches and operational locations.',
          icon: 'lan',
          route: '/admin/branches',
        },
        {
          title: 'Warehouses',
          description: 'Configure storage locations and warehouse operations.',
          icon: 'warehouse',
          route: '/warehouses',
        },
        {
          title: 'Financial Years',
          description: 'Manage accounting periods and active financial year.',
          icon: 'calendar_month',
          route: '/admin/financial-years',
        },
        {
          title: 'Currencies',
          description: 'Currency configuration and display preferences.',
          icon: 'currency_exchange',
          planned: true,
        },
        {
          title: 'Number Series',
          description: 'Document numbering and sequence configuration.',
          icon: 'format_list_numbered',
          planned: true,
        },
      ],
    },
    {
      title: 'Security',
      icon: 'shield',
      items: [
        {
          title: 'Users',
          description: 'User accounts, access, and account status.',
          icon: 'person',
          route: '/admin/users',
        },
        {
          title: 'Roles & Permissions',
          description: 'Role-based access and permission assignments.',
          icon: 'admin_panel_settings',
          route: '/admin/roles',
        },
        {
          title: 'Password Policy',
          description: 'Password strength and expiration requirements.',
          icon: 'password',
          planned: true,
        },
        {
          title: 'Session Policy',
          description: 'Session timeout and concurrent-login settings.',
          icon: 'timer',
          planned: true,
        },
        {
          title: 'Login History',
          description: 'Successful and failed sign-in activity.',
          icon: 'login',
          route: '/admin/login-history',
        },
      ],
    },
    {
      title: 'System',
      icon: 'settings',
      items: [
        {
          title: 'Application Settings',
          description: 'General system and operational configuration.',
          icon: 'tune',
          route: '/admin/settings',
        },
        {
          title: 'GST Settings',
          description: 'GST registration and compliance configuration.',
          icon: 'percent',
          route: '/gst/configuration',
        },
        {
          title: 'Theme & Language',
          description: 'User interface, language, and time-zone preferences.',
          icon: 'palette',
          route: '/admin/preferences',
        },
      ],
    },
    {
      title: 'Communication',
      icon: 'forum',
      items: [
        {
          title: 'Customer Notifications',
          description: 'Post-sale WhatsApp, SMS, templates, delivery history, and retry.',
          icon: 'mark_chat_read',
          route: '/admin/customer-notifications',
        },
        {
          title: 'Email / SMTP',
          description: 'Outgoing email and SMTP provider settings.',
          icon: 'mail',
          planned: true,
        },
        {
          title: 'SMS Provider',
          description: 'SMS gateway and messaging credentials.',
          icon: 'sms',
          planned: true,
        },
        {
          title: 'Notification Templates',
          description: 'Operational notification message templates.',
          icon: 'notifications',
          planned: true,
        },
        {
          title: 'WhatsApp',
          description: 'WhatsApp Business integration placeholder.',
          icon: 'chat',
          planned: true,
        },
      ],
    },
    {
      title: 'Printing',
      icon: 'print',
      items: [
        {
          title: 'Printer Configuration',
          description: 'Printers, paper sizes, and default print behavior.',
          icon: 'print',
          route: '/admin/printers',
        },
        {
          title: 'Receipt & Invoice Templates',
          description: 'Manage document templates and layouts.',
          icon: 'receipt_long',
          route: '/printing/templates',
        },
        {
          title: 'Label Template',
          description: 'Barcode and inventory label design.',
          icon: 'label',
          route: '/printing/labels',
        },
      ],
    },
    {
      title: 'Backup',
      icon: 'backup',
      items: [
        {
          title: 'Backup Center',
          description: 'Backup history, manual backup, and storage status.',
          icon: 'cloud_upload',
          route: '/admin/backup',
        },
        {
          title: 'Restore Center',
          description: 'Validate verified backups for planned restoration.',
          icon: 'restore',
          route: '/admin/restore',
        },
        {
          title: 'Scheduled Backup',
          description: 'Automated backup schedules and retention.',
          icon: 'schedule',
          planned: true,
        },
      ],
    },
    {
      title: 'Monitoring',
      icon: 'monitor_heart',
      items: [
        {
          title: 'Audit Logs',
          description: 'User activity and configuration-change history.',
          icon: 'history',
          route: '/admin/audit',
        },
        {
          title: 'Login History',
          description: 'Authentication and security events.',
          icon: 'manage_accounts',
          route: '/admin/login-history',
        },
        {
          title: 'System Health',
          description: 'Application, database, API, disk, and storage status.',
          icon: 'monitor_heart',
          planned: true,
        },
      ],
    },
    {
      title: 'Personalization',
      icon: 'palette',
      items: [
        {
          title: 'User Preferences',
          description: 'Theme, language, time zone, and personal defaults.',
          icon: 'tune',
          route: '/admin/preferences',
        },
      ],
    },
  ];
  readonly visible = computed(() => {
    const q = this.query.trim().toLowerCase(),
      group = this.groups.find((x) => x.title === this.category()) ?? this.groups[0];
    if (!q) return group.items;
    return this.groups
      .flatMap((x) => x.items)
      .filter((x) => `${x.title} ${x.description}`.toLowerCase().includes(q));
  });
}
