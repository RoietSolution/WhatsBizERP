import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { finalize, forkJoin } from 'rxjs';
import { ProductApiService } from '../products/product-api.service';
import { Category, ProductListItem } from '../products/product.models';
import { CategoryCoinRule, CoinConfiguration, LoyaltyApiService, ProductCoinRule } from './loyalty-api.service';

@Component({
  imports:[FormsModule,MatButtonModule,MatCheckboxModule,MatFormFieldModule,MatInputModule,MatSelectModule,MatSlideToggleModule],
  templateUrl:'./loyalty-configuration.component.html',
  styles:[`.page{display:grid;gap:18px}.heading,.section-head,.rule{display:flex;align-items:center;gap:12px}.heading,.section-head{justify-content:space-between}.card{padding:20px;border:1px solid #dce5df;border-radius:14px;background:#fff}.grid{display:grid;grid-template-columns:repeat(3,minmax(180px,1fr));gap:12px}.rule-picker{display:grid;grid-template-columns:1fr auto;gap:10px}.rule{padding:10px 0;border-bottom:1px solid #edf1ee}.rule>span{flex:1}.rule mat-form-field{width:130px}.hint{color:#627269;font-size:12px}.error{color:#b42318}.actions{display:flex;justify-content:flex-end}@media(max-width:800px){.grid{grid-template-columns:1fr}.rule{align-items:flex-start;flex-wrap:wrap}}`],
  changeDetection:ChangeDetectionStrategy.OnPush,
})
export class LoyaltyConfigurationComponent {
  readonly busy=signal(true); readonly error=signal(''); readonly products=signal<ProductListItem[]>([]); readonly categories=signal<Category[]>([]);
  config:CoinConfiguration={isEnabled:false,purchaseAmount:100,purchaseCoins:1,earningPriority:'PRODUCT_FIRST',awardOrderStatus:'DELIVERED',redemptionCoins:100,redemptionValue:10,minimumRedemptionCoins:100,maximumRedemptionCoins:null,allowWithOtherDiscounts:false,restoreRedeemedOnCancel:true,restoreRedeemedOnRefund:true,productRules:[],categoryRules:[]};
  productId=''; categoryId='';
  constructor(private readonly loyalty:LoyaltyApiService,private readonly productApi:ProductApiService,private readonly snack:MatSnackBar){this.load();}
  load(){this.busy.set(true);forkJoin({config:this.loyalty.configuration(),products:this.productApi.search({sortBy:'productName',descending:false,pageNumber:1,pageSize:200,isActive:true}),categories:this.productApi.categories()}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.config=x.config;this.products.set(x.products.items);this.categories.set(this.flatten(x.categories));},error:()=>this.error.set('Loyalty configuration could not be loaded. Apply database V13 before using this page.')});}
  addProduct(){const product=this.products().find(x=>x.productId===this.productId);if(!product||this.config.productRules.some(x=>x.productId===product.productId))return;this.config.productRules=[...this.config.productRules,{productId:product.productId,productCode:product.productCode,productName:product.productName,isEnabled:true,coinsPerUnit:0}];this.productId='';}
  addCategory(){const category=this.categories().find(x=>x.productCategoryId===this.categoryId);if(!category||this.config.categoryRules.some(x=>x.productCategoryId===category.productCategoryId))return;this.config.categoryRules=[...this.config.categoryRules,{productCategoryId:category.productCategoryId,categoryCode:category.categoryCode,categoryName:category.categoryName,isEnabled:true,coinsPerUnit:0}];this.categoryId='';}
  removeProduct(rule:ProductCoinRule){this.config.productRules=this.config.productRules.filter(x=>x!==rule);}
  removeCategory(rule:CategoryCoinRule){this.config.categoryRules=this.config.categoryRules.filter(x=>x!==rule);}
  save(){this.busy.set(true);this.error.set('');this.loyalty.save(this.config).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.config=x;this.snack.open('Coin configuration saved.','Close',{duration:2500});},error:()=>this.error.set('Coin configuration could not be saved. Check rates, limits, and rules.')});}
  private flatten(items:Category[]):Category[]{return items.flatMap(x=>[x,...this.flatten(x.children??[])]);}
}
