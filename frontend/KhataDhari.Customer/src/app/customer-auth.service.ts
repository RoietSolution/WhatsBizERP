import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';
import { CustomerSessionService, StorefrontCustomer } from './customer-session.service';
import { WishlistService } from './wishlist.service';
interface Challenge{challengeId:string;expiresAt:string;resendAfterSeconds:number}
interface Authentication{customerSessionToken:string;customer:StorefrontCustomer}
@Injectable({providedIn:'root'})
export class CustomerAuthService{
 constructor(private readonly http:HttpClient,private readonly session:CustomerSessionService,private readonly wishlist:WishlistService){}
 request(storeKey:string,mobileNumber:string):Promise<Challenge>{return firstValueFrom(this.http.post<Challenge>(this.url(storeKey)+'/customer-auth/otp/request',{mobileNumber}));}
 async verify(storeKey:string,challengeId:string,mobileNumber:string,otp:string,name?:string,email?:string):Promise<Authentication>{const result=await firstValueFrom(this.http.post<Authentication>(this.url(storeKey)+'/customer-auth/otp/verify',{challengeId,mobileNumber,otp,name:name||null,email:email||null}));this.session.establish(storeKey,result.customerSessionToken,result.customer);await this.wishlist.switchCustomer(storeKey,result.customer.id);return result;}
 private url(storeKey:string):string{return environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey);}
}