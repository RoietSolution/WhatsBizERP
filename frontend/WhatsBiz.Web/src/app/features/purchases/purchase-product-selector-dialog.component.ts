import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogActions, MatDialogClose, MatDialogContent, MatDialogRef, MatDialogTitle } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { ProductApiService } from '../products/product-api.service';
import { Category, ProductListItem } from '../products/product.models';

@Component({
  selector: 'app-purchase-product-selector-dialog',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatDialogTitle, MatDialogContent, MatDialogActions, MatDialogClose],
  template: `
    <h2 mat-dialog-title>Add products to purchase</h2>
    <mat-dialog-content>
      <div class="filters">
        <mat-form-field appearance="outline"><mat-label>Search products</mat-label><input matInput [(ngModel)]="search" (keyup.enter)="applyFilters()" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Category</mat-label><mat-select [(ngModel)]="categoryId" (selectionChange)="applyFilters()"><mat-option value="">All categories</mat-option>@for (x of categories(); track x.productCategoryId) { <mat-option [value]="x.productCategoryId">{{ x.categoryName }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Brand</mat-label><mat-select [(ngModel)]="brandId" (selectionChange)="applyFilters()"><mat-option value="">All brands</mat-option>@for (x of brands(); track x.brandId) { <mat-option [value]="x.brandId">{{ x.brandName }}</mat-option> }</mat-select></mat-form-field>
        <button mat-stroked-button type="button" (click)="applyFilters()">Search</button>
      </div>
      <div class="selection-bar"><strong>{{ selected().size }} selected</strong><span>{{ total() }} filtered product(s)</span><button mat-button type="button" [disabled]="loading() || !total()" (click)="selectAllFiltered()">Select All Filtered</button><button mat-button type="button" [disabled]="!selected().size" (click)="clear()">Clear Selection</button></div>
      <div class="list" [class.busy]="loading()">
        @for (product of products(); track product.productId) {
          <label class="row"><mat-checkbox [checked]="selected().has(product.productId)" (change)="toggle(product, $event.checked)" /><span><strong>{{ product.productName }}</strong><small>{{ product.productCode }} @if (product.barcode) { · {{ product.barcode }} }</small></span><span>{{ product.categoryName }}</span><span>{{ product.brandName }}</span></label>
        } @empty { <p class="empty">{{ loading() ? 'Loading products…' : 'No products match these filters.' }}</p> }
      </div>
      <div class="pager"><button mat-button type="button" [disabled]="page() === 1 || loading()" (click)="go(page()-1)">Previous</button><span>Page {{ page() }} of {{ pageCount() }}</span><button mat-button type="button" [disabled]="page() >= pageCount() || loading()" (click)="go(page()+1)">Next</button></div>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Cancel</button><button mat-stroked-button type="button" [disabled]="loading() || !total()" (click)="addAll()">Add All Products</button><button mat-flat-button color="primary" type="button" [disabled]="!selected().size" (click)="addSelected()">Add Selected Products ({{ selected().size }})</button></mat-dialog-actions>
  `,
  styles: [`:host{display:block}.filters{display:grid;grid-template-columns:2fr 1fr 1fr auto;gap:10px;align-items:start}.selection-bar{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:8px}.selection-bar span{color:#667085}.list{max-height:440px;overflow:auto;border:1px solid #d9e1dc;border-radius:10px}.list.busy{opacity:.55}.row{display:grid;grid-template-columns:42px minmax(190px,2fr) minmax(100px,1fr) minmax(100px,1fr);align-items:center;gap:8px;padding:10px;border-bottom:1px solid #edf0ee}.row:last-child{border:0}.row span{min-width:0}.row strong,.row small{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.row small{color:#667085}.empty{padding:35px;text-align:center;color:#667085}.pager{display:flex;justify-content:center;align-items:center;gap:12px;margin-top:8px}@media(max-width:700px){.filters{grid-template-columns:1fr}.row{grid-template-columns:38px minmax(0,1fr)}.row>span:nth-last-child(-n+2){display:none}mat-dialog-content{padding-inline:14px!important}}`],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PurchaseProductSelectorDialogComponent implements OnInit {
  readonly products=signal<ProductListItem[]>([]);readonly categories=signal<Category[]>([]);readonly brands=signal<Array<{brandId:string;brandName:string}>>([]);readonly selected=signal(new Map<string,ProductListItem>());readonly total=signal(0);readonly page=signal(1);readonly loading=signal(false);readonly pageSize=50;readonly pageCount=computed(()=>Math.max(1,Math.ceil(this.total()/this.pageSize)));search='';categoryId='';brandId='';
  constructor(private readonly api:ProductApiService,private readonly ref:MatDialogRef<PurchaseProductSelectorDialogComponent>){ }
  ngOnInit(){this.api.categories().subscribe(x=>this.categories.set(this.flatten(x)));this.api.brands().subscribe(x=>this.brands.set(x));this.load();}
  applyFilters(){this.page.set(1);this.load();}go(page:number){this.page.set(page);this.load();}
  load(){this.loading.set(true);this.api.search({search:this.search.trim()||undefined,isActive:true,categoryId:this.categoryId||undefined,brandId:this.brandId||undefined,sortBy:'productName',descending:false,pageNumber:this.page(),pageSize:this.pageSize}).subscribe({next:x=>{this.products.set(x.items);this.total.set(x.totalCount);this.loading.set(false);},error:()=>this.loading.set(false)});}
  toggle(product:ProductListItem,checked:boolean){this.selected.update(current=>{const next=new Map(current);checked?next.set(product.productId,product):next.delete(product.productId);return next;});}
  clear(){this.selected.set(new Map());}
  async selectAllFiltered(){const rows=await this.fetchAllFiltered();if(rows)this.selected.set(new Map(rows.map(x=>[x.productId,x])));}
  async addAll(){if(!confirm(`Add all ${this.total()} products matching the current filters? This is a deliberate bulk action.`))return;const rows=await this.fetchAllFiltered();if(rows)this.ref.close(rows);}
  addSelected(){this.ref.close([...this.selected().values()]);}
  private async fetchAllFiltered():Promise<ProductListItem[]|null>{if(this.total()>5000&&!confirm(`This filter matches ${this.total()} products. Continue loading all filtered products?`))return null;this.loading.set(true);try{const all:ProductListItem[]=[];const pages=Math.ceil(this.total()/200);for(let page=1;page<=pages;page++){const result=await firstValueFrom(this.api.search({search:this.search.trim()||undefined,isActive:true,categoryId:this.categoryId||undefined,brandId:this.brandId||undefined,sortBy:'productName',descending:false,pageNumber:page,pageSize:200}));all.push(...result.items);}return all;}finally{this.loading.set(false);}}
  private flatten(rows:Category[]):Category[]{return rows.flatMap(x=>[x,...this.flatten(x.children??[])]);}
}