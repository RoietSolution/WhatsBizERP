import { signal } from '@angular/core';
import { LandingRouteService } from './landing-route.service';
import { CurrentUser } from './jwt-storage.service';
import { CurrentUserService } from './current-user.service';
import { FeatureService } from './feature.service';

describe('LandingRouteService', () => {
  const user = (permissions: string[], features: Record<string, boolean>): CurrentUser => ({
    userId: 'user', tenantId: 'tenant', username: 'employee', email: 'employee@example.test',
    roles: ['Employee'], permissions, features, mustChangePassword: false,
  });
  const resolve = (value: CurrentUser) =>
    new LandingRouteService(
      { user: signal(value) } as unknown as CurrentUserService,
      { hasFeature: (key: string) => value.features[key] === true } as FeatureService,
    ).resolve();

  it('uses Dashboard when permission and feature are available', () =>
    expect(resolve(user(['dashboard.view'], { DASHBOARD: true }))).toBe('/dashboard'));

  it('sends a POS-only employee to New Sale', () =>
    expect(resolve(user(['pos.view', 'pos.create'], { POS: true }))).toBe('/pos'));

  it('sends a Reports-only employee to Reports', () =>
    expect(resolve(user(['gst.view'], { REPORTS: true }))).toBe('/reports'));

  it('uses sidebar order when multiple modules are available', () =>
    expect(resolve(user(['product.view', 'inventory.view'], { PRODUCTS: true, INVENTORY: true }))).toBe('/products'));

  it('falls back to Profile when no functional route is available', () =>
    expect(resolve(user([], {}))).toBe('/profile'));

  it('skips a permitted module when the tenant feature is disabled', () =>
    expect(resolve(user(['dashboard.view', 'pos.view', 'pos.create'], { DASHBOARD: false, POS: true }))).toBe('/pos'));
});
