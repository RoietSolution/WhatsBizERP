import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';

export interface StorefrontCustomer { id:string; name:string; email?:string; mobile?:string; profileImageUrl?:string; }

@Injectable({providedIn:'root'})
export class CustomerSessionService {
  readonly active=signal(false);readonly customer=signal<StorefrontCustomer|null>(null);readonly customerId=signal<string|null>(null);readonly restoring=signal(false);readonly restoreError=signal(false);private profileObjectUrl='';
  private currentStoreKey=''; private restorePromise:Promise<void>|null=null; private restorePromiseStore='';
  private loadedToken=''; private loadedAt=0; private generation=0; private imageSource=''; private imageRequest:Promise<void>|null=null;
  private refreshRequest:Promise<void>|null=null; private refreshKey='';
  constructor(private readonly http:HttpClient){}
  token(storeKey:string):string{return localStorage.getItem(this.key(storeKey))??'';}
  isAuthenticated(storeKey:string):boolean{return !!this.token(storeKey);}
  establish(storeKey:string,token:string,customer?:StorefrontCustomer):void{if(!storeKey||!token)return;const normalized=storeKey.trim().toLowerCase();if(this.currentStoreKey!==normalized||this.token(normalized)!==token||(!!customer&&!!this.customer()&&this.customer()!.id!==customer.id))this.resetMemory();this.currentStoreKey=normalized;localStorage.setItem(this.key(normalized),token);this.restoreError.set(false);this.restoring.set(false);this.active.set(true);if(customer){const mapped=this.withAssetUrl(customer);this.customer.set(this.displayProfile(mapped,normalized));this.customerId.set(mapped.id);this.saveCustomerId(normalized,mapped.id);void this.hydrateProfileImage(normalized,mapped);}}
  clear(storeKey:string):void{const normalized=storeKey.trim().toLowerCase();localStorage.removeItem(this.key(normalized));localStorage.removeItem(this.customerKey(normalized));if(this.currentStoreKey===normalized){this.resetMemory();this.active.set(false);}this.restoring.set(false);this.restoreError.set(false);this.restorePromise=null;this.restorePromiseStore='';}
  restore(storeKey:string):Promise<void>{
    const normalized=storeKey.trim().toLowerCase(),token=this.token(normalized);
    if(this.currentStoreKey!==normalized||(this.loadedToken&&this.loadedToken!==token)){this.resetMemory();this.currentStoreKey=normalized;this.active.set(false);}
    if(!token){this.clear(normalized);return Promise.resolve();}
    if(this.restorePromise&&this.restorePromiseStore===normalized)return this.restorePromise;
    if(this.loadedToken===token&&Date.now()-this.loadedAt<5*60*1000&&this.customer()&&!this.restoreError())return Promise.resolve();
    if(!this.customerId()){const known=this.readCustomerId(normalized);if(known)this.customerId.set(known);}
    this.active.set(true);this.restoring.set(true);this.restoreError.set(false);this.restorePromiseStore=normalized;
    const request=this.refresh(normalized);this.restorePromise=request;
    return request.finally(()=>{if(this.restorePromise===request){this.restoring.set(false);this.restorePromise=null;}});
  }
  refresh(storeKey:string,refreshImage=false):Promise<void>{const normalized=storeKey.trim().toLowerCase(),identity=JSON.stringify([normalized,this.token(normalized)]);if(refreshImage){++this.generation;this.imageSource='';this.imageRequest=null;this.refreshRequest=null;}if(this.refreshRequest&&this.refreshKey===identity)return this.refreshRequest;this.refreshKey=identity;const request=this.fetchProfile(normalized);this.refreshRequest=request;return request.finally(()=>{if(this.refreshRequest===request)this.refreshRequest=null;});}
  private async fetchProfile(storeKey:string):Promise<void>{const normalized=storeKey.trim().toLowerCase();const token=this.token(normalized);if(!token){if(this.currentStoreKey===normalized)this.clear(normalized);return;}if(this.currentStoreKey!==normalized||this.loadedToken&&this.loadedToken!==token)this.resetMemory();this.currentStoreKey=normalized;const generation=this.generation;try{const customer=await firstValueFrom(this.http.get<StorefrontCustomer>(this.url(normalized)+'/session',{headers:{'X-Customer-Session':token}}));if(generation!==this.generation||this.currentStoreKey!==normalized||this.token(normalized)!==token)return;this.loadedToken=token;this.loadedAt=Date.now();const mapped=this.withAssetUrl(customer);this.customer.set(this.displayProfile(mapped,normalized));this.customerId.set(mapped.id);this.saveCustomerId(normalized,mapped.id);this.active.set(true);this.restoreError.set(false);await this.hydrateProfileImage(normalized,mapped);}catch(error){if(generation!==this.generation||this.currentStoreKey!==normalized)return;if(error instanceof HttpErrorResponse&&error.status===401&&this.token(normalized)===token){this.clear(normalized);}else if(this.token(normalized)===token){this.active.set(true);this.restoreError.set(true);}}}
  async update(storeKey:string,name:string,email?:string):Promise<StorefrontCustomer>{const token=this.token(storeKey),generation=++this.generation;this.refreshRequest=null;this.imageRequest=null;const customer=await firstValueFrom(this.http.put<StorefrontCustomer>(this.url(storeKey)+'/session/profile',{name,email:email||null},{headers:{'X-Customer-Session':this.token(storeKey)}}));if(generation!==this.generation||this.token(storeKey)!==token||this.currentStoreKey!==storeKey.trim().toLowerCase())throw new Error('Customer session changed.');const mapped=this.withAssetUrl(customer);this.customer.set(this.displayProfile(mapped,storeKey));await this.hydrateProfileImage(storeKey,mapped);return this.customer()??mapped;}
  signOut(storeKey:string):void{this.clear(storeKey);}
  private displayProfile(customer:StorefrontCustomer,storeKey:string):StorefrontCustomer{return {...customer,profileImageUrl:this.imageSource===JSON.stringify([storeKey.trim().toLowerCase(),customer.id,customer.profileImageUrl??''])&&this.profileObjectUrl?this.profileObjectUrl:undefined};}
  private async hydrateProfileImage(storeKey:string,customer:StorefrontCustomer):Promise<void>{
    storeKey=storeKey.trim().toLowerCase();
    const token=this.token(storeKey),generation=this.generation,source=JSON.stringify([storeKey,customer.id,customer.profileImageUrl??'']);
    const current=()=>generation===this.generation&&this.currentStoreKey===storeKey&&this.token(storeKey)===token&&this.customer()?.id===customer.id;
    if(source===this.imageSource&&this.profileObjectUrl){if(current())this.customer.set({...customer,profileImageUrl:this.profileObjectUrl});return;}
    if(source===this.imageSource&&this.imageRequest){await this.imageRequest;if(current())this.customer.set({...customer,profileImageUrl:this.profileObjectUrl||undefined});return;}
    this.imageSource=source;
    if(!customer.profileImageUrl){this.revokeProfileObjectUrl();return;}
    const request=(async()=>{try{const blob=await firstValueFrom(this.http.get(customer.profileImageUrl!,{headers:{'X-Customer-Session':token},responseType:'blob'}));if(!current()||source!==this.imageSource)return;this.revokeProfileObjectUrl();this.profileObjectUrl=URL.createObjectURL(blob);this.customer.set({...customer,profileImageUrl:this.profileObjectUrl});}catch{if(current()&&source===this.imageSource){this.revokeProfileObjectUrl();this.customer.set({...customer,profileImageUrl:undefined});}}})();
    this.imageRequest=request;try{await request;}finally{if(this.imageRequest===request)this.imageRequest=null;}
  }
  private resetMemory():void{++this.generation;this.loadedToken='';this.loadedAt=0;this.imageSource='';this.imageRequest=null;this.refreshRequest=null;this.restorePromise=null;this.restorePromiseStore='';this.revokeProfileObjectUrl();this.customer.set(null);this.customerId.set(null);}
  private revokeProfileObjectUrl():void{if(this.profileObjectUrl){URL.revokeObjectURL(this.profileObjectUrl);this.profileObjectUrl='';}}
  private key(storeKey:string):string{return'khatadhari.customer-session.'+storeKey.trim().toLowerCase();}
  private customerKey(storeKey:string):string{return'khatadhari.customer-id.'+storeKey.trim().toLowerCase();}
  private readCustomerId(storeKey:string):string|null{try{return localStorage.getItem(this.customerKey(storeKey))||null;}catch{return null;}}
  private saveCustomerId(storeKey:string,customerId:string):void{try{localStorage.setItem(this.customerKey(storeKey),customerId);}catch{}}
  private url(storeKey:string):string{return environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey);}
  private withAssetUrl(customer:StorefrontCustomer):StorefrontCustomer{return customer.profileImageUrl&&!customer.profileImageUrl.startsWith('http')?{...customer,profileImageUrl:environment.apiBaseUrl+customer.profileImageUrl}:customer;}
}
