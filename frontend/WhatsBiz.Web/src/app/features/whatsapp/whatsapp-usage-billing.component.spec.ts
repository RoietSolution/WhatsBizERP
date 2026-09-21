import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { WhatsAppUsageBillingApiService, WhatsAppUsageSummary } from './whatsapp-usage-billing-api.service';
import { WhatsAppUsageBillingComponent } from './whatsapp-usage-billing.component';

describe('WhatsAppUsageBillingComponent',()=>{
  const data:WhatsAppUsageSummary={period:'2026-09',currency:null,estimatedMetaCharges:null,rateConfigurationComplete:false,currencyBreakdown:[],categories:[{category:'MARKETING',deliveredMessages:1,estimatedMetaCost:null,currency:null,rateConfigured:false,currencyBreakdown:[]}],khataDhariSubscription:{planName:'Retail',whatsAppCommerceEntitled:true}};
  beforeEach(()=>TestBed.configureTestingModule({imports:[WhatsAppUsageBillingComponent],providers:[{provide:WhatsAppUsageBillingApiService,useValue:{summary:()=>of(data)}}]}));
  it('renders unavailable rates and keeps Meta charges separate from the subscription',()=>{
    const fixture=TestBed.createComponent(WhatsAppUsageBillingComponent);fixture.detectChanges();
    const text=(fixture.nativeElement as HTMLElement).textContent??'';
    expect(text).toContain('Estimated Meta Charges');expect(text).toContain('Rate not configured');expect(text).toContain('KhataDhari Subscription');
    expect(text).toContain("Meta's billing records remain authoritative");expect(text).not.toContain('Meta Invoice');
  });
});
