import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
interface BusinessType{code:string;name:string;isActive:boolean;version:string;}
@Component({selector:'app-business-types',standalone:true,imports:[FormsModule],
 template:`<section class="catalog-panel"><header><div><p>Application Owner · Shared onboarding catalog</p><h2>Business types</h2></div><button (click)="load()" [disabled]="busy()">Reload / Retry</button></header>
 <p>Normalized choices for all retailers. Disabling a choice prevents new assignments; it does not delete existing retailer information or automatically unpublish stores.</p>
 @if(error()){<p class="error" role="alert">{{error()}}</p>}@if(message()){<p role="status">{{message()}}</p>}
 <form (ngSubmit)="save()"><div class="detail-grid"><label>Stable code<input name="code" [(ngModel)]="code" maxlength="40" required pattern="[A-Za-z][A-Za-z0-9_]*" [disabled]="busy()||!!version" placeholder="E.g. HOME_GOODS"></label>
 <label>Display name<input name="name" [(ngModel)]="name" maxlength="80" required [disabled]="busy()" placeholder="Friendly business type name"></label></div>
 <label><span><input style="width:auto" type="checkbox" name="active" [(ngModel)]="active" [disabled]="busy()"> Available for new assignments</span></label>
 <div class="actions"><button class="primary" [disabled]="busy()||!code.trim()||!name.trim()">{{version?'Save business type':'Add business type'}}</button><button type="button" (click)="reset()" [disabled]="busy()">New / Cancel edit</button></div></form>
 <div class="queue">@for(t of types();track t.code){<article><div><h3>{{t.name}}</h3><p>{{t.code}} · {{t.isActive?'Active':'Disabled for new assignments'}}</p></div><button (click)="edit(t)" [disabled]="busy()">Edit</button></article>}@empty{<p>No business types loaded. Reload or configure a choice.</p>}</div></section>`,
 styleUrl:'../products/public-catalog.css'})
export class BusinessTypesComponent{
 private http=inject(HttpClient);readonly types=signal<BusinessType[]>([]);readonly busy=signal(false);readonly error=signal('');readonly message=signal('');code='';name='';active=true;version:string|null=null;
 constructor(){this.load();}
 load(){this.busy.set(true);this.error.set('');this.http.get<BusinessType[]>('/api/public-catalog/owner/business-types').pipe(finalize(()=>this.busy.set(false))).subscribe({next:r=>this.types.set(r),error:()=>this.error.set('Business types could not be loaded. Reload to retry.')});}
 edit(t:BusinessType){this.code=t.code;this.name=t.name;this.active=t.isActive;this.version=t.version;this.message.set('');}
 reset(){this.code='';this.name='';this.active=true;this.version=null;}
 save(){if(this.busy())return;this.busy.set(true);this.error.set('');this.message.set('');this.http.put<BusinessType[]>('/api/public-catalog/owner/business-types',{code:this.code,name:this.name,isActive:this.active,version:this.version}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:r=>{this.types.set(r);this.reset();this.message.set('Saved. Download a new onboarding template to include catalog changes.');},error:e=>this.error.set(e.error?.message||e.error?.detail||'Business type could not be saved. Reload before retrying.')});}
}
