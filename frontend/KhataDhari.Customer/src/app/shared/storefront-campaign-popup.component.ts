import { Component, ElementRef, HostListener, Input, OnChanges, OnDestroy, ViewChild, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { NavigationEnd, Router } from '@angular/router';
import { firstValueFrom, Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import { CampaignFeed, PopupCampaign, impressionKey, nextCampaign } from './campaign-visibility';
import { CampaignCategoryNavigationService } from './campaign-category-navigation.service';

@Component({standalone:true,selector:'shop-campaign-popup',templateUrl:'./storefront-campaign-popup.component.html',styleUrl:'./storefront-campaign-popup.component.css'})
export class StorefrontCampaignPopupComponent implements OnChanges,OnDestroy {
 @Input({required:true}) storeKey='';@ViewChild('modal',{static:true}) modal!:ElementRef<HTMLDialogElement>;
 readonly active=signal<PopupCampaign|null>(null);readonly imageFailed=signal(false);
 private rows:PopupCampaign[]=[];private seenMemory=new Set<string>();private timer?:ReturnType<typeof setTimeout>;private openTimer?:ReturnType<typeof setTimeout>;
 private request=0;private inFlight=false;private destroyed=false;private validUntil=0;private lastRequest=0;private readonly navigation:Subscription;
 constructor(private readonly http:HttpClient,private readonly router:Router,private readonly categoryNavigation:CampaignCategoryNavigationService){this.navigation=router.events.subscribe(event=>{if(event instanceof NavigationEnd){if(!this.browsing())this.close(false);else this.showNext();}});}
 ngOnChanges():void{++this.request;this.inFlight=false;this.rows=[];this.validUntil=0;this.close(false);void this.refresh();}
 ngOnDestroy():void{this.destroyed=true;++this.request;clearTimeout(this.timer);clearTimeout(this.openTimer);this.navigation.unsubscribe();this.modal?.nativeElement.close();}
 @HostListener('document:visibilitychange') resume():void{if(document.visibilityState!=='visible')return;if(Date.now()>=this.validUntil||Date.now()-this.lastRequest>30000)void this.refresh();else this.showNext();}
 private browsing():boolean{const path=this.router.url.split('?')[0];const root='/'+encodeURIComponent(this.storeKey);return path===root||path===root+'/'||['/products','/categories','/search','/offers'].some(part=>path===root+part||path.startsWith(root+part+'/'));}
 private storage(item:PopupCampaign):Storage{return item.frequency==='VISIT'?sessionStorage:localStorage;}
 private seen(item:PopupCampaign):boolean{const key=impressionKey(this.storeKey,item);try{return this.seenMemory.has(key)||this.storage(item).getItem(key)==='1';}catch{return this.seenMemory.has(key);}}
 private record(item:PopupCampaign):void{const key=impressionKey(this.storeKey,item);this.seenMemory.add(key);try{this.storage(item).setItem(key,'1');}catch{}}
 async refresh():Promise<void>{
   if(!this.storeKey||this.inFlight||this.destroyed)return;
   clearTimeout(this.timer);const sequence=++this.request,key=this.storeKey;this.inFlight=true;this.lastRequest=Date.now();
   try{
     const feed=await firstValueFrom(this.http.get<CampaignFeed>(`${environment.apiBaseUrl}/api/store/${encodeURIComponent(key)}/campaigns`));
     if(sequence!==this.request||key!==this.storeKey||this.destroyed)return;
     const now=Date.parse(feed.serverNowUtc),boundary=Date.parse(feed.refreshAtUtc);if(!Number.isFinite(now)||!Number.isFinite(boundary)||boundary<=now)throw new Error('Invalid campaign timing');
     const delay=Math.min(300000,Math.max(1000,boundary-now));this.validUntil=this.lastRequest+delay;this.rows=feed.campaigns;
     const current=this.active();if(current){const updated=this.rows.find(x=>x.campaignId===current.campaignId);if(!updated)this.close(false);else this.active.set(updated);}
     if(Date.now()>=this.validUntil){this.rows=[];this.close(false);}
     this.timer=setTimeout(()=>void this.refresh(),Math.max(1000,this.validUntil-Date.now()));this.showNext();
   }catch{if(sequence===this.request&&!this.destroyed){this.rows=[];this.validUntil=0;this.close(false);this.timer=setTimeout(()=>void this.refresh(),60000);}}
   finally{if(sequence===this.request)this.inFlight=false;}
 }
 private showNext():void{
   if(this.destroyed||document.visibilityState!=='visible'||this.active()||Date.now()>=this.validUntil||!this.browsing())return;
   const item=nextCampaign(this.rows,row=>this.seen(row));if(!item)return;
   this.active.set(item);this.imageFailed.set(false);
   clearTimeout(this.openTimer);this.openTimer=setTimeout(()=>{if(this.destroyed||this.active()?.campaignId!==item.campaignId||!this.browsing())return;try{this.modal.nativeElement.showModal();this.record(item);}catch{this.active.set(null);}},0);
 }
 close(next=true):void{this.modal?.nativeElement.close();this.active.set(null);clearTimeout(this.openTimer);if(next&&!this.destroyed)this.openTimer=setTimeout(()=>this.showNext(),180);}
 cancel(event:Event):void{event.preventDefault();this.close();}
 imageUrl(item:PopupCampaign):string{return item.imageUrl?environment.apiBaseUrl+item.imageUrl:'';}
 go():void{const destination=this.active()?.destination;if(!destination)return;const url=new URL(destination,window.location.origin),root='/'+encodeURIComponent(this.storeKey);if(url.origin!==window.location.origin||!(url.pathname===root||url.pathname.startsWith(root+'/')))return;this.close(false);const category=url.searchParams.get('category');if(category&&(url.pathname===root||url.pathname===root+'/'))void this.categoryNavigation.navigate(url.pathname+url.search,this.storeKey,category);else{this.categoryNavigation.cancel();void this.router.navigateByUrl(url.pathname+url.search);}}
}
