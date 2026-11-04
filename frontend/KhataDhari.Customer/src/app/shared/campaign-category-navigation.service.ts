import { Injectable, OnDestroy, signal } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { Subscription } from 'rxjs';

export interface CampaignCategoryScroll { sequence:number;storeKey:string;categoryId:string; }

/** Transient intent, deliberately absent from the URL/history/session storage. */
@Injectable({providedIn:'root'})
export class CampaignCategoryNavigationService implements OnDestroy {
  readonly pending=signal<CampaignCategoryScroll|null>(null);
  private sequence=0;
  private readonly navigation:Subscription;
  constructor(private readonly router:Router){this.navigation=router.events.subscribe(event=>{if(event instanceof NavigationStart&&this.pending())this.cancel();});}
  ngOnDestroy():void{this.navigation.unsubscribe();this.cancel();}
  async navigate(destination:string,storeKey:string,categoryId:string):Promise<void>{
    const sequence=++this.sequence;this.pending.set(null);
    try{
      const navigated=await this.router.navigateByUrl(destination,{onSameUrlNavigation:'reload'});
      if(navigated&&sequence===this.sequence&&this.router.url===destination)this.pending.set({sequence,storeKey,categoryId});
    }catch{if(sequence===this.sequence)this.pending.set(null);}
  }
  cancel():void{++this.sequence;this.pending.set(null);}
  consume(sequence:number):boolean{if(this.pending()?.sequence!==sequence)return false;this.pending.set(null);return true;}
}
