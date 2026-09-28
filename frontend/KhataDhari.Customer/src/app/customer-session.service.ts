import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';

export interface StorefrontCustomer { id:string; name:string; email?:string; mobile?:string; }

@Injectable({ providedIn: 'root' })
export class CustomerSessionService {
  readonly active=signal(false);readonly customer=signal<StorefrontCustomer|null>(null);
  constructor(private readonly http:HttpClient){}
  token(storeKey:string):string{return localStorage.getItem(this.key(storeKey))??'';}
  establish(storeKey:string,token:string,customer?:StorefrontCustomer):void{if(!storeKey||!token)return;localStorage.setItem(this.key(storeKey),token);this.active.set(true);if(customer)this.customer.set(customer);}
  clear(storeKey:string):void{localStorage.removeItem(this.key(storeKey));this.active.set(false);this.customer.set(null);}
  restore(storeKey:string):void{this.active.set(!!this.token(storeKey));if(this.active())void this.refresh(storeKey);}
  async refresh(storeKey:string):Promise<void>{const token=this.token(storeKey);if(!token){this.clear(storeKey);return;}try{const customer=await firstValueFrom(this.http.get<StorefrontCustomer>(this.url(storeKey)+'/session',{headers:{'X-Customer-Session':token}}));this.customer.set(customer);this.active.set(true);}catch(error){if(error instanceof HttpErrorResponse&&error.status===401)this.clear(storeKey);}}
  async update(storeKey:string,name:string,email?:string):Promise<StorefrontCustomer>{const customer=await firstValueFrom(this.http.put<StorefrontCustomer>(this.url(storeKey)+'/session/profile',{name,email:email||null},{headers:{'X-Customer-Session':this.token(storeKey)}}));this.customer.set(customer);return customer;}
  signOut(storeKey:string):void{this.clear(storeKey);}
  private key(storeKey:string):string{return'khatadhari.customer-session.'+storeKey.trim().toLowerCase();}
  private url(storeKey:string):string{return environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey);}
}