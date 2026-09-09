import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { featureGuard } from './feature.guard';

describe('featureGuard', () => {
  it('does not apply retailer tenant features to an explicit platform route', () => {
    const route = { data: { platform: true, role: 'ApplicationOwner' } } as unknown as ActivatedRouteSnapshot;
    const state = { url: '/admin/demo-requests' } as RouterStateSnapshot;

    const result = TestBed.runInInjectionContext(() => featureGuard(route, state));

    expect(result).toBeTrue();
  });
});
