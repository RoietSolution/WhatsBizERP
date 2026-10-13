import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../environments/environment';
import { CustomerSessionService, StorefrontCustomer } from './customer-session.service';
import { WishlistService } from './wishlist.service';
import { CustomerRewardsService } from './customer-rewards.service';
import { StorefrontActivityService } from './storefront-activity.service';
interface Challenge{challengeId:string;expiresAt:string;resendAfterSeconds:number}
interface Authentication{customerSessionToken:string;customer:StorefrontCustomer}
@Injectable({providedIn:'root'})
export class CustomerAuthService{
 constructor(private readonly http:HttpClient,private readonly session:CustomerSessionService,private readonly wishlist:WishlistService,private readonly rewards:CustomerRewardsService,private readonly activity:StorefrontActivityService){}
 request(storeKey:string,mobileNumber:string):Promise<Challenge>{return firstValueFrom(this.http.post<Challenge>(this.url(storeKey)+'/customer-auth/otp/request',{mobileNumber}));}
 async verify(storeKey:string,challengeId:string,mobileNumber:string,otp:string,name:string,email:string|undefined,password?:string,confirmPassword?:string,referralCode?:string):Promise<Authentication>{const result=await firstValueFrom(this.http.post<Authentication>(this.url(storeKey)+'/customer-auth/otp/verify',{challengeId,mobileNumber,otp,name,email:email||null,isRegistration:true,password,confirmPassword}));await this.establish(storeKey,result);this.activity.record(storeKey,'CUSTOMER_SIGN_IN');if(referralCode?.trim()){try{await this.rewards.capture(storeKey,referralCode.trim());}catch{this.rewards.rememberPending(storeKey,referralCode);}}return result;}
 async verifyOtp(storeKey:string,challengeId:string,mobileNumber:string,otp:string):Promise<Authentication>{const result=await firstValueFrom(this.http.post<Authentication>(this.url(storeKey)+'/customer-auth/otp/verify',{challengeId,mobileNumber,otp,isRegistration:false}));await this.establish(storeKey,result);this.activity.record(storeKey,'CUSTOMER_SIGN_IN');return result;}
 async signIn(storeKey:string,mobileNumber:string,password:string):Promise<Authentication>{const result=await firstValueFrom(this.http.post<Authentication>(this.url(storeKey)+'/customer-auth/password/signin',{mobileNumber,password}));await this.establish(storeKey,result);this.activity.record(storeKey,'CUSTOMER_SIGN_IN');return result;}
 reset(storeKey:string,input:{challengeId:string;mobileNumber:string;otp:string;newPassword:string;confirmPassword:string}):Promise<void>{return firstValueFrom(this.http.post<void>(this.url(storeKey)+'/customer-auth/password/reset',input));}
 changePassword(storeKey:string,currentPassword:string,newPassword:string,confirmPassword:string):Promise<void>{return firstValueFrom(this.http.post<void>(this.url(storeKey)+'/session/password/change',{currentPassword,newPassword,confirmPassword},{headers:{'X-Customer-Session':this.session.token(storeKey)}}));}
 private async establish(storeKey:string,result:Authentication):Promise<void>{this.session.establish(storeKey,result.customerSessionToken,result.customer);await this.wishlist.switchCustomer(storeKey,result.customer.id);}
 private url(storeKey:string):string{return environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey);}
}