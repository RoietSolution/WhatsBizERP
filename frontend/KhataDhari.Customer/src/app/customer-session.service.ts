import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';

export interface StorefrontCustomer { id:string; name:string; email?:string; mobile?:string; profileImageUrl?:string; }

@Injectable({providedIn:'root'})
export class CustomerSessionService {
  readonly active=signal(false);readonly customer=signal<StorefrontCustomer|null>(null);private profileObjectUrl='';
  constructor(private readonly http:HttpClient){}
  token(storeKey:string):string{return localStorage.getItem(this.key(storeKey))??'';}
  isAuthenticated(storeKey:string):boolean{return !!this.token(storeKey);}
  establish(storeKey:string,token:string,customer?:StorefrontCustomer):void{if(!storeKey||!token)return;localStorage.setItem(this.key(storeKey),token);this.active.set(true);if(customer){const mapped=this.withAssetUrl(customer);this.customer.set(mapped);void this.hydrateProfileImage(storeKey,mapped);}}
  clear(storeKey:string):void{localStorage.removeItem(this.key(storeKey));this.active.set(false);this.customer.set(null);this.revokeProfileObjectUrl();}
  restore(storeKey:string):void{this.active.set(!!this.token(storeKey));if(this.active())void this.refresh(storeKey);}
  async refresh(storeKey:string):Promise<void>{const token=this.token(storeKey);if(!token){this.clear(storeKey);return;}try{const customer=await firstValueFrom(this.http.get<StorefrontCustomer>(this.url(storeKey)+'/session',{headers:{'X-Customer-Session':token}}));const mapped=this.withAssetUrl(customer);this.customer.set(mapped);this.active.set(true);await this.hydrateProfileImage(storeKey,mapped);}catch(error){if(error instanceof HttpErrorResponse&&error.status===401)this.clear(storeKey);}}
  async update(storeKey:string,name:string,email?:string):Promise<StorefrontCustomer>{const customer=await firstValueFrom(this.http.put<StorefrontCustomer>(this.url(storeKey)+'/session/profile',{name,email:email||null},{headers:{'X-Customer-Session':this.token(storeKey)}}));const mapped=this.withAssetUrl(customer);this.customer.set(mapped);await this.hydrateProfileImage(storeKey,mapped);return this.customer()??mapped;}
  signOut(storeKey:string):void{this.clear(storeKey);}
  private async hydrateProfileImage(storeKey:string,customer:StorefrontCustomer):Promise<void>{if(!customer.profileImageUrl){this.revokeProfileObjectUrl();if(this.customer()?.id===customer.id)this.customer.set({...customer,profileImageUrl:undefined});return;}if(customer.profileImageUrl.startsWith('blob:'))return;try{const blob=await firstValueFrom(this.http.get(customer.profileImageUrl,{headers:{'X-Customer-Session':this.token(storeKey)},responseType:'blob'}));this.revokeProfileObjectUrl();this.profileObjectUrl=URL.createObjectURL(blob);if(this.customer()?.id===customer.id)this.customer.set({...customer,profileImageUrl:this.profileObjectUrl});}catch{this.revokeProfileObjectUrl();if(this.customer()?.id===customer.id)this.customer.set({...customer,profileImageUrl:undefined});}}
  private revokeProfileObjectUrl():void{if(this.profileObjectUrl){URL.revokeObjectURL(this.profileObjectUrl);this.profileObjectUrl='';}}
  private key(storeKey:string):string{return'khatadhari.customer-session.'+storeKey.trim().toLowerCase();}
  private url(storeKey:string):string{return environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey);}
  private withAssetUrl(customer:StorefrontCustomer):StorefrontCustomer{return customer.profileImageUrl&&!customer.profileImageUrl.startsWith('http')?{...customer,profileImageUrl:environment.apiBaseUrl+customer.profileImageUrl}:customer;}
}
