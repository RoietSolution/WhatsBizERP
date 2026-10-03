import { Component, OnInit, signal } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { CustomerAuthService } from '../customer-auth.service';
@Component({selector:'shop-customer-auth-page',standalone:true,imports:[],templateUrl:'./customer-auth.page.html',styleUrl:'./customer-auth.page.css'})
export class CustomerAuthPage implements OnInit{
 storeKey='';mobile='';otp='';name='';email='';challengeId='';mode:'signin'|'signup'='signin';busy=signal(false);error=signal('');nameError=signal('');resendAt=signal(0);
 constructor(private readonly route:ActivatedRoute,private readonly router:Router,private readonly auth:CustomerAuthService){}
 ngOnInit():void{this.storeKey=this.route.parent?.snapshot.paramMap.get('storeKey')??'';this.mode=this.route.snapshot.queryParamMap.get('mode')==='signup'?'signup':'signin';}
 async send():Promise<void>{if(this.busy())return;this.name=this.name.trim();this.nameError.set('');if(!this.name){this.nameError.set('Name is required.');return;}this.busy.set(true);this.error.set('');try{const x=await this.auth.request(this.storeKey,this.mobile);this.challengeId=x.challengeId;this.resendAt.set(Date.now()+x.resendAfterSeconds*1000);}catch(e){this.error.set(this.message(e,'Verification code could not be sent.'));}finally{this.busy.set(false);}}
 async verify():Promise<void>{if(this.busy()||!this.challengeId)return;this.name=this.name.trim();this.nameError.set('');if(!this.name){this.nameError.set('Name is required.');this.challengeId='';return;}this.busy.set(true);this.error.set('');try{await this.auth.verify(this.storeKey,this.challengeId,this.mobile,this.otp,this.name,this.mode==='signup'?this.email:undefined);const returnUrl=this.route.snapshot.queryParamMap.get('returnUrl');const safeReturn=returnUrl&&returnUrl.startsWith('/'+this.storeKey+'/')?returnUrl:null;await this.router.navigateByUrl(safeReturn??('/'+this.storeKey+'/account'));}catch(e){this.error.set(this.message(e,'The verification code is invalid or expired.'));}finally{this.busy.set(false);}}
 canResend():boolean{return Date.now()>=this.resendAt();}
 private message(e:unknown,fallback:string):string{const body=e instanceof HttpErrorResponse?e.error as{message?:string}:null;return body?.message??fallback;}
}
