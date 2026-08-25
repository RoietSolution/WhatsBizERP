import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ReferralRewardsApiService } from './referral-rewards-api.service';

describe('ReferralRewardsApiService',()=>{
 let api:ReferralRewardsApiService;let http:HttpTestingController;
 beforeEach(()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});api=TestBed.inject(ReferralRewardsApiService);http=TestBed.inject(HttpTestingController);});
 afterEach(()=>http.verify());
 it('always scopes wallet-facing referral code requests to the server route',()=>{api.code('customer-a').subscribe();const request=http.expectOne('/api/customer-referrals/customers/customer-a/code');expect(request.request.method).toBe('GET');request.flush({});});
 it('sends no tenant or reward values while requesting a customer code',()=>{api.code('customer-a').subscribe();const request=http.expectOne('/api/customer-referrals/customers/customer-a/code');expect(request.request.params.keys()).toEqual([]);request.flush({});});
 it('uses the protected configuration endpoint',()=>{api.configuration().subscribe();const request=http.expectOne('/api/customer-referrals/configuration');expect(request.request.method).toBe('GET');request.flush({});});
 it('encodes referral codes on the public resolution endpoint',()=>{api.resolve('AB/CD').subscribe();const request=http.expectOne('/api/customer-referrals/resolve/AB%2FCD');expect(request.request.method).toBe('GET');request.flush({code:'ABCD',tenantKey:'shop',retailerName:'Shop',isEnabled:true});});
});
