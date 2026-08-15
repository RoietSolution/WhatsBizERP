import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSelectModule } from '@angular/material/select';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { WhatsAppApiService, WhatsAppConfiguration, WhatsAppMetaTestDiagnostics } from './whatsapp-api.service';

@Component({
  imports: [FormsModule, DatePipe, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSlideToggleModule, MatSelectModule, PageContainerComponent, PageHeaderComponent, StatusChipComponent],
  templateUrl: './whatsapp-configuration.component.html',
  styles: [`
    .card { margin-top: 14px; padding: 20px; background: var(--wb-surface); border: 1px solid var(--wb-border); border-radius: var(--wb-radius-md); }
    .grid { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 14px; }
    .wide { grid-column: 1/-1; } .meta { display:flex; flex-wrap:wrap; gap:18px; margin: 0 0 18px; color:var(--wb-text-secondary); }
    .notice { padding:12px; border-radius:8px; background:var(--wb-primary-soft); margin-bottom:16px; }
    .readiness { margin:16px 0; padding:16px; border:1px solid var(--wb-border); border-radius:8px; }
    .checks { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:8px 18px; margin:12px 0; }
    .ok { color:var(--wb-success); } .missing { color:var(--wb-danger); }
    code { overflow-wrap:anywhere; }
    .error { color:var(--wb-danger); } footer { display:flex; justify-content:flex-end; gap:10px; margin-top:18px; }
    @media(max-width:700px){.grid{grid-template-columns:1fr}.wide{grid-column:auto}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WhatsAppConfigurationComponent {
  readonly loading = signal(true); readonly saving = signal(false); readonly message = signal('');
  readonly diagnostics = signal<WhatsAppMetaTestDiagnostics | null>(null);
  readonly model = signal<WhatsAppConfiguration>({ providerMode:'MOCK', isEnabled:false, connectionStatus:'NOT_CONFIGURED', hasAccessToken:false, hasWebhookVerifyToken:false, hasAppSecret:false });
  accessToken = ''; webhookVerifyToken = ''; appSecret = ''; recipientNumber=''; testMessage='WhatsBiz META_TEST connection successful.';
  constructor(private readonly api: WhatsAppApiService) { this.reload(); }
  reload() { this.api.get().subscribe({ next:x=>{this.model.set(x);this.recipientNumber=x.testRecipientNumber??'';this.loading.set(false);if(x.providerMode==='META_TEST')this.reloadDiagnostics();}, error:()=>{this.message.set('Unable to load WhatsApp configuration.');this.loading.set(false);} }); }
  reloadDiagnostics(){this.api.diagnostics().subscribe({next:x=>this.diagnostics.set(x),error:()=>this.diagnostics.set(null)});}
  save() { const x=this.model(); this.saving.set(true); this.message.set(''); this.api.save({ providerMode:x.providerMode, metaAppId:x.metaAppId??'', whatsAppBusinessAccountId:x.whatsAppBusinessAccountId??'', phoneNumberId:x.phoneNumberId??'', apiVersion:x.apiVersion??'', testRecipientNumber:this.recipientNumber||undefined, isEnabled:x.isEnabled, accessToken:this.accessToken||undefined, webhookVerifyToken:this.webhookVerifyToken||undefined, appSecret:this.appSecret||undefined }).subscribe({next:y=>{this.model.set(y);this.clearSecrets();this.message.set(x.providerMode==='MOCK'?'Mock provider configured. No Meta registration or credentials are used.':'Configuration saved. Validate the Meta connection next.');this.saving.set(false);this.reloadDiagnostics();},error:()=>{this.message.set('Configuration could not be saved. Check the values and try again.');this.saving.set(false);}}); }
  validate() { this.saving.set(true);this.message.set('');this.api.validate(this.accessToken).subscribe({next:r=>{this.message.set(r.message??'Validation completed.');this.clearSecrets();this.reload();this.saving.set(false);},error:()=>{this.message.set('Connection validation failed safely. Review the configuration and server logs.');this.saving.set(false);}}); }
  sendTestMessage(){this.saving.set(true);this.message.set('');this.api.sendTestMessage(this.recipientNumber,this.testMessage).subscribe({next:r=>{this.message.set(r.succeeded?`${r.message} Meta message ID: ${r.metaMessageId}; sent ${new Date(r.attemptedAt).toLocaleString()}.`:r.message??'Meta rejected the test message.');this.saving.set(false);this.reloadDiagnostics();},error:()=>{this.message.set('The test message could not be sent. Check the recipient and META_TEST configuration.');this.saving.set(false);}});}
  setupReady(){const x=this.model();const d=this.diagnostics();return x.isEnabled&&x.providerMode==='META_TEST'&&!!x.metaAppId&&!!x.whatsAppBusinessAccountId&&!!x.phoneNumberId&&!!x.apiVersion&&x.hasAccessToken&&x.hasWebhookVerifyToken&&x.hasAppSecret&&!!x.testRecipientNumber&&x.connectionStatus==='CONNECTED'&&!!d?.lastWebhookVerifiedOn;}
  tone(): 'success'|'warning'|'danger'|'info' { return this.model().connectionStatus==='CONNECTED'?'success':this.model().connectionStatus==='ERROR'?'danger':this.model().connectionStatus==='DISABLED'?'warning':'info'; }
  private clearSecrets(){this.accessToken='';this.webhookVerifyToken='';this.appSecret='';}
}
