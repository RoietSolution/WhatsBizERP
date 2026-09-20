import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSelectModule } from '@angular/material/select';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { WhatsAppApiService, WhatsAppConfiguration, WhatsAppMetaTestDiagnostics } from './whatsapp-api.service';
import { FeatureService, FeatureTenantSummary } from '../../core/services/feature.service';

@Component({
  imports: [FormsModule, DatePipe, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSlideToggleModule, MatSelectModule, PageContainerComponent, PageHeaderComponent, StatusChipComponent],
  templateUrl: './whatsapp-configuration.component.html',
  styles: [`
    .card { margin-top: 14px; padding: 20px; background: var(--wb-surface); border: 1px solid var(--wb-border); border-radius: var(--wb-radius-md); }
    .connection-card { max-width: 760px; margin: 18px auto; padding: 24px; background: var(--wb-surface); border: 1px solid var(--wb-border); border-radius: var(--wb-radius-md); box-shadow: 0 4px 18px rgba(20,45,60,.06); }
    .connection-heading { display:flex; justify-content:space-between; align-items:flex-start; gap:16px; }
    .connection-heading h2 { margin: 2px 0 0; font-size: 1.35rem; }
    .eyebrow { margin:0; color:var(--wb-text-secondary); font-size:.78rem; text-transform:uppercase; letter-spacing:.06em; }
    .status { display:inline-flex; align-items:center; min-height:30px; padding:0 12px; border-radius:999px; background:var(--wb-primary-soft); color:var(--wb-text-secondary); font-weight:700; font-size:.8rem; white-space:nowrap; }
    .status.connected { background:var(--wb-success-soft, #e5f5ec); color:var(--wb-success); }
    .intro { max-width:620px; margin:18px 0; color:var(--wb-text-secondary); line-height:1.55; }
    .details { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:14px; margin:22px 0; }
    .details div { min-width:0; padding:12px; border:1px solid var(--wb-border); border-radius:10px; }
    .owner-details { margin:0 0 18px; }
    .details dt { color:var(--wb-text-secondary); font-size:.82rem; }
    .details dd { margin:5px 0 0; font-weight:600; overflow-wrap:anywhere; }
    .mode { display:flex; flex-direction:column; justify-content:center; gap:5px; color:var(--wb-text-secondary); }
    .mode strong { color:var(--wb-text); }
    .grid { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 14px; }
    .wide { grid-column: 1/-1; } .meta { display:flex; flex-wrap:wrap; gap:18px; margin: 0 0 18px; color:var(--wb-text-secondary); }
    .notice { padding:12px; border-radius:8px; background:var(--wb-primary-soft); margin-bottom:16px; }
    .readiness { margin:16px 0; padding:16px; border:1px solid var(--wb-border); border-radius:8px; }
    .checks { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:8px 18px; margin:12px 0; }
    .ok { color:var(--wb-success); } .missing { color:var(--wb-danger); }
    code { overflow-wrap:anywhere; }
    .error { color:var(--wb-danger); } footer { display:flex; justify-content:flex-end; flex-wrap:wrap; gap:10px; margin-top:18px; }
    @media(max-width:700px){.grid,.details{grid-template-columns:1fr}.wide{grid-column:auto}.connection-card{padding:18px}.connection-heading{align-items:flex-start}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WhatsAppConfigurationComponent {
  readonly loading = signal(true); readonly saving = signal(false); readonly message = signal('');
  readonly diagnostics = signal<WhatsAppMetaTestDiagnostics | null>(null);
  readonly tenants = signal<FeatureTenantSummary[]>([]);
  readonly applicationOwner:boolean;
  readonly model = signal<WhatsAppConfiguration>({ providerMode:'MOCK', isEnabled:false, connectionStatus:'NOT_CONFIGURED', hasAccessToken:false, hasWebhookVerifyToken:false, hasAppSecret:false, usesSharedPlatformCredentials:false });
  accessToken = ''; webhookVerifyToken = ''; appSecret = ''; recipientNumber=''; testMessage='WhatsBiz META_TEST connection successful.'; tenantId='';
  constructor(private readonly api: WhatsAppApiService, private readonly features:FeatureService, route:ActivatedRoute) {
    this.applicationOwner=route.snapshot.data['platform']===true;
    if(this.applicationOwner)this.features.tenants().subscribe({next:tenants=>{this.tenants.set(tenants);this.loading.set(false);},error:()=>{this.message.set('Unable to load retailers.');this.loading.set(false);}});
    else this.reload();
  }
  connectLive(){ if(this.applicationOwner&&!this.tenantId)return; this.saving.set(true); this.message.set(''); this.api.onboardingConfig().subscribe({next:cfg=>{if(!cfg.enabled||!cfg.appId||!cfg.configurationId){this.message.set('Live WhatsApp onboarding is not configured yet.');this.saving.set(false);return;} this.launchEmbeddedSignup(cfg.appId,cfg.configurationId,cfg.graphApiVersion||'v23.0');},error:()=>{this.message.set('Unable to prepare Meta onboarding.');this.saving.set(false);}}); }
  private launchEmbeddedSignup(appId:string,configurationId:string,version:string){
    let waba:string|undefined; let phone:string|undefined;
    const finish=(code:string)=>this.api.completeOnboarding({authorizationCode:code,whatsAppBusinessAccountId:waba,phoneNumberId:phone,apiVersion:version}).subscribe({next:r=>{this.message.set(r.message||'WhatsApp Business connected.');this.reload();this.saving.set(false);},error:()=>{this.message.set('Meta onboarding could not be completed. Please try again.');this.saving.set(false);}});
    const run=()=>{const fb=(window as unknown as {FB?:{login:(cb:(r:{authResponse?:{code?:string}})=>void,o:unknown)=>void}}).FB;if(!fb){this.message.set('Meta onboarding could not load.');this.saving.set(false);return;} window.addEventListener('message',(event:MessageEvent)=>{if(event.origin!=='https://www.facebook.com')return;let data:unknown;try{data=JSON.parse(String(event.data));}catch{return;}if(typeof data!=='object'||!data)return;const d=data as {event?:string;data?:unknown};if(d.event!=='FINISH'&&d.event!=='FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING')return;const session=typeof d.data==='object'&&d.data!==null?d.data as {waba_id?:unknown;phone_number_id?:unknown}:undefined;waba=typeof session?.waba_id==='string'&&session.waba_id.trim()?session.waba_id:undefined;phone=typeof session?.phone_number_id==='string'&&session.phone_number_id.trim()?session.phone_number_id:undefined;this.message.set('Meta setup received. Completing securely…');});fb.login(response=>{const code=response.authResponse?.code;if(code)finish(code);else{this.message.set('Meta onboarding was cancelled.');this.saving.set(false);}}, {config_id:configurationId,response_type:'code',override_default_response_type:true,extras:{setup:{},featureType:'whatsapp_business_app_onboarding',sessionInfoVersion:'3',version:'v4'}});};
    if((window as unknown as {FB?:unknown}).FB){run();return;} const script=document.createElement('script');script.id='facebook-jssdk';script.src='https://connect.facebook.net/en_US/sdk.js';script.async=true;script.onload=()=>{(window as unknown as {fbAsyncInit?:()=>void}).fbAsyncInit=()=>{const w=window as unknown as {FB:{init:(o:unknown)=>void}};w.FB.init({appId,xfbml:false,version});run();};const existing=(window as unknown as {FB?:{init:(o:unknown)=>void}}).FB;if(existing){existing.init({appId,xfbml:false,version});run();}};script.onerror=()=>{this.message.set('Meta onboarding could not load.');this.saving.set(false);};document.head.appendChild(script);
  }
  setProviderMode(mode:WhatsAppConfiguration['providerMode']) {
    const current=this.model();
    if (mode === current.providerMode) return;
    this.clearSecrets();
    this.model.set(mode === 'LIVE'
      ? {...current,providerMode:mode,metaAppId:undefined,whatsAppBusinessAccountId:undefined,phoneNumberId:undefined,apiVersion:undefined,testRecipientNumber:undefined,hasAccessToken:false}
      : {...current,providerMode:mode});
  }
  selectTenant(){this.diagnostics.set(null);this.clearSecrets();if(this.tenantId)this.reload();}
  reload() { if(this.applicationOwner&&!this.tenantId)return;this.loading.set(true);this.api.get(this.tenantId||undefined).subscribe({ next:x=>{this.model.set(x);this.recipientNumber=x.testRecipientNumber??'';this.loading.set(false);if(x.providerMode==='META_TEST')this.reloadDiagnostics();}, error:()=>{this.message.set('Unable to load WhatsApp configuration.');this.loading.set(false);} }); }
  reloadDiagnostics(){if(this.applicationOwner&&!this.tenantId)return;this.api.diagnostics(this.tenantId||undefined).subscribe({next:x=>this.diagnostics.set(x),error:()=>this.diagnostics.set(null)});}
  save() { if(this.applicationOwner&&!this.tenantId)return;const x=this.model(); this.saving.set(true); this.message.set(''); this.api.save(this.tenantId||undefined,{ providerMode:x.providerMode, metaAppId:x.metaAppId??'', whatsAppBusinessAccountId:x.whatsAppBusinessAccountId??'', phoneNumberId:x.phoneNumberId??'', apiVersion:x.apiVersion??'', testRecipientNumber:this.recipientNumber||undefined, isEnabled:x.isEnabled, accessToken:this.accessToken||undefined, webhookVerifyToken:this.webhookVerifyToken||undefined, appSecret:this.appSecret||undefined }).subscribe({next:y=>{this.model.set(y);this.clearSecrets();this.message.set(x.providerMode==='MOCK'?'Mock provider configured. No Meta registration or credentials are used.':x.providerMode==='LIVE'&&y.connectionStatus!=='CONNECTED'?'LIVE onboarding is enabled. The retailer can now connect its WhatsApp Business account.':'Configuration saved. Validate the Meta connection next.');this.saving.set(false);this.reloadDiagnostics();},error:()=>{this.message.set('Configuration could not be saved. Check the values and try again.');this.saving.set(false);}}); }
  validate() { if(this.applicationOwner&&!this.tenantId)return;this.saving.set(true);this.message.set('');this.api.validate(this.tenantId||undefined,this.accessToken).subscribe({next:r=>{this.message.set(r.message??'Validation completed.');this.clearSecrets();this.reload();this.saving.set(false);},error:()=>{this.message.set('Connection validation failed safely. Review the configuration and server logs.');this.saving.set(false);}}); }
  sendTestMessage(){if(this.applicationOwner&&!this.tenantId)return;this.saving.set(true);this.message.set('');this.api.sendTestMessage(this.tenantId||undefined,this.recipientNumber,this.testMessage).subscribe({next:r=>{this.message.set(r.succeeded?`${r.message} Meta message ID: ${r.metaMessageId}; sent ${new Date(r.attemptedAt).toLocaleString()}.`:r.message??'Meta rejected the test message.');this.saving.set(false);this.reloadDiagnostics();},error:()=>{this.message.set('The test message could not be sent. Check the recipient and META_TEST configuration.');this.saving.set(false);}});}
  setupReady(){const x=this.model();const d=this.diagnostics();return x.isEnabled&&x.providerMode==='META_TEST'&&!!x.metaAppId&&!!x.whatsAppBusinessAccountId&&!!x.phoneNumberId&&!!x.apiVersion&&x.hasAccessToken&&x.hasWebhookVerifyToken&&x.hasAppSecret&&!!x.testRecipientNumber&&x.connectionStatus==='CONNECTED'&&!!d?.lastWebhookVerifiedOn;}
  maskedPhoneNumber(){const value=this.model().displayPhoneNumber?.trim();if(!value)return 'Not available';const digits=value.replace(/\D/g,'');if(digits.length<3)return '••••';return `${value.slice(0, Math.max(0,value.length-2)).replace(/\d/g,'•')}${value.slice(-2)}`;}
  liveStatusLabel(){if(!this.model().isEnabled)return 'DISABLED';const status=this.model().connectionStatus;if(status==='ACTION_REQUIRED')return 'ACTION REQUIRED';return 'NOT CONNECTED';}
  tone(): 'success'|'warning'|'danger'|'info' { return this.model().connectionStatus==='CONNECTED'?'success':this.model().connectionStatus==='ERROR'?'danger':this.model().connectionStatus==='DISABLED'?'warning':'info'; }
  private clearSecrets(){this.accessToken='';this.webhookVerifyToken='';this.appSecret='';}
}
