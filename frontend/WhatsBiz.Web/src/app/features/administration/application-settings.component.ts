import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { AdminApiService, Setting } from './admin-api.service';
@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatTabsModule,
    PageContainerComponent,
    PageHeaderComponent,
    StatusChipComponent,
  ],
  templateUrl: './application-settings.component.html',
  styles: [
    `
      .settings-card {
        margin-top: 14px;
        padding: 18px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
      }
      .settings-card header {
        display: flex;
        margin-bottom: 18px;
        align-items: center;
        gap: 10px;
      }
      .settings-card header > .material-symbols-rounded {
        display: grid;
        width: 42px;
        height: 42px;
        color: var(--wb-primary);
        background: var(--wb-primary-soft);
        border-radius: 10px;
        place-items: center;
      }
      h2,
      p {
        margin: 0;
      }
      .settings-card p,
      footer span {
        color: var(--wb-text-secondary);
        font-size: 12px;
      }
      .settings-grid {
        display: grid;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 12px;
      }
      .empty {
        display: grid;
        min-height: 280px;
        color: var(--wb-text-secondary);
        place-content: center;
        text-align: center;
      }
      .empty .material-symbols-rounded {
        margin: auto;
        color: var(--wb-primary);
        font-size: 44px;
      }
      footer {
        position: sticky;
        bottom: 0;
        z-index: 2;
        display: flex;
        margin-top: 14px;
        padding: 12px 16px;
        background: var(--wb-surface);
        border: 1px solid var(--wb-border);
        border-radius: var(--wb-radius-md);
        box-shadow: var(--wb-shadow-md);
        align-items: center;
        justify-content: space-between;
      }
      @media (max-width: 700px) {
        .settings-grid {
          grid-template-columns: 1fr;
        }
        footer span {
          display: none;
        }
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationSettingsComponent {
  readonly items = signal<Setting[]>([]);
  private readonly posPaymentDefaults: Setting[] = [
    { key: 'POS_UPI_ID', value: '', dataType: 'STRING', category: 'POS Payments' },
    { key: 'POS_UPI_PAYEE_NAME', value: '', dataType: 'STRING', category: 'POS Payments' },
  ];
  title = 'Application Settings';
  constructor(
    private api: AdminApiService,
    route: ActivatedRoute,
  ) {
    this.title = route.snapshot.data['title'] ?? this.title;
    api.settings().subscribe((x) => {
      const settings = [...x];
      for (const item of this.posPaymentDefaults)
        if (!settings.some((existing) => existing.key === item.key)) settings.push({ ...item });
      this.items.set(settings);
    });
  }
  categories() {
    return [...new Set(this.items().map((x) => x.category))];
  }
  byCategory(x: string) {
    return this.items().filter((y) => y.category === x);
  }
  label(key: string) {
    return key
      .replaceAll('_', ' ')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/^./, (x) => x.toUpperCase());
  }
  save() {
    this.api.saveSettings(this.items()).subscribe();
  }
}
