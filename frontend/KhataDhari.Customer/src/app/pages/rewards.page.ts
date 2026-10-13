import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CustomerRewards, CustomerRewardsService } from '../customer-rewards.service';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontNotificationService } from '../storefront-notification.service';

@Component({selector:'shop-rewards-page',standalone:true,imports:[DatePipe],templateUrl:'./rewards.page.html',styleUrl:'./rewards.page.css'})
export class RewardsPage implements OnInit {
  storeKey=''; readonly loading=signal(true); readonly error=signal(false); readonly data=signal<CustomerRewards|null>(null); readonly copied=signal('');
  constructor(private readonly route:ActivatedRoute, readonly session:CustomerSessionService, private readonly rewards:CustomerRewardsService, private readonly notify:StorefrontNotificationService) {}
  ngOnInit():void{this.storeKey=this.route.parent?.parent?.snapshot.paramMap.get('storeKey')??'';void this.load();}
  async load():Promise<void>{this.loading.set(true);this.error.set(false);try{this.data.set(await this.rewards.get(this.storeKey));}catch{this.error.set(true);}finally{this.loading.set(false);}}
  async copy(value:string,label:string):Promise<void>{try{await navigator.clipboard.writeText(value);this.copied.set(label);this.notify.show(label+' copied.');setTimeout(()=>this.copied.set(''),1800);}catch{this.notify.show('Copy is not available on this device.');}}
  share():void{const link=this.data()?.referralCode?.referralUrl;if(!link)return;const message=`I use KhataDhari for my retail shopping. Join using my referral link: ${link}`;window.open('https://wa.me/?text='+encodeURIComponent(message),'_blank','noopener,noreferrer');}
  statusClass(status:string):string{return status.toLowerCase();}
}