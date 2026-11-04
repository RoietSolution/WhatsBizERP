import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { FeatureAccessState, FeatureService, FeatureTenantSummary, TenantCapacitySummary, TenantEnrollmentResult, TenantFeatureConfiguration } from '../../core/services/feature.service';
import { CustomerReferralAdministration, ReferralConfiguration, ReferralRewardsApiService, QualificationType } from './referral-rewards-api.service';

@Component({
  selector: 'app-feature-management',
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatSlideToggleModule],
  templateUrl: './feature-management.component.html',
  styleUrl: './feature-management.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeatureManagementComponent implements OnInit {
  private readonly api = inject(FeatureService);
  private readonly referralApi = inject(ReferralRewardsApiService);
  readonly tenants = signal<FeatureTenantSummary[]>([]);
  readonly configuration = signal<TenantFeatureConfiguration | null>(null);
  readonly capacity = signal<TenantCapacitySummary | null>(null);
  userLimit: number | null = null; branchLimit: number | null = null; usersUnlimited = false; branchesUnlimited = false;
  readonly selectedTenantId = signal('');
  displayName = '';
  readonly loading = signal(false); readonly saving = signal(false); readonly importing = signal(false); readonly message = signal(''); readonly error = signal('');
  readonly referral = signal<CustomerReferralAdministration | null>(null);
  readonly referralSaving = signal(false);
  readonly qualificationTypes: QualificationType[] = ['CUSTOMER_REGISTERED','FIRST_ORDER_PLACED','FIRST_PAID_ORDER','FIRST_COMPLETED_ORDER','FIRST_COMPLETED_ORDER_MIN_AMOUNT','MANUAL_APPROVAL'];
  private original = new Map<string, boolean>();
  readonly versions = computed(() => (this.configuration()?.features ?? []).filter(x => x.featureType === 'VERSION'));
  ngOnInit(): void {
    this.loading.set(true);
    this.api.tenants().subscribe({ next: rows => { this.tenants.set(rows); if (rows[0]) this.select(rows[0].tenantId); else this.loading.set(false); }, error: () => { this.error.set('Tenant feature administration is available only to an ApplicationOwner.'); this.loading.set(false); } });
  }
  select(tenantId: string): void {
    this.selectedTenantId.set(tenantId); this.loading.set(true); this.error.set(''); this.message.set('');
    this.referral.set(null);
    this.referralApi.administrationConfiguration(tenantId).subscribe({ next: x => this.referral.set(x), error: () => this.error.set('Unable to load customer referral configuration.') });
    this.api.tenant(tenantId).subscribe({ next: x => { this.configuration.set(x); this.displayName = x.displayName ?? ''; this.original = new Map(x.features.map(f => [f.featureKey, f.configuredEnabled])); this.loading.set(false); }, error: () => { this.error.set('Unable to load tenant feature configuration.'); this.loading.set(false); } });
    this.api.capacity(tenantId).subscribe({next:x=>{this.capacity.set(x);this.userLimit=x.users.limit??null;this.branchLimit=x.branches.limit??null;this.usersUnlimited=x.users.unlimited||!x.users.configured;this.branchesUnlimited=x.branches.unlimited||!x.branches.configured;},error:()=>this.error.set('Unable to load tenant capacity.')});
  }
  saveDisplayName(): void {
    const config = this.configuration(); if (!config) return;
    this.saving.set(true); this.error.set(''); this.message.set('');
    this.api.updateDisplayName(config.tenantId, this.displayName.trim()).subscribe({ next: x => { this.configuration.set(x); this.displayName = x.displayName ?? ''; this.tenants.update(rows => rows.map(row => row.tenantId === x.tenantId ? { ...row, displayName: x.displayName } : row)); this.saving.set(false); this.message.set('Customer-facing Display Name saved.'); }, error: e => { this.saving.set(false); this.error.set(e?.error?.detail ?? 'Display Name could not be saved.'); } });
  }
  children(version: FeatureAccessState): FeatureAccessState[] { return (this.configuration()?.features ?? []).filter(x => x.parentFeatureKey === version.featureKey); }
  storefrontHint(key: string): string {
    if(key==='STOREFRONT_SCHEDULED_SELLING')return 'Disabling suspends booking-window restrictions; products follow normal active/stock rules. Saved schedules are retained.';
    if(key==='STOREFRONT_POPUP_CAMPAIGNS')return 'Disabling stops public campaign delivery without deleting saved campaigns.';
    if(key==='STOREFRONT_NOTIFICATIONS')return 'Optional restock messages and retailer order alerts only. Essential customer order, payment and security messages are unaffected.';
    return '';
  }
  save(): void {
    const config = this.configuration(); if (!config) return;
    const updates = config.features.filter(x => this.original.get(x.featureKey) !== x.configuredEnabled).map(x => ({ featureKey: x.featureKey, configuredEnabled: x.configuredEnabled }));
    if (!updates.length) { this.message.set('No configuration changes to save.'); return; }
    this.saving.set(true); this.error.set(''); this.message.set('');
    this.api.update(config.tenantId, updates).subscribe({ next: x => { this.configuration.set(x); this.original = new Map(x.features.map(f => [f.featureKey, f.configuredEnabled])); this.message.set('Feature configuration saved. Effective access has been refreshed.'); this.saving.set(false); }, error: e => { this.error.set(e?.error?.detail ?? 'Feature configuration could not be saved.'); this.saving.set(false); } });
  }
  updateReferral<K extends keyof ReferralConfiguration>(key: K, value: ReferralConfiguration[K]): void {
    const current = this.referral();
    if (!current) return;
    this.referral.set({ ...current, configuration: { ...current.configuration, [key]: value } });
  }
  saveCustomerReferralConfiguration(): void {
    const selected = this.selectedTenantId();
    const value = this.referral();
    if (!selected || !value || !value.featureEntitled) return;
    this.referralSaving.set(true); this.error.set(''); this.message.set('');
    this.referralApi.saveAdministrationConfiguration(selected, value.configuration).subscribe({
      next: result => { this.referral.set(result); this.referralSaving.set(false); this.message.set('Customer referral rewards configuration saved.'); },
      error: e => { this.referralSaving.set(false); this.error.set(e?.error?.detail ?? 'Customer referral configuration could not be saved.'); }
    });
  }
  saveCapacity(): void {
    if ((!this.usersUnlimited && (!this.userLimit || this.userLimit < 1)) || (!this.branchesUnlimited && (!this.branchLimit || this.branchLimit < 1))) { this.error.set('Finite limits must be at least 1.'); return; }
    this.saving.set(true);this.error.set('');this.message.set('');
    this.api.updateCapacity(this.selectedTenantId(),{configured:true,unlimited:this.usersUnlimited,limit:this.usersUnlimited?undefined:this.userLimit!},{configured:true,unlimited:this.branchesUnlimited,limit:this.branchesUnlimited?undefined:this.branchLimit!})
      .subscribe({next:x=>{this.capacity.set(x);this.saving.set(false);this.message.set('Tenant capacity saved.');},error:e=>{this.saving.set(false);this.error.set(e?.error?.detail??'Capacity could not be saved.');}});
  }
  downloadEnrollmentTemplate(): void {
    this.error.set('');
    this.api.enrollmentTemplate().subscribe({ next: file => { const url = URL.createObjectURL(file); const link = document.createElement('a'); link.href = url; link.download = 'tenant-enrollment-template.xlsx'; link.click(); URL.revokeObjectURL(url); }, error: () => this.error.set('Unable to download the tenant enrollment template.') });
  }
  importTenant(event: Event): void {
    const input = event.target as HTMLInputElement; const file = input.files?.[0]; if (!file) return;
    this.importing.set(true); this.error.set(''); this.message.set('');
    this.api.enrollTenant(file).subscribe({ next: result => this.enrollmentComplete(result), error: response => { this.error.set(response?.error?.detail ?? 'Tenant enrollment failed. No tenant was created.'); this.importing.set(false); input.value = ''; } });
  }
  private enrollmentComplete(result: TenantEnrollmentResult): void {
    this.message.set(`Tenant ${result.tenantName} and administrator ${result.administratorUsername} were created successfully.`);
    this.api.tenants().subscribe({ next: rows => { this.tenants.set(rows); this.select(result.tenantId); this.importing.set(false); }, error: () => { this.importing.set(false); } });
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
