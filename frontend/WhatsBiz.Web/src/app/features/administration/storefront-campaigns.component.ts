import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { RichTextEditorComponent } from '../../shared/rich-text-editor.component';
import { ProductApiService } from '../products/product-api.service';
import { Category } from '../products/product.models';

interface CampaignForm { name:string;headline:string;htmlContent:string;isActive:boolean;startsAtLocal:string;endsAtLocal:string;timeZoneId:string;frequency:string;priority:number;ctaText:string|null;destinationType:string|null;destinationId:string|null;scheduleId:string|null;onlyWhenOrderingOpen:boolean; }
interface Campaign { campaignId:string;settings:CampaignForm;mediaId?:string; }
@Component({standalone:true,selector:'app-storefront-campaigns',imports:[FormsModule,RichTextEditorComponent],templateUrl:'./storefront-campaigns.component.html',styleUrl:'./storefront-campaigns.component.css'})
export class StorefrontCampaignsComponent implements OnInit,OnDestroy {
 readonly rows=signal<Campaign[]>([]);readonly schedules=signal<{scheduleId:string;scheduleName:string;timeZoneId:string}[]>([]);
 readonly busy=signal(false);readonly loading=signal(true);readonly error=signal('');readonly message=signal('');editingId='';file:File|null=null;form=this.blank();
 readonly destinations=signal<Record<string,{id:string;name:string}[]>>({CATEGORY:[],PRODUCT:[],OFFER:[]});readonly choicesError=signal('');
 readonly savedImage=signal('');readonly selectedImage=signal('');readonly imageLoading=signal(false);readonly imageError=signal('');
 private imageRequest=0;private imageKey='';private pendingImage='';
 private readonly url='/api/storefront-administration/campaigns';
 constructor(private readonly http:HttpClient,private readonly products:ProductApiService){}
 private blank():CampaignForm{return {name:'',headline:'',htmlContent:'',isActive:false,startsAtLocal:'',endsAtLocal:'',timeZoneId:'',frequency:'VISIT',priority:0,ctaText:null,destinationType:null,destinationId:null,scheduleId:null,onlyWhenOrderingOpen:false};}
 ngOnInit():void{void this.load();void this.loadDestinations();}
 async loadDestinations():Promise<void>{this.choicesError.set('');try{
   const [categories,admin]=await Promise.all([firstValueFrom(this.products.categories()),firstValueFrom(this.http.get<{promotions:{promotionId:string;offerName:string;isActive:boolean}[]}>('/api/storefront-administration'))]);
   const flatten=(rows:Category[]):{id:string;name:string}[]=>rows.flatMap(x=>[...(x.isActive?[{id:x.productCategoryId,name:x.categoryName}]:[]),...flatten(x.children??[])]);
   const items:{id:string;name:string}[]=[];let page=1,total=0,fetched=0;do{const result=await firstValueFrom(this.products.search({sortBy:'productName',descending:false,pageNumber:page,pageSize:200,isActive:true}));items.push(...result.items.filter(x=>x.isActive&&x.isWhatsAppVisible).map(x=>({id:x.productId,name:x.productName+' · '+x.productCode})));total=result.totalCount;fetched+=result.items.length;if(!result.items.length)break;page++;}while(fetched<total);
   this.destinations.set({CATEGORY:flatten(categories),PRODUCT:[...new Map(items.map(x=>[x.id,x])).values()],OFFER:admin.promotions.filter(x=>x.isActive).map(x=>({id:x.promotionId,name:x.offerName}))});
 }catch{this.choicesError.set('CTA choices could not be loaded. Retry to select a destination.');}}
 async load(refreshPreview=true):Promise<void>{this.loading.set(true);this.error.set('');try{const [rows,schedules]=await Promise.all([firstValueFrom(this.http.get<Campaign[]>(this.url)),firstValueFrom(this.http.get<{scheduleId:string;scheduleName:string;timeZoneId:string}[]>('/api/storefront-administration/availability-schedules'))]);this.rows.set(rows);this.schedules.set(schedules);if(!this.form.timeZoneId)this.form.timeZoneId=schedules[0]?.timeZoneId??'';const current=rows.find(x=>x.campaignId===this.editingId);if(refreshPreview&&current)void this.previewSaved(current);}catch{this.error.set('Campaigns could not be loaded. Retry before editing.');}finally{this.loading.set(false);}}
 edit(row:Campaign):void{this.editingId=row.campaignId;this.form={...row.settings};this.file=null;this.release(this.selectedImage);void this.previewSaved(row);this.message.set('Editing saved configuration. Changes are not saved until Save.');}
 reset():void{const zone=this.form.timeZoneId;this.editingId='';this.form=this.blank();this.form.timeZoneId=zone;this.file=null;this.clearImages();this.message.set('');}
 choose(event:Event):void{const input=event.target as HTMLInputElement;const file=input.files?.[0];input.value='';if(!file)return;if(file.size>5242880||!['image/jpeg','image/png','image/webp'].includes(file.type)){this.file=null;this.release(this.selectedImage);this.imageError.set('Choose JPG, PNG or WebP up to 5 MB.');this.error.set('Choose JPG, PNG or WebP up to 5 MB.');return;}this.file=file;this.error.set('');this.imageError.set('');this.release(this.selectedImage);this.selectedImage.set(URL.createObjectURL(file));}
 private release(image:{():string;set(value:string):void}):void{if(image())URL.revokeObjectURL(image());image.set('');}
 private clearImages():void{++this.imageRequest;this.imageKey='';this.pendingImage='';this.release(this.savedImage);this.release(this.selectedImage);this.imageLoading.set(false);this.imageError.set('');}
 ngOnDestroy():void{this.clearImages();}
 async previewSaved(row?:Campaign):Promise<void>{
   row=row??this.rows().find(x=>x.campaignId===this.editingId);if(!row)return;
   const key=row.campaignId+'/'+(row.mediaId??'');if(key===this.imageKey&&(this.savedImage()||this.pendingImage===key))return;
   const request=++this.imageRequest;this.imageKey=key;this.pendingImage=key;this.release(this.savedImage);this.imageError.set('');
   if(!row.mediaId){this.pendingImage='';this.imageLoading.set(false);return;}
   this.imageLoading.set(true);
   try{const blob=await firstValueFrom(this.http.get(`${this.url}/${row.campaignId}/image`,{responseType:'blob'}));if(request!==this.imageRequest||row.campaignId!==this.editingId)return;if(!blob.type.startsWith('image/'))throw new Error('Invalid image response');this.savedImage.set(URL.createObjectURL(blob));}
   catch{if(request===this.imageRequest)this.imageError.set('The saved banner could not be loaded. Retry preview.');}
   finally{if(request===this.imageRequest){this.imageLoading.set(false);this.pendingImage='';}}
 }
 async removeImage():Promise<void>{if(!this.editingId||this.busy())return;this.busy.set(true);try{await firstValueFrom(this.http.delete(`${this.url}/${this.editingId}/image`));await this.load();this.message.set('Saved image removed.');}catch{this.error.set('Image could not be removed.');}finally{this.busy.set(false);}}
 async save():Promise<void>{if(this.busy()||this.loading()||this.error())return;this.busy.set(true);this.message.set('');let stage='configuration';try{
   const body={...this.form,destinationType:this.form.destinationType||null,destinationId:this.form.destinationType?this.form.destinationId:null,scheduleId:this.form.scheduleId||null};
   const saved=await firstValueFrom(this.editingId?this.http.put<Campaign>(`${this.url}/${this.editingId}`,body):this.http.post<Campaign>(this.url,body));
   this.editingId=saved.campaignId;
   stage='image-upload';if(this.file){const data=new FormData();data.append('file',this.file);await firstValueFrom(this.http.post(`${this.url}/${saved.campaignId}/image`,data));}
   stage='reload';await this.load(false);const reloaded=this.rows().find(x=>x.campaignId===saved.campaignId);if(!reloaded||this.error())throw new Error('Reload failed');this.edit(reloaded);this.message.set('Saved and reloaded from the server.');
 }catch(e){const response=e instanceof HttpErrorResponse?e:null;const reference=response?.headers.get('X-Correlation-ID');const safeReference=reference&&/^[A-Za-z0-9._:\-]{1,100}$/.test(reference)?` Reference: ${reference}.`:'';const detail=response&&response.status>=400&&response.status<500?(response.error?.message??response.error?.detail??'Check the image format and size.'):'The request could not be completed.';this.error.set((stage==='image-upload'?'Campaign settings were saved, but the banner upload could not be confirmed. The current preview is retained; reload before retrying. ':stage==='reload'?'The save may have completed. Reload to confirm before retrying. ':'Campaign settings could not be saved. ')+detail+safeReference);}finally{this.busy.set(false);}}
}
