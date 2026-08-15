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
import { WhatsAppApiService, WhatsAppConfiguration } from './whatsapp-api.service';

@Component({
  imports: [FormsModule, DatePipe, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSlideToggleModule, MatSelectModule, PageContainerComponent, PageHeaderComponent, StatusChipComponent],
  templateUrl: './whatsapp-configuration.component.html',
  styles: [`
    .card { margin-top: 14px; padding: 20px; background: var(--wb-surface); border: 1px solid var(--wb-border); border-radius: var(--wb-radius-md); }
    .grid { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 14px; }
    .wide { grid-column: 1/-1; } .meta { display:flex; flex-wrap:wrap; gap:18px; margin: 0 0 18px; color:var(--wb-text-secondary); }
    .notice { padding:12px; border-radius:8px; background:var(--wb-primary-soft); margin-bottom:16px; }
    .error { color:var(--wb-danger); } footer { display:flex; justify-content:flex-end; gap:10px; margin-top:18px; }
    @media(max-width:700px){.grid{grid-template-columns:1fr}.wide{grid-column:auto}}
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WhatsAppConfigurationComponent {
  readonly loading = signal(true); readonly saving = signal(false); readonly message = signal('');
  readonly model = signal<WhatsAppConfiguration>({ providerMode:'MOCK', isEnabled:false, connectionStatus:'NOT_CONFIGURED', hasAccessToken:false, hasWebhookVerifyToken:false, hasAppSecret:false });
  accessToken = ''; webhookVerifyToken = ''; appSecret = '';
  constructor(private readonly api: WhatsAppApiService) { this.reload(); }
  reload() { this.api.get().subscribe({ next:x=>{this.model.set(x);this.loading.set(false);}, error:()=>{this.message.set('Unable to load WhatsApp configuration.');this.loading.set(false);} }); }
  save() { const x=this.model(); this.saving.set(true); this.message.set(''); this.api.save({ providerMode:x.providerMode, whatsAppBusinessAccountId:x.whatsAppBusinessAccountId??'', phoneNumberId:x.phoneNumberId??'', apiVersion:x.apiVersion??'', isEnabled:x.isEnabled, accessToken:this.accessToken||undefined, webhookVerifyToken:this.webhookVerifyToken||undefined, appSecret:this.appSecret||undefined }).subscribe({next:y=>{this.model.set(y);this.clearSecrets();this.message.set(x.providerMode==='MOCK'?'Mock provider configured. No Meta registration or credentials are used.':'Configuration saved. Validate the connection before use.');this.saving.set(false);},error:()=>{this.message.set('Configuration could not be saved. Check the values and try again.');this.saving.set(false);}}); }
  validate() { this.saving.set(true);this.message.set('');this.api.validate(this.accessToken).subscribe({next:r=>{this.message.set(r.message??'Validation completed.');this.clearSecrets();this.reload();this.saving.set(false);},error:()=>{this.message.set('Connection validation failed safely. Review the configuration and server logs.');this.saving.set(false);}}); }
  tone(): 'success'|'warning'|'danger'|'info' { return this.model().connectionStatus==='CONNECTED'?'success':this.model().connectionStatus==='ERROR'?'danger':this.model().connectionStatus==='DISABLED'?'warning':'info'; }
  private clearSecrets(){this.accessToken='';this.webhookVerifyToken='';this.appSecret='';}
}
