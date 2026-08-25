import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WhatsAppApiService } from './whatsapp-api.service';

describe('WhatsAppApiService contacts',()=>{
  let api:WhatsAppApiService;let http:HttpTestingController;
  beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});api=TestBed.inject(WhatsAppApiService);http=TestBed.inject(HttpTestingController);});
  afterEach(()=>http.verify());
  it('lists contacts without accepting tenant identity from the client',()=>{api.contacts({search:'amit',status:'NEW',pageNumber:1,pageSize:20}).subscribe();const request=http.expectOne(x=>x.url==='/api/whatsapp-contacts');expect(request.request.method).toBe('GET');expect(request.request.params.get('search')).toBe('amit');expect(request.request.params.has('tenantId')).toBeFalse();request.flush({items:[],totalCount:0,newCount:0,matchedCount:0,convertedCount:0,pageNumber:1,pageSize:20});});
  it('links only contact and customer identifiers',()=>{api.linkContact('contact-a','customer-a').subscribe();const request=http.expectOne('/api/whatsapp-contacts/contact-a/link');expect(request.request.method).toBe('POST');expect(request.request.body).toEqual({customerId:'customer-a'});request.flush({});});
});
