import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { CustomerAuthService } from '../customer-auth.service';
@Component({selector:'shop-customer-auth-page',standalone:true,imports:[],templateUrl:'./customer-auth.page.html',styleUrl:'./customer-auth.page.css'})
export class CustomerAuthPage implements OnInit {
  storeKey=''; mobile=''; otp=''; name=''; email=''; password=''; confirmPassword=''; challengeId=''; mode:'signin'|'signup'='signin'; forgot=false; otpLogin=false; busy=signal(false); error=signal(''); resendAt=signal(0);
  constructor(private readonly route:ActivatedRoute,private readonly router:Router,private readonly auth:CustomerAuthService){}
  ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';this.mode=this.route.snapshot.queryParamMap.get('mode')==='signup'?'signup':'signin';}
  async signIn():Promise<void>{if(this.busy())return;this.busy.set(true);this.error.set('');try{await this.auth.signIn(this.storeKey,this.mobile,this.password);await this.goBack();}catch(e){this.error.set(this.message(e,'The mobile number or password is incorrect.'));}finally{this.busy.set(false);}}
  async beginOtp():Promise<void>{if(this.busy())return;this.busy.set(true);this.error.set('');try{const x=await this.auth.request(this.storeKey,this.mobile);this.challengeId=x.challengeId;this.resendAt.set(Date.now()+x.resendAfterSeconds*1000);}catch(e){this.error.set(this.message(e,'Verification code could not be sent.'));}finally{this.busy.set(false);}}
  async verifyOtpLogin():Promise<void>{if(this.busy()||!this.challengeId)return;this.busy.set(true);this.error.set('');try{await this.auth.verifyOtp(this.storeKey,this.challengeId,this.mobile,this.otp);await this.goBack();}catch(e){this.error.set(this.message(e,'The verification code is invalid or expired.'));}finally{this.busy.set(false);}}
  async verifySignup():Promise<void>{if(this.busy()||!this.challengeId)return;this.busy.set(true);this.error.set('');try{await this.auth.verify(this.storeKey,this.challengeId,this.mobile,this.otp,this.name.trim(),this.email.trim()||undefined,this.password||undefined,this.confirmPassword||undefined,this.route.snapshot.queryParamMap.get('ref')??undefined);await this.goBack();}catch(e){this.error.set(this.message(e,'The verification code is invalid or expired.'));}finally{this.busy.set(false);}}
  async resetPassword():Promise<void>{if(this.busy()||!this.challengeId)return;this.busy.set(true);this.error.set('');try{await this.auth.reset(this.storeKey,{challengeId:this.challengeId,mobileNumber:this.mobile,otp:this.otp,newPassword:this.password,confirmPassword:this.confirmPassword});this.forgot=false;this.challengeId='';this.password='';this.confirmPassword='';this.error.set('Password reset. You can now sign in.');}catch(e){this.error.set(this.message(e,'Password reset could not be completed.'));}finally{this.busy.set(false);}}
  canResend():boolean{return Date.now()>=this.resendAt();}
  private async goBack():Promise<void>{const returnUrl=this.route.snapshot.queryParamMap.get('returnUrl');const safeReturn=returnUrl&&returnUrl.startsWith('/'+this.storeKey+'/')?returnUrl:null;await this.router.navigateByUrl(safeReturn??('/'+this.storeKey+'/account'));}
  private message(e:unknown,fallback:string):string{const body=e instanceof HttpErrorResponse?e.error as{message?:string}:null;return body?.message??fallback;}
}
