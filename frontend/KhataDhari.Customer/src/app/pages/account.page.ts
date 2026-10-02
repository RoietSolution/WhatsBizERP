import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CustomerSessionService } from '../customer-session.service';
import { StorefrontDataService } from '../data/storefront-data.service';

@Component({selector:'shop-account-page',standalone:true,imports:[RouterLink,RouterLinkActive,RouterOutlet],templateUrl:'./account.page.html',styleUrl:'./account.page.css'})
export class AccountPage implements OnInit {
  storeKey=''; readonly loading=signal(true);
  constructor(readonly route:ActivatedRoute,readonly session:CustomerSessionService,private readonly data:StorefrontDataService){}
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';this.session.restore(this.storeKey);void this.load();}
  initials(name:string):string{return name.split(/\s+/).filter(Boolean).slice(0,2).map(x=>x[0]).join('').toUpperCase();}
  private async load():Promise<void>{try{await this.session.refresh(this.storeKey);}finally{this.loading.set(false);}}
}
