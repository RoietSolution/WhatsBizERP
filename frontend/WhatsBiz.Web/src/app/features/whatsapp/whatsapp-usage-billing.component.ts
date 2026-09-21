import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { WhatsAppUsageBillingApiService, WhatsAppUsageCategorySummary, WhatsAppUsageCurrencyAmount, WhatsAppUsageSummary } from './whatsapp-usage-billing-api.service';

@Component({
  selector:'app-whatsapp-usage-billing',
  imports:[FormsModule,MatButtonModule,PageContainerComponent,PageHeaderComponent],
  templateUrl:'./whatsapp-usage-billing.component.html',
  styleUrl:'./whatsapp-usage-billing.component.scss',
  changeDetection:ChangeDetectionStrategy.OnPush,
})
export class WhatsAppUsageBillingComponent implements OnInit {
  readonly summary=signal<WhatsAppUsageSummary|null>(null);
  readonly loading=signal(false);
  readonly error=signal('');
  month=new Date().toISOString().slice(0,7);
  constructor(private readonly api:WhatsAppUsageBillingApiService){}
  ngOnInit(){this.load();}
  load(){
    const [year,month]=this.month.split('-').map(Number);
    if(!year||!month)return;
    this.loading.set(true);this.error.set('');
    this.api.summary(year,month).subscribe({next:value=>{this.summary.set(value);this.loading.set(false);},error:()=>{this.error.set('WhatsApp usage could not be loaded.');this.loading.set(false);}});
  }
  label(category:string){return category.charAt(0)+category.slice(1).toLowerCase();}
  amount(value:number,currency:string){return new Intl.NumberFormat('en-IN',{style:'currency',currency,minimumFractionDigits:2,maximumFractionDigits:6}).format(value);}
  categoryCost(row:WhatsAppUsageCategorySummary){return row.estimatedMetaCost!==null&&row.currency?this.amount(row.estimatedMetaCost,row.currency):null;}
  breakdown(values:WhatsAppUsageCurrencyAmount[]){return values.map(x=>this.amount(x.estimatedMetaCost,x.currency)).join(' + ');}
}
