import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PaymentsApiService } from './payments-api.service';

describe('PaymentsApiService',()=>{
  let api:PaymentsApiService;let http:HttpTestingController;
  beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});api=TestBed.inject(PaymentsApiService);http=TestBed.inject(HttpTestingController);});
  afterEach(()=>http.verify());
  it('sends no tenant id and preserves blank replacement secrets',()=>{api.saveRazorpay({keyId:'',keySecret:null,webhookSecret:null,isEnabled:true,isDefault:true,isTestMode:true}).subscribe();const request=http.expectOne('/api/payments/settings/razorpay');expect(request.request.method).toBe('PUT');expect(request.request.body).toEqual(jasmine.objectContaining({keySecret:null,webhookSecret:null}));expect(request.request.body.tenantId).toBeUndefined();request.flush({onlinePaymentEnabled:true,providers:[]});});
  it('uses the tenant-scoped verification route without a client tenant id',()=>{api.verify('payment-1','UPI123').subscribe();const request=http.expectOne('/api/payments/payment-1/verify-direct-upi');expect(request.request.body).toEqual({reference:'UPI123'});request.flush({});});
});
