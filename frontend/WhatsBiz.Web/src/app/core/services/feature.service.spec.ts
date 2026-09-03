import { HttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { CurrentUserService } from './current-user.service';
import { FeatureService } from './feature.service';

describe('FeatureService', () => {
  function service(features: Record<string, boolean>) {
    return new FeatureService({} as HttpClient, { user: signal({ features }) } as unknown as CurrentUserService);
  }
  it('maps direct V1 and V2 URLs to their authoritative feature', () => {
    const value = service({});
    expect(value.requiredFeature('/pos/history')).toBe('POS');
    expect(value.requiredFeature('/inventory/balance')).toBe('INVENTORY');
    expect(value.requiredFeature('/admin/whatsapp-demo')).toBe('WHATSAPP_COMMERCE_DEMO');
    expect(value.requiredFeature('/products/collections/1/edit')).toBe('COMMERCE_COLLECTIONS');
    expect(value.requiredFeature('/admin/company')).toBe('ADMINISTRATION');
    expect(value.requiredFeature('/admin/features')).toBeUndefined();
  });
  it('uses effective backend feature state supplied with the authenticated session', () => {
    const value = service({ V1: true, POS: false, WHATSAPP_COMMERCE: false });
    expect(value.hasFeature('V1')).toBeTrue();
    expect(value.hasFeature('POS')).toBeFalse();
    expect(value.hasFeature('WHATSAPP_COMMERCE')).toBeFalse();
  });
  it('clears tenant feature state when the authenticated session changes', () => {
    const current = signal({ features: { DASHBOARD: false } });
    const http = jasmine.createSpyObj<HttpClient>('HttpClient', ['get']);
    http.get.and.returnValue(
      of({
        tenantId: 'tenant-1',
        tenantName: 'Retailer',
        features: [
          {
            featureId: 'dashboard',
            featureKey: 'DASHBOARD',
            featureName: 'Dashboard',
            featureType: 'MODULE',
            version: 'V1',
            sortOrder: 1,
            configuredEnabled: true,
            effectiveEnabled: true,
            subscriptionAllowed: true,
            globalAllowed: true,
            dependencies: [],
          },
        ],
      }),
    );
    const value = new FeatureService(
      http,
      { user: current } as unknown as CurrentUserService,
    );
    value.load().subscribe();
    expect(value.hasFeature('DASHBOARD')).toBeTrue();

    value.reset();

    expect(value.configuration()).toBeNull();
    expect(value.hasFeature('DASHBOARD')).toBeFalse();
  });
});
