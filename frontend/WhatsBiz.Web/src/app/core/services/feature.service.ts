import { HttpClient } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { Observable, catchError, map, of, shareReplay, tap } from 'rxjs';
import { CurrentUserService } from './current-user.service';

export interface FeatureAccessState {
  featureId: string; featureKey: string; featureName: string; featureType: 'VERSION' | 'MODULE';
  parentFeatureKey?: string; version: string; sortOrder: number; configuredEnabled: boolean;
  effectiveEnabled: boolean; disabledReason?: string; subscriptionAllowed: boolean; globalAllowed: boolean;
  dependencies: string[];
}
export interface TenantFeatureConfiguration { tenantId: string; tenantName: string; planKey?: string; planName?: string; features: FeatureAccessState[]; }
export interface FeatureTenantSummary { tenantId: string; tenantKey: string; tenantName: string; planKey?: string; planName?: string; }

@Injectable({ providedIn: 'root' })
export class FeatureService {
  private readonly configurationState = signal<TenantFeatureConfiguration | null>(null);
  private request?: Observable<TenantFeatureConfiguration | null>;
  readonly configuration = this.configurationState.asReadonly();
  readonly effective = computed(() => Object.fromEntries((this.configurationState()?.features ?? []).map(x => [x.featureKey, x.effectiveEnabled])));
  constructor(private readonly http: HttpClient, private readonly currentUser: CurrentUserService) {}
  hasFeature(featureKey: string): boolean {
    const loaded = this.configurationState()?.features.find(x => x.featureKey === featureKey)?.effectiveEnabled;
    return loaded ?? this.currentUser.user()?.features?.[featureKey] === true;
  }
  load(force = false): Observable<TenantFeatureConfiguration | null> {
    if (!force && this.configurationState()) return of(this.configurationState());
    if (!force && this.request) return this.request;
    this.request = this.http.get<TenantFeatureConfiguration>('/api/features/effective').pipe(
      tap(x => this.configurationState.set(x)), map(x => x as TenantFeatureConfiguration | null), catchError(() => of(null)),
      shareReplay({ bufferSize: 1, refCount: false }));
    return this.request;
  }
  refresh(): Observable<TenantFeatureConfiguration | null> { this.request = undefined; return this.load(true); }
  tenants(): Observable<FeatureTenantSummary[]> { return this.http.get<FeatureTenantSummary[]>('/api/features/administration/tenants'); }
  tenant(tenantId: string): Observable<TenantFeatureConfiguration> { return this.http.get<TenantFeatureConfiguration>(`/api/features/administration/tenants/${tenantId}`); }
  update(tenantId: string, updates: { featureKey: string; configuredEnabled: boolean }[]): Observable<TenantFeatureConfiguration> {
    return this.http.put<TenantFeatureConfiguration>(`/api/features/administration/tenants/${tenantId}`, updates).pipe(
      tap(x => { if (x.tenantId === this.currentUser.user()?.tenantId) this.configurationState.set(x); }));
  }
  requiredFeature(url: string): string | undefined {
    if (url.startsWith('/admin/features')) return undefined;
    const routes: [string, string][] = [
      ['/admin/whatsapp-deliveries','COMMERCE_ORDERS'], ['/admin/whatsapp-demo','WHATSAPP_COMMERCE_DEMO'], ['/admin/whatsapp','WHATSAPP_CONFIGURATION'],
      ['/products/collections','COMMERCE_COLLECTIONS'], ['/dashboard','DASHBOARD'], ['/pos','POS'], ['/products','PRODUCTS'],
      ['/product-categories','PRODUCTS'], ['/brands','PRODUCTS'], ['/units','PRODUCTS'], ['/customers','CUSTOMERS'], ['/customer-groups','CUSTOMERS'],
      ['/suppliers','SUPPLIERS'], ['/purchases','PURCHASE'], ['/inventory','INVENTORY'], ['/finance','FINANCE'], ['/reports','REPORTS'],
      ['/gst','GST'], ['/analytics','ANALYTICS'], ['/print','PRINTING'], ['/printing','PRINTING'], ['/warehouses','WAREHOUSES'], ['/warehouse-types','WAREHOUSES'],
      ['/admin/users','USERS_ROLES'], ['/admin/roles','USERS_ROLES'], ['/admin','ADMINISTRATION']
    ];
    return routes.find(([prefix]) => url.startsWith(prefix))?.[1];
  }
}
