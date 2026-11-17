import { HttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { NEVER } from 'rxjs';
import { GuestVisitorAnalyticsComponent } from './guest-visitor-analytics.component';

describe('GuestVisitorAnalyticsComponent', () => {
  let fixture: ComponentFixture<GuestVisitorAnalyticsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GuestVisitorAnalyticsComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { data: { owner: false } } } },
        { provide: HttpClient, useValue: { get: () => NEVER } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(GuestVisitorAnalyticsComponent);
    fixture.detectChanges();
  });

  it('renders unavailable completed-order attribution as text, not through the number pipe', () => {
    fixture.componentInstance.report.set({
      visitors: 0, sessions: 0, engagedSeconds: 0, pageViews: 0, productViews: 0,
      categoryViews: 0, searches: 0, addToCart: 0, checkoutStarted: 0, completedOrders: null,
      devices: {}, browsers: {}, operatingSystems: {}, trafficSources: {}, mostViewedProducts: [],
    });
    fixture.componentInstance.loading.set(false);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Not enabled');
    expect(fixture.nativeElement.textContent).toContain('Unique visitors');
  });

  it('does not send malformed metric values to Angular number formatting', () => {
    const metrics = fixture.componentInstance.metrics({
      visitors: Number.NaN, sessions: 0, engagedSeconds: 0, pageViews: 0, productViews: 0,
      categoryViews: 0, searches: 0, addToCart: 0, checkoutStarted: 0, completedOrders: null,
      devices: {}, browsers: {}, operatingSystems: {}, trafficSources: {}, mostViewedProducts: [],
    });

    expect(metrics[0]).toEqual({ label: 'Unique visitors', value: null, text: 'Unavailable' });
  });

  it('explains an analytics gateway timeout and distinguishes an empty date range', () => {
    expect(fixture.componentInstance.reportErrorMessage(504)).toContain('timed out');
    expect(fixture.componentInstance.reportErrorMessage(400)).toContain('93 days');
  });

  it('distinguishes no consented sessions from consented sessions without persisted events', () => {
    const report = {
      visitors: 0, sessions: 0, engagedSeconds: 0, pageViews: 0, productViews: 0,
      categoryViews: 0, searches: 0, addToCart: 0, checkoutStarted: 0, completedOrders: null,
      devices: {}, browsers: {}, operatingSystems: {}, trafficSources: {}, mostViewedProducts: [],
      availability: { activeRetailers: 1, featureEnabledRetailers: 1, reviewedPolicyRetailers: 1,
        eligibleRetailers: 1, collectionEnabled: true, retentionCleanupEnabled: true,
        retentionWorkerHealthy: true, selectedRetailerActive: null, selectedFeatureEnabled: null,
        selectedRetentionPolicyReviewed: null },
    };
    expect(fixture.componentInstance.availabilityMessage(report)).toContain('not opted in');
    expect(fixture.componentInstance.availabilityMessage({ ...report, sessions: 1, visitors: 1 }))
      .toContain('no analytics events were persisted');
  });
});
