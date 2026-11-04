import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ProductApiService } from '../products/product-api.service';
import { Category, ProductListItem } from '../products/product.models';

interface Schedule { scheduleId:string; scheduleName:string; isEnabled:boolean; orderStartTime:string; orderEndTime:string; deliveryStartTime:string; deliveryEndTime:string; weekdayMask:number; timeZoneId:string; customerTitle?:string|null; customerMessage?:string|null; showAnnouncement:boolean; productIds:string[]; categoryIds:string[]; categoryCoveredProductIds?:string[]; }
interface Choice { id:string; name:string; code?:string; }

@Component({standalone:true,selector:'app-storefront-availability-schedules',imports:[CommonModule,FormsModule],templateUrl:'./storefront-availability-schedules.component.html',styleUrl:'./storefront-availability-schedules.component.css'})
export class StorefrontAvailabilitySchedulesComponent implements OnInit {
 readonly schedules=signal<Schedule[]>([]);readonly products=signal<Choice[]>([]);readonly categories=signal<Choice[]>([]);readonly message=signal('');readonly saving=signal(false);private persistedForm='';readonly productsLoading=signal(false);readonly categoriesLoading=signal(false);readonly productsError=signal('');readonly categoriesError=signal('');readonly productSearch=signal('');readonly categorySearch=signal('');editingId='';days=[{label:'Sun',value:1},{label:'Mon',value:2},{label:'Tue',value:4},{label:'Wed',value:8},{label:'Thu',value:16},{label:'Fri',value:32},{label:'Sat',value:64}];form=this.blank();
 constructor(private readonly http:HttpClient,private readonly productApi:ProductApiService){}
 ngOnInit():void{this.persistedForm=this.fingerprint();void this.load().catch(()=>this.message.set('Saved schedules could not be loaded. Refresh this page to retry.'));void this.loadChoices();}
 private blank():Schedule{return{scheduleId:'',scheduleName:'',isEnabled:true,orderStartTime:'15:00',orderEndTime:'17:00',deliveryStartTime:'15:00',deliveryEndTime:'17:00',weekdayMask:127,timeZoneId:'Asia/Kolkata',customerTitle:'',customerMessage:'',showAnnouncement:false,productIds:[],categoryIds:[]};}
 async load():Promise<void>{this.schedules.set(await firstValueFrom(this.http.get<Schedule[]>('/api/storefront-administration/availability-schedules')));}
 async loadChoices():Promise<void>{await Promise.all([this.loadProducts(),this.loadCategories()]);}
 private async loadProducts():Promise<void>{this.productsLoading.set(true);this.productsError.set('');try{const all:ProductListItem[]=[];const pageSize=200;let page=1;let total=0;do{const result=await firstValueFrom(this.productApi.search({sortBy:'productName',descending:false,pageNumber:page,pageSize,isActive:true}));all.push(...result.items.filter(x=>x.isActive));total=result.totalCount;page++;}while(all.length<total&&page<=Math.ceil(total/pageSize)+1);this.products.set([...new Map(all.map(x=>[x.productId,{id:x.productId,name:x.productName,code:x.productCode}])).values()]);}catch{this.products.set([]);this.productsError.set('Active products could not be loaded.');}finally{this.productsLoading.set(false);}}
 private async loadCategories():Promise<void>{this.categoriesLoading.set(true);this.categoriesError.set('');try{const result=await firstValueFrom(this.productApi.categories());this.categories.set([...new Map(this.flattenCategories(result).map(x=>[x.productCategoryId,{id:x.productCategoryId,name:x.categoryName}])).values()]);}catch{this.categories.set([]);this.categoriesError.set('Active categories could not be loaded.');}finally{this.categoriesLoading.set(false);}}
 private flattenCategories(items:Category[]):Category[]{return items.flatMap(item=>[...(item.isActive?[item]:[]),...this.flattenCategories(item.children??[])]);}
 filteredProducts():Choice[]{const search=this.productSearch().trim().toLocaleLowerCase();return search?this.products().filter(x=>`${x.name} ${x.code??''}`.toLocaleLowerCase().includes(search)):this.products();}
 filteredCategories():Choice[]{const search=this.categorySearch().trim().toLocaleLowerCase();return search?this.categories().filter(x=>`${x.name} ${x.code??''}`.toLocaleLowerCase().includes(search)):this.categories();}
 toggleDay(value:number):void{this.form.weekdayMask=(this.form.weekdayMask&value)?this.form.weekdayMask-value:this.form.weekdayMask+value;}
 isDaySelected(value:number):boolean{return (this.form.weekdayMask&value)!==0;}
 async save():Promise<void>{
   if(this.saving())return;
   if(!this.form.scheduleName.trim()){this.message.set('Enter a schedule name.');return;}
   this.saving.set(true);
   try {
     const body={...this.form, scheduleId:undefined,categoryCoveredProductIds:undefined,productIds:[...new Set(this.form.productIds)],categoryIds:[...new Set(this.form.categoryIds)]};
     const url='/api/storefront-administration/availability-schedules'+(this.editingId?'/'+this.editingId:'');
     const saved=await firstValueFrom(this.editingId?this.http.put<Schedule>(url,body):this.http.post<Schedule>(url,body));
     await this.load();
     const persisted=this.schedules().find(item=>item.scheduleId===saved.scheduleId);
     if(!persisted)throw new Error('Saved schedule not found after reload.');
     this.edit(persisted);this.message.set('Schedule saved and reloaded. Displaying persisted assignments.');
   }catch{this.message.set('Could not confirm saved assignments. Your changes are retained; retry or reload before leaving.');}
   finally{this.saving.set(false);}
 }
 edit(item:Schedule):void{this.editingId=item.scheduleId;this.form={...item,productIds:[...new Set(item.productIds)],categoryIds:[...new Set(item.categoryIds)]};this.persistedForm=this.fingerprint();}
 reset():void{this.editingId='';this.form=this.blank();this.productSearch.set('');this.categorySearch.set('');this.persistedForm=this.fingerprint();}
 async remove(item:Schedule):Promise<void>{if(!confirm(`Delete ${item.scheduleName}?`))return;await firstValueFrom(this.http.delete('/api/storefront-administration/availability-schedules/'+item.scheduleId));await this.load();}
 fingerprint():string{return JSON.stringify({...this.form,productIds:[...this.form.productIds].sort(),categoryIds:[...this.form.categoryIds].sort()});}
 isDirty():boolean{return this.persistedForm!==this.fingerprint();}
 toggleAssignment(kind:'productIds'|'categoryIds',id:string):void{const current=this.form[kind];this.form[kind]=current.includes(id)?current.filter(value=>value!==id):[...current,id];}
 choicesFor(ids:string[],kind:'products'|'categories'):Choice[]{const choices=this[kind]();return [...new Set(ids)].map((id,index)=>choices.find(choice=>choice.id===id)??{id,name:'Unavailable saved '+(kind==='products'?'product':'category')+' '+(index+1)});}
 noAssignments(item:Schedule):boolean{return item.isEnabled&&!item.productIds.length&&!item.categoryIds.length;}
 }
