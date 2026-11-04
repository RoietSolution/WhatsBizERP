import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { environment } from '../../environments/environment';

interface DirectoryStore {storeKey:string;name:string;businessType:string;shortDescription:string;logoUrl:string|null;}
@Component({
  standalone:true,imports:[FormsModule,RouterLink],
  template:`<main><header><a routerLink="/" aria-label="KhataDhari Shop home">KhataDhari <span>shop</span></a>
    <h1>Explore Local Stores</h1><p>Discover and shop your local stores.</p></header>
    <section class="filters" aria-label="Find a store"><label class="search">Search stores<input type="search" placeholder="Store name or description" [ngModel]="search()" (ngModelChange)="search.set($event)"></label>
    <label class="type">Business type<select [ngModel]="type()" (ngModelChange)="type.set($event)"><option value="">All types</option>@for(t of types();track t){<option [value]="t">{{t}}</option>}</select></label>
    @if(hasFilters()){<button class="clear" (click)="clearFilters()">Clear filters</button>}</section>
    @if(loading()){<p role="status">Finding stores…</p>}@else if(error()){<p role="alert">{{error()}} <button (click)="load()">Retry</button></p>}
    @else{<section class="stores" aria-label="Retailer directory">@for(s of visible();track s.storeKey){<article><div class="logo">@if(s.logoUrl&&!failed().has(s.storeKey)){<img [src]="asset(s.logoUrl)" [alt]="s.name+' logo'" (error)="imageFailed(s.storeKey)">}@else{<span aria-hidden="true">{{s.name.slice(0,1)}}</span>}</div>
    <div class="store-info"><small>{{s.businessType||'Local retailer'}}</small><h2>{{s.name}}</h2><p class="description" [title]="s.shortDescription||'Explore this local retailer.'">{{s.shortDescription||'Explore this local retailer.'}}</p><a class="explore" [routerLink]="['/',s.storeKey]" [attr.aria-label]="'Explore '+s.name">Explore Store <span aria-hidden="true">→</span></a></div></article>}
    @empty{<div class="empty" role="status">@if(stores().length&&hasFilters()){<h2>No stores match your search</h2><p>Try another name or business type.</p><button (click)="clearFilters()">Clear filters</button>}@else{<h2>No stores available yet</h2><p>Local retailers will appear here once their listings are published.</p>}</div>}</section>}</main>`,
  styleUrl:'./store-directory.page.css'
})
export class StoreDirectoryPage {
  private http=inject(HttpClient);readonly stores=signal<DirectoryStore[]>([]);readonly loading=signal(false);readonly error=signal('');readonly search=signal('');readonly type=signal('');readonly failed=signal(new Set<string>());
  readonly types=computed(()=>[...new Set(this.stores().map(s=>s.businessType).filter(Boolean))].sort());
  readonly hasFilters=computed(()=>!!(this.search().trim()||this.type()));
  readonly visible=computed(()=>this.stores().filter(s=>(!this.type()||s.businessType===this.type())&&`${s.name} ${s.businessType} ${s.shortDescription}`.toLowerCase().includes(this.search().trim().toLowerCase())));
  clearFilters(){this.search.set('');this.type.set('');}
  constructor(){this.load();}
  load(){this.loading.set(true);this.error.set('');this.http.get<DirectoryStore[]>(`${environment.apiBaseUrl}/api/store-directory`).subscribe({next:s=>{this.stores.set(s);this.loading.set(false);},error:()=>{this.error.set('Stores could not be loaded. Please try again.');this.loading.set(false);}});}
  asset(path:string){return `${environment.apiBaseUrl}${path}`;}
  imageFailed(key:string){this.failed.update(s=>new Set([...s,key]));}
}
