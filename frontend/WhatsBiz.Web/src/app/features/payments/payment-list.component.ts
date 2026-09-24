import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { CurrentUserService } from '../../core/services/current-user.service';
import { FeatureService, FeatureTenantSummary } from '../../core/services/feature.service';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { CommercePayment, PaymentsApiService } from './payments-api.service';

@Component({selector:'app-payment-list',imports:[DatePipe,DecimalPipe,FormsModule,MatButtonModule,PageContainerComponent,PageHeaderComponent],templateUrl:'./payment-list.component.html',styleUrl:'./payments.component.scss',changeDetection:ChangeDetectionStrategy.OnPush})
export class PaymentListComponent implements OnInit {
  readonly rows=signal<CommercePayment[]>([]);readonly tenants=signal<FeatureTenantSummary[]>([]);readonly selected=signal<CommercePayment|null>(null);readonly total=signal(0);readonly loading=signal(false);readonly error=signal('');
  readonly applicationOwner:boolean;tenantId='';dateFrom='';dateTo='';status='';provider='';pageNumber=1;pageSize=25;
  constructor(private readonly api:PaymentsApiService,currentUser:CurrentUserService,private readonly features:FeatureService){this.applicationOwner=currentUser.user()?.roles.includes('ApplicationOwner')===true;}
  ngOnInit(){if(this.applicationOwner)this.features.tenants().subscribe({next:x=>this.tenants.set(x),error:()=>this.error.set('Retailers could not be loaded.')});this.load();}
  load(){this.loading.set(true);this.error.set('');this.api.list({tenantId:this.applicationOwner?this.tenantId||undefined:undefined,dateFrom:this.dateFrom||undefined,dateTo:this.dateTo||undefined,status:this.status||undefined,provider:this.provider||undefined,pageNumber:this.pageNumber,pageSize:this.pageSize},this.applicationOwner).subscribe({next:x=>{this.rows.set(x.items);this.total.set(x.totalCount);this.pageNumber=x.pageNumber;this.loading.set(false);},error:()=>{this.error.set('Payments could not be loaded.');this.loading.set(false);}});}
  applyFilters(){this.pageNumber=1;this.load();}
  previous(){if(this.pageNumber>1){this.pageNumber--;this.load();}}
  next(){if(this.pageNumber*this.pageSize<this.total()){this.pageNumber++;this.load();}}
  open(row:CommercePayment){this.api.get(row.paymentId,this.applicationOwner).subscribe({next:x=>this.selected.set(x),error:()=>this.error.set('Payment details could not be loaded.')});}
  verify(row:CommercePayment){if(this.applicationOwner)return;if(!globalThis.confirm(`Confirm that ₹${row.amount.toFixed(2)} was received for ${row.orderNumber}?`))return;const reference=globalThis.prompt('Bank/UPI reference (optional)');this.api.verify(row.paymentId,reference).subscribe({next:()=>this.load(),error:()=>this.error.set('Payment could not be verified. Refresh and retry.')});}
}
