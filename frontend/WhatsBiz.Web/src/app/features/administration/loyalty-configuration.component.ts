import { DatePipe } from '@angular/common';
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
import { CustomerApiService } from '../customers/customer-api.service';
import { CustomerList } from '../customers/customer.models';
import { CategoryCoinRule, CoinConfiguration, CoinWallet, LoyaltyApiService, ProductCoinRule } from './loyalty-api.service';

@Component({
  imports:[DatePipe,FormsModule,MatButtonModule,MatCheckboxModule,MatFormFieldModule,MatInputModule,MatSelectModule,MatSlideToggleModule],
  templateUrl:'./loyalty-configuration.component.html',
  styles:[`.page{display:grid;gap:18px}.heading,.section-head,.rule{display:flex;align-items:center;gap:12px}.heading,.section-head{justify-content:space-between}.card{padding:20px;border:1px solid #dce5df;border-radius:14px;background:#fff}.grid{display:grid;grid-template-columns:repeat(3,minmax(180px,1fr));gap:12px}.rule-picker{display:grid;grid-template-columns:1fr auto;gap:10px}.rule{padding:10px 0;border-bottom:1px solid #edf1ee}.rule>span{flex:1}.rule mat-form-field{width:130px}.hint{color:#627269;font-size:12px}.error{color:#b42318}.actions{display:flex;justify-content:flex-end}.wallet-picker{width:min(520px,100%)}.wallet-summary{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin:6px 0 18px}.wallet-summary div{display:flex;flex-direction:column;padding:14px;border-radius:12px;background:#fff8df;color:#725b18}.wallet-summary strong{color:#352c15;font-size:24px}.ledger{width:100%;border-collapse:collapse}.ledger th,.ledger td{padding:10px;border-bottom:1px solid #edf1ee;text-align:left}.ledger th{color:#627269;font-size:11px;text-transform:uppercase}.coin-positive{color:#008069;font-weight:800}.coin-negative{color:#b42318;font-weight:800}@media(max-width:800px){.grid,.wallet-summary{grid-template-columns:1fr}.rule{align-items:flex-start;flex-wrap:wrap}.ledger{font-size:12px}}`],
  changeDetection:ChangeDetectionStrategy.OnPush,
})
export class LoyaltyConfigurationComponent {
  readonly busy=signal(true); readonly error=signal(''); readonly products=signal<ProductListItem[]>([]); readonly categories=signal<Category[]>([]); readonly customers=signal<CustomerList[]>([]); readonly wallet=signal<CoinWallet|null>(null); readonly walletBusy=signal(false);
  config:CoinConfiguration={isEnabled:false,purchaseAmount:100,purchaseCoins:1,earningPriority:'PRODUCT_FIRST',awardOrderStatus:'DELIVERED',redemptionCoins:100,redemptionValue:10,minimumRedemptionCoins:100,maximumRedemptionCoins:null,allowWithOtherDiscounts:false,restoreRedeemedOnCancel:true,restoreRedeemedOnRefund:true,productRules:[],categoryRules:[]};
  productId=''; categoryId=''; customerId='';
  constructor(private readonly loyalty:LoyaltyApiService,private readonly productApi:ProductApiService,private readonly customerApi:CustomerApiService,private readonly snack:MatSnackBar){this.load();}
  load(){this.busy.set(true);forkJoin({config:this.loyalty.configuration(),products:this.productApi.search({sortBy:'productName',descending:false,pageNumber:1,pageSize:200,isActive:true}),categories:this.productApi.categories(),customers:this.customerApi.search({sortBy:'customerName',descending:false,pageNumber:1,pageSize:200,isActive:true})}).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.config=x.config;this.products.set(x.products.items);this.categories.set(this.flatten(x.categories));this.customers.set(x.customers.items);},error:()=>this.error.set('Loyalty configuration could not be loaded. Apply database V13 before using this page.')});}
  loadWallet(){if(!this.customerId){this.wallet.set(null);return;}this.walletBusy.set(true);this.loyalty.wallet(this.customerId).pipe(finalize(()=>this.walletBusy.set(false))).subscribe({next:x=>this.wallet.set(x),error:()=>{this.wallet.set(null);this.error.set('The selected customer wallet could not be loaded.');}});}
  addProduct(){const product=this.products().find(x=>x.productId===this.productId);if(!product||this.config.productRules.some(x=>x.productId===product.productId))return;this.config.productRules=[...this.config.productRules,{productId:product.productId,productCode:product.productCode,productName:product.productName,isEnabled:true,coinsPerUnit:0}];this.productId='';}
  addCategory(){const category=this.categories().find(x=>x.productCategoryId===this.categoryId);if(!category||this.config.categoryRules.some(x=>x.productCategoryId===category.productCategoryId))return;this.config.categoryRules=[...this.config.categoryRules,{productCategoryId:category.productCategoryId,categoryCode:category.categoryCode,categoryName:category.categoryName,isEnabled:true,coinsPerUnit:0}];this.categoryId='';}
  removeProduct(rule:ProductCoinRule){this.config.productRules=this.config.productRules.filter(x=>x!==rule);}
  removeCategory(rule:CategoryCoinRule){this.config.categoryRules=this.config.categoryRules.filter(x=>x!==rule);}
  save(){this.busy.set(true);this.error.set('');this.loyalty.save(this.config).pipe(finalize(()=>this.busy.set(false))).subscribe({next:x=>{this.config=x;this.snack.open('Coin configuration saved.','Close',{duration:2500});},error:()=>this.error.set('Coin configuration could not be saved. Check rates, limits, and rules.')});}
  private flatten(items:Category[]):Category[]{return items.flatMap(x=>[x,...this.flatten(x.children??[])]);}
}
