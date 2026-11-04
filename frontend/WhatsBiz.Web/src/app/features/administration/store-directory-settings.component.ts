import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';
interface DirectorySettings{businessType:string;businessTypeCode:string|null;businessTypeRequiresCompletion:boolean;shortDescription:string;optedIn:boolean;ownerApproved:boolean;version:string|null;}
interface BusinessType{code:string;name:string;isActive:boolean;}
@Component({selector:'app-store-directory-settings',standalone:true,imports:[FormsModule],
 template:`<section class="catalog-panel"><header><div><p>Store discovery</p><h2>Public retailer directory</h2></div><button (click)="load()" [disabled]="busy()">Reload saved settings</button></header>
 <p>Opt in to be considered for the KhataDhari store directory. The Application Owner makes the final publication decision.</p>
 @if(error()){<p class="error" role="alert">{{error()}}</p>}@if(message()){<p role="status">{{message()}}</p>}
 @if(settings();as s){<form (ngSubmit)="save()"><label>Business type<select name="businessType" [(ngModel)]="s.businessTypeCode" required [disabled]="busy()"><option [ngValue]="null">Choose a business type</option>@for(t of types();track t.code){<option [value]="t.code">{{t.name}}</option>}</select></label>
 @if(s.businessTypeRequiresCompletion){<p class="notice">Complete your business type using an active catalog choice. @if(s.businessType){Your saved legacy label is: {{s.businessType}}.} Existing publication remains unchanged until you save.</p>}
 <label>Short business description<textarea name="shortDescription" [(ngModel)]="s.shortDescription" maxlength="160" rows="2" [disabled]="busy()" placeholder="Describe your business in one short sentence"></textarea><small>{{s.shortDescription.length}} / 160 characters. Recommended for discovery; required when opting in.</small></label>
 <label><span><input style="width:auto" type="checkbox" name="optedIn" [(ngModel)]="s.optedIn" [disabled]="busy()"> Request public directory listing</span></label>
 <p>Saved publication decision: <strong>{{s.ownerApproved?'Approved by owner':'Not published'}}</strong>. Edited content requires owner review again.</p>
 <button class="primary" type="submit" [disabled]="busy()||!s.businessTypeCode">{{busy()?'Saving…':'Save directory request'}}</button></form>}
 @else if(busy()){<p role="status">Loading directory settings…</p>}</section>`,styleUrl:'../products/public-catalog.css'})
export class StoreDirectorySettingsComponent{
 private http=inject(HttpClient);readonly types=signal<BusinessType[]>([]);readonly settings=signal<DirectorySettings|null>(null);readonly busy=signal(false);readonly error=signal('');readonly message=signal('');constructor(){this.load();}
 load(){this.busy.set(true);this.error.set('');forkJoin({settings:this.http.get<DirectorySettings>('/api/public-catalog/directory'),types:this.http.get<BusinessType[]>('/api/public-catalog/business-types')}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:r=>{this.types.set(r.types);this.settings.set({...r.settings,businessTypeCode:r.types.some(t=>t.code===r.settings.businessTypeCode)?r.settings.businessTypeCode:null});},error:()=>this.error.set('Directory settings or business types could not be loaded. Reload to retry.')});}
 save(){const s=this.settings();if(!s||this.busy())return;this.busy.set(true);this.error.set('');this.message.set('');this.http.put<DirectorySettings>('/api/public-catalog/directory',s).pipe(finalize(()=>this.busy.set(false))).subscribe({next:r=>{this.settings.set(r);this.message.set('Saved and reloaded. Owner review is required before publication.');},error:e=>this.error.set(e.error?.message||'Directory request could not be saved.')});}
}
