import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize, forkJoin } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { CustomerApiService } from '../customers/customer-api.service';
import { CustomerList } from '../customers/customer.models';
import { Referral, ReferralCode, ReferralConfiguration, ReferralMetrics, ReferralRewardsApiService } from './referral-rewards-api.service';

@Component({selector:'app-referral-rewards',imports:[CurrencyPipe,DatePipe,DecimalPipe,FormsModule,MatButtonModule,MatCheckboxModule,MatFormFieldModule,MatInputModule,MatSelectModule,MatSlideToggleModule],templateUrl:'./referral-rewards.component.html',styleUrl:'./referral-rewards.component.scss',changeDetection:ChangeDetectionStrategy.OnPush})
export class ReferralRewardsComponent {
  readonly busy=signal(true);readonly error=signal('');readonly customers=signal<CustomerList[]>([]);readonly metrics=signal<ReferralMetrics|null>(null);readonly history=signal<Referral[]>([]);readonly code=signal<ReferralCode|null>(null);
  readonly canManage:boolean;
  customerId='';
  adjustmentCoins:number|null=null;adjustmentReason='';
  config:ReferralConfiguration={isEnabled:false,qualificationType:'FIRST_COMPLETED_ORDER',minimumQualifyingAmount:0,referrerRewardCoins:200,referredRewardCoins:100,coinValidityDays:180,maximumRewardedReferralsPerCustomerMonth:10,maximumCoinsPerCustomerMonth:2000,reverseOnRefund:true,redemptionCoins:100,redemptionValue:10,minimumRedemptionCoins:100,maximumOrderPercentage:20,allowWithCoupons:false,allowDiscountedProducts:true,allowTax:false,allowDelivery:false};
  constructor(private readonly api:ReferralRewardsApiService,customers:CustomerApiService,private readonly snack:MatSnackBar,permissions:PermissionService){this.canManage=permissions.has('customer.rewards.manage');forkJoin({config:api.configuration(),metrics:api.metrics(),history:api.history(),customers:customers.search({sortBy:'customerName',descending:false,pageNumber:1,pageSize:200,isActive:true})}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.config=x.config;this.metrics.set(x.metrics);this.history.set(x.history);this.customers.set(x.customers.items);},error:()=>this.error.set('Referral rewards could not be loaded. Check feature access and database V15.')});}
  save(){this.busy.set(true);this.api.save(this.config).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.config=x;this.snack.open('Referral program saved.','Close',{duration:2500});},error:()=>this.error.set('Referral settings could not be saved.')});}
  loadCustomer(){this.code.set(null);if(!this.customerId)return;forkJoin({code:this.api.code(this.customerId),history:this.api.history(this.customerId)}).subscribe({next:x=>{this.code.set(x.code);this.history.set(x.history);},error:()=>this.error.set('Customer referral details could not be loaded.')});}
  copy(){const value=this.code()?.referralUrl;if(!value)return;navigator.clipboard.writeText(value).then(()=>this.snack.open('Referral link copied.','Close',{duration:1800}));}
  share(){const value=this.code();if(!value)return;window.open(`https://wa.me/?text=${encodeURIComponent(`Refer & Earn\nUse my referral code ${value.code}\n${value.referralUrl}`)}`,'_blank','noopener,noreferrer');}
  toggleCode(){const value=this.code();if(!value)return;this.api.setCodeActive(value.customerId,!value.isActive).subscribe(()=>this.code.set({...value,isActive:!value.isActive}));}
  approve(referral:Referral){this.busy.set(true);this.api.approve(referral.referralId).pipe(finalize(()=>this.busy.set(false))).subscribe({next:()=>{this.snack.open('Referral approved and rewards issued.','Close',{duration:2500});this.refresh();},error:()=>this.error.set('The referral could not be approved.')});}
  adjust(){if(!this.customerId||!this.adjustmentCoins||!this.adjustmentReason.trim())return;this.busy.set(true);this.api.adjust(this.customerId,this.adjustmentCoins,this.adjustmentReason.trim()).pipe(finalize(()=>this.busy.set(false))).subscribe({next:()=>{this.adjustmentCoins=null;this.adjustmentReason='';this.snack.open('Customer wallet adjusted.','Close',{duration:2500});this.refresh();},error:()=>this.error.set('The wallet adjustment could not be applied.')});}
  private refresh(){forkJoin({metrics:this.api.metrics(),history:this.api.history(this.customerId||undefined)}).subscribe(x=>{this.metrics.set(x.metrics);this.history.set(x.history);});}
}
