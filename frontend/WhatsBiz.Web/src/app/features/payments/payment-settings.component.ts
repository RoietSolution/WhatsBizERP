import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { PaymentSettings, PaymentsApiService } from './payments-api.service';

@Component({selector:'app-payment-settings',imports:[FormsModule,MatButtonModule,MatCheckboxModule,MatFormFieldModule,MatInputModule,PageContainerComponent,PageHeaderComponent],templateUrl:'./payment-settings.component.html',styleUrl:'./payments.component.scss',changeDetection:ChangeDetectionStrategy.OnPush})
export class PaymentSettingsComponent implements OnInit {
  readonly loading=signal(false);readonly message=signal('');readonly error=signal('');
  onlinePaymentEnabled=false;
  razorpay={keyId:'',maskedKeyId:'',keySecret:'',webhookSecret:'',isEnabled:false,isDefault:false,isTestMode:true,hasKeySecret:false,hasWebhookSecret:false};
  upi={upiVpa:'',payeeName:'',isEnabled:false,isDefault:false};cod={isEnabled:false,isDefault:false};
  constructor(private readonly api:PaymentsApiService){}
  ngOnInit(){this.load();}
  load(){this.loading.set(true);this.api.settings().subscribe({next:x=>{this.bind(x);this.loading.set(false);},error:()=>{this.error.set('Payment settings could not be loaded.');this.loading.set(false);}});}
  saveRazorpay(){this.save(this.api.saveRazorpay({keyId:this.razorpay.keyId,keySecret:this.razorpay.keySecret||null,webhookSecret:this.razorpay.webhookSecret||null,isEnabled:this.razorpay.isEnabled,isDefault:this.razorpay.isDefault,isTestMode:this.razorpay.isTestMode}));}
  saveUpi(){this.save(this.api.saveDirectUpi(this.upi));}
  saveCod(){this.save(this.api.saveCod(this.cod));}
  saveOptions(){this.save(this.api.saveOptions({onlinePaymentEnabled:this.onlinePaymentEnabled}));}
  private save(request:ReturnType<PaymentsApiService['saveCod']>){this.loading.set(true);this.error.set('');this.message.set('');request.subscribe({next:x=>{this.bind(x);this.message.set('Payment settings saved. Existing secrets were not returned.');this.loading.set(false);},error:()=>{this.error.set('Payment settings could not be saved. Check the configuration and retry.');this.loading.set(false);}});}
  private bind(x:PaymentSettings){this.onlinePaymentEnabled=x.onlinePaymentEnabled;const r=x.providers.find(p=>p.provider==='RAZORPAY'),u=x.providers.find(p=>p.provider==='DIRECT_UPI'),c=x.providers.find(p=>p.provider==='COD');this.razorpay={keyId:'',maskedKeyId:r?.maskedKeyId??'',keySecret:'',webhookSecret:'',isEnabled:r?.isEnabled??false,isDefault:r?.isDefault??false,isTestMode:r?.isTestMode??true,hasKeySecret:r?.hasKeySecret??false,hasWebhookSecret:r?.hasWebhookSecret??false};this.upi={upiVpa:u?.upiVpa??'',payeeName:u?.payeeName??'',isEnabled:u?.isEnabled??false,isDefault:u?.isDefault??false};this.cod={isEnabled:c?.isEnabled??false,isDefault:c?.isDefault??false};}
}
