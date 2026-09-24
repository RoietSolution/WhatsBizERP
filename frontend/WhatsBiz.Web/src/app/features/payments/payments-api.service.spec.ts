import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PaymentsApiService } from './payments-api.service';

describe('PaymentsApiService',()=>{
  let api:PaymentsApiService;let http:HttpTestingController;
  beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});api=TestBed.inject(PaymentsApiService);http=TestBed.inject(HttpTestingController);});
  afterEach(()=>http.verify());
  it('uses the owner tenant route and preserves blank replacement secrets',()=>{api.saveRazorpay('tenant-1',{keyId:'',keySecret:null,webhookSecret:null,isEnabled:true,isDefault:true,isTestMode:true}).subscribe();const request=http.expectOne('/api/payments/administration/tenants/tenant-1/settings/razorpay');expect(request.request.method).toBe('PUT');expect(request.request.body).toEqual(jasmine.objectContaining({keySecret:null,webhookSecret:null}));expect(request.request.body.tenantId).toBeUndefined();request.flush({onlinePaymentEnabled:true,providers:[]});});
  it('uses the tenant-scoped verification route without a client tenant id',()=>{api.verify('payment-1','UPI123').subscribe();const request=http.expectOne('/api/payments/payment-1/verify-direct-upi');expect(request.request.body).toEqual({reference:'UPI123'});request.flush({});});
  it('keeps retailer listing tenant-context based while sending server filters',()=>{api.list({dateFrom:'2026-09-01',status:'PAID',provider:'RAZORPAY',pageNumber:2,pageSize:25}).subscribe();const request=http.expectOne(x=>x.url==='/api/payments');expect(request.request.params.get('tenantId')).toBeNull();expect(request.request.params.get('status')).toBe('PAID');expect(request.request.params.get('pageNumber')).toBe('2');request.flush({items:[],totalCount:0,pageNumber:2,pageSize:25});});
  it('uses the platform route for cross-retailer filters',()=>{api.list({tenantId:'tenant-2',dateTo:'2026-09-30',pageNumber:1,pageSize:25},true).subscribe();const request=http.expectOne(x=>x.url==='/api/payments/administration');expect(request.request.params.get('tenantId')).toBe('tenant-2');expect(request.request.params.get('dateTo')).toBe('2026-09-30');request.flush({items:[],totalCount:0,pageNumber:1,pageSize:25});});
});
