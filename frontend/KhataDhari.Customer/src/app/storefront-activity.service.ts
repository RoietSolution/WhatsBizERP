import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';
import { CustomerSessionService } from './customer-session.service';
@Injectable({providedIn:'root'})
export class StorefrontActivityService {
  private readonly ids=new Map<string,string>();
  private readonly recorded=new Set<string>();
  constructor(private readonly http:HttpClient,private readonly session:CustomerSessionService){}
  id(storeKey:string):string{const key=storeKey.toLowerCase();let value=this.ids.get(key);if(value)return value;try{value=sessionStorage.getItem('khata-dhari-visitor:'+key)||'';}catch{value='';}if(!/^[A-Za-z0-9_-]{16,64}$/.test(value)){value=crypto.randomUUID().replaceAll('-','');try{sessionStorage.setItem('khata-dhari-visitor:'+key,value);}catch{}}this.ids.set(key,value);return value;}
  record(storeKey:string,eventType:string,productId?:string,searchTerm?:string):void{const visitorSessionId=this.id(storeKey);const dedupe=eventType==='STORE_VISIT'||eventType==='PRODUCT_VIEW';const key=storeKey.toLowerCase()+'|'+visitorSessionId+'|'+eventType+'|'+(productId??'');if(dedupe&&this.recorded.has(key))return;if(dedupe)this.recorded.add(key);const token=this.session.token(storeKey);void firstValueFrom(this.http.post(environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey)+'/activity',{visitorSessionId,eventType,productId:productId??null,searchTerm:searchTerm??null},{headers:token?{'X-Customer-Session':token}:{}})).catch(()=>{if(dedupe)this.recorded.delete(key);});}
  recordDemandSearch(storeKey:string,keyword:string):void{const token=this.session.token(storeKey);void firstValueFrom(this.http.post(environment.apiBaseUrl+'/api/store/'+encodeURIComponent(storeKey)+'/search-events',{keyword,anonymousSessionId:this.id(storeKey)},{headers:token?{'X-Customer-Session':token}:{}})).catch(()=>{});}
}
