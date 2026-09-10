import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { WhatsAppCommerceDemoApiService } from './whatsapp-commerce-demo-api.service';

describe('WhatsAppCommerceDemoApiService account scope', () => {
  let api:WhatsAppCommerceDemoApiService;
  let http:HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});
    api=TestBed.inject(WhatsAppCommerceDemoApiService);
    http=TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses the tenantless URL contract for a retailer so the backend resolves the JWT tenant', () => {
    api.setup().subscribe();
    http.expectOne('/api/whatsapp-commerce/demo/setup').flush({});
  });

  it('uses the explicit platform target only when supplied by an application owner', () => {
    api.setup(undefined,'tenant-a').subscribe();
    http.expectOne('/api/whatsapp-commerce/administration/tenants/tenant-a/demo/setup').flush({});
  });
});
