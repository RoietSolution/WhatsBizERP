import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { FeatureAccessState, FeatureService, FeatureTenantSummary, TenantFeatureConfiguration } from '../../core/services/feature.service';

@Component({
  selector: 'app-feature-management',
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatSlideToggleModule],
  templateUrl: './feature-management.component.html',
  styleUrl: './feature-management.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeatureManagementComponent implements OnInit {
  private readonly api = inject(FeatureService);
  readonly tenants = signal<FeatureTenantSummary[]>([]);
  readonly configuration = signal<TenantFeatureConfiguration | null>(null);
  readonly selectedTenantId = signal('');
  readonly loading = signal(false); readonly saving = signal(false); readonly message = signal(''); readonly error = signal('');
  private original = new Map<string, boolean>();
  readonly versions = computed(() => (this.configuration()?.features ?? []).filter(x => x.featureType === 'VERSION'));
  ngOnInit(): void {
    this.loading.set(true);
    this.api.tenants().subscribe({ next: rows => { this.tenants.set(rows); if (rows[0]) this.select(rows[0].tenantId); else this.loading.set(false); }, error: () => { this.error.set('Tenant feature administration is available only to a SystemAdministrator.'); this.loading.set(false); } });
  }
  select(tenantId: string): void {
    this.selectedTenantId.set(tenantId); this.loading.set(true); this.error.set(''); this.message.set('');
    this.api.tenant(tenantId).subscribe({ next: x => { this.configuration.set(x); this.original = new Map(x.features.map(f => [f.featureKey, f.configuredEnabled])); this.loading.set(false); }, error: () => { this.error.set('Unable to load tenant feature configuration.'); this.loading.set(false); } });
  }
  children(version: FeatureAccessState): FeatureAccessState[] { return (this.configuration()?.features ?? []).filter(x => x.parentFeatureKey === version.featureKey); }
  save(): void {
    const config = this.configuration(); if (!config) return;
    const updates = config.features.filter(x => this.original.get(x.featureKey) !== x.configuredEnabled).map(x => ({ featureKey: x.featureKey, configuredEnabled: x.configuredEnabled }));
    if (!updates.length) { this.message.set('No configuration changes to save.'); return; }
    this.saving.set(true); this.error.set(''); this.message.set('');
    this.api.update(config.tenantId, updates).subscribe({ next: x => { this.configuration.set(x); this.original = new Map(x.features.map(f => [f.featureKey, f.configuredEnabled])); this.message.set('Feature configuration saved. Effective access has been refreshed.'); this.saving.set(false); }, error: e => { this.error.set(e?.error?.detail ?? 'Feature configuration could not be saved.'); this.saving.set(false); } });
  }
  reason(feature: FeatureAccessState): string {
    if (feature.effectiveEnabled) return 'Available';
    const value = feature.disabledReason ?? 'Unavailable';
    if (value === 'PARENT_VERSION_DISABLED') return `${feature.version} is disabled`;
    if (value === 'SUBSCRIPTION_NOT_ENTITLED') return 'Not included in the active subscription';
    if (value === 'GLOBAL_FEATURE_DISABLED') return 'Disabled by the global emergency switch';
    if (value.startsWith('DEPENDENCY_DISABLED:')) return `Required feature ${value.split(':')[1]} is unavailable`;
    return 'Disabled for this retailer';
  }
}
