import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WhatsAppUsageBillingApiService } from './whatsapp-usage-billing-api.service';

describe('WhatsAppUsageBillingApiService',()=>{
  let api:WhatsAppUsageBillingApiService;let http:HttpTestingController;
  beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});api=TestBed.inject(WhatsAppUsageBillingApiService);http=TestBed.inject(HttpTestingController);});
  afterEach(()=>http.verify());
  it('uses the tenant-implicit protected summary URL',()=>{
    api.summary(2026,9).subscribe();
    const request=http.expectOne(r=>r.url==='/api/whatsapp/usage/summary');
    expect(request.request.method).toBe('GET');expect(request.request.params.get('year')).toBe('2026');expect(request.request.params.get('month')).toBe('9');
    expect(request.request.params.has('tenantId')).toBeFalse();request.flush({});
  });
});
