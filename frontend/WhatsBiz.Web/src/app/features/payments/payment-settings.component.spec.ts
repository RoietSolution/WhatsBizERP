import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PaymentSettingsComponent } from './payment-settings.component';
import { PaymentsApiService } from './payments-api.service';
import { FeatureService } from '../../core/services/feature.service';

describe('PaymentSettingsComponent',()=>{
  let fixture:ComponentFixture<PaymentSettingsComponent>;
  const api={settings:jasmine.createSpy().and.returnValue(of({onlinePaymentEnabled:true,providers:[{provider:'RAZORPAY',isEnabled:true,isDefault:true,isConfigured:true,maskedKeyId:'rzp_****1234',hasKeySecret:true,hasWebhookSecret:true,isTestMode:true,upiVpa:null,payeeName:null},{provider:'DIRECT_UPI',isEnabled:true,isDefault:false,isConfigured:true,maskedKeyId:null,hasKeySecret:false,hasWebhookSecret:false,isTestMode:false,upiVpa:'shop@upi',payeeName:'Shop'},{provider:'COD',isEnabled:false,isDefault:false,isConfigured:true,maskedKeyId:null,hasKeySecret:false,hasWebhookSecret:false,isTestMode:false,upiVpa:null,payeeName:null}]})),saveRazorpay:jasmine.createSpy(),saveDirectUpi:jasmine.createSpy(),saveCod:jasmine.createSpy(),saveOptions:jasmine.createSpy()};
  beforeEach(async()=>{await TestBed.configureTestingModule({imports:[PaymentSettingsComponent],providers:[{provide:PaymentsApiService,useValue:api},{provide:FeatureService,useValue:{tenants:()=>of([{tenantId:'tenant-1',tenantKey:'T1',tenantName:'Retailer One'}])}}]}).compileComponents();fixture=TestBed.createComponent(PaymentSettingsComponent);fixture.detectChanges();});
  it('never places stored secrets or the masked key id in input values',()=>{const inputs=[...fixture.nativeElement.querySelectorAll('input')] as HTMLInputElement[];expect(inputs.some(x=>x.value.includes('secret'))).toBeFalse();expect(inputs.some(x=>x.value.includes('rzp_****1234'))).toBeFalse();});
  it('shows configured placeholders while leaving secret controls empty',()=>{const secrets=[...fixture.nativeElement.querySelectorAll('input[type=password]')] as HTMLInputElement[];expect(secrets.length).toBe(2);expect(secrets.every(x=>x.value==='')).toBeTrue();expect(secrets.every(x=>x.placeholder.includes('Configured'))).toBeTrue();});
});
