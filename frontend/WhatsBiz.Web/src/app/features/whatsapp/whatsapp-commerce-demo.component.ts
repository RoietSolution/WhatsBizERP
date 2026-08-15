import { ChangeDetectionStrategy, Component, computed, OnDestroy, signal } from '@angular/core';
import { catchError, forkJoin, map, of } from 'rxjs';
import { CurrencyPipe, DatePipe, NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { DemoCart, DemoCategory, DemoMessage, DemoOrder, DemoProduct, DemoReadiness, DemoSetup, OrderDetails, OrderSummary, WhatsAppCommerceDemoApiService } from './whatsapp-commerce-demo-api.service';

type CatalogView='home'|'categories'|'products'|'detail'|'cart'|'orders';
@Component({
  imports:[CurrencyPipe,DatePipe,NgTemplateOutlet,FormsModule,RouterLink,MatButtonModule,MatFormFieldModule,MatSelectModule,PageContainerComponent,PageHeaderComponent,StatusChipComponent],
  templateUrl:'./whatsapp-commerce-demo.component.html', styleUrl:'./whatsapp-commerce-demo.component.scss', changeDetection:ChangeDetectionStrategy.OnPush,
})
export class WhatsAppCommerceDemoComponent implements OnDestroy {
  readonly readiness=signal<DemoReadiness|null>(null); readonly setup=signal<DemoSetup|null>(null); readonly cart=signal<DemoCart|null>(null); readonly order=signal<DemoOrder|null>(null); readonly orders=signal<OrderSummary[]>([]); readonly orderDetails=signal<OrderDetails|null>(null); readonly selectedProduct=signal<DemoProduct|null>(null); readonly started=signal(false); readonly error=signal(''); readonly busy=signal(false); readonly view=signal<CatalogView>('home'); readonly search=signal(''); readonly selectedCategory=signal<DemoCategory|null>(null);
  readonly selectedImageIndex=signal(0);
  readonly visibleProducts=computed(()=>{const data=this.setup();if(!data)return[];const term=this.search().trim().toLowerCase();const category=this.selectedCategory()?.categoryId;return data.products.filter(x=>(!category||x.categoryId===category)&&(!term||`${x.productName} ${x.productCode} ${x.categoryName}`.toLowerCase().includes(term)));});
  customerId=''; warehouseId=''; private quantities=new Map<string,number>(); private imageUrls:string[]=[];
  constructor(private readonly api:WhatsAppCommerceDemoApiService){this.checkReadiness();}
  checkReadiness(){this.busy.set(true);this.api.readiness().subscribe({next:x=>{this.readiness.set(x);this.busy.set(false);if(x.ready)this.load();},error:()=>{this.error.set('Demo readiness could not be checked.');this.busy.set(false);}});}
  load(warehouseId?:string){this.busy.set(true);this.api.setup(warehouseId).subscribe({next:x=>{this.loadImages(x);this.warehouseId=warehouseId??x.warehouses[0]?.warehouseId??'';this.customerId=x.customers[0]?.customerId??'';},error:()=>{this.error.set('MOCK mode must be enabled for this tenant before starting the simulator.');this.busy.set(false);}});}
  changeWarehouse(){this.quantities.clear();this.cart.set(null);this.selectedCategory.set(null);this.load(this.warehouseId);}
  start(){this.reset();this.started.set(true);}
  go(next:CatalogView){this.view.set(next);if(next!=='products')this.selectedCategory.set(null);if(next!=='detail')this.selectedProduct.set(null);if(next==='orders')this.loadOrders();}
  chooseCategory(category:DemoCategory){this.selectedCategory.set(category);this.search.set('');this.view.set('products');}
  showAllProducts(){this.selectedCategory.set(null);this.view.set('products');}
  openProduct(product:DemoProduct){this.selectedProduct.set(product);this.selectedImageIndex.set(0);this.view.set('detail');}
  back(){if(this.view()==='detail')this.view.set('products');else if(this.view()==='products')this.view.set(this.selectedCategory()?'categories':'home');else this.view.set('home');}
  add(product:DemoProduct){if(product.availableQuantity<=0)return;this.quantities.set(product.productId,(this.quantities.get(product.productId)??0)+1);this.calculate();}
  quantity(productId:string,delta:number){const next=(this.quantities.get(productId)??0)+delta;if(next<=0)this.quantities.delete(productId);else this.quantities.set(productId,next);this.calculate();}
  remove(productId:string){this.quantities.delete(productId);this.calculate();}
  cartCount(){return [...this.quantities.values()].reduce((a,b)=>a+b,0);}
  quantityFor(id:string){return this.quantities.get(id)??0;}
  loadOrders(){if(!this.customerId)return;this.api.orders(this.customerId).subscribe({next:x=>this.orders.set(x),error:()=>this.error.set('Orders could not be loaded.')});}
  openOrder(x:OrderSummary){this.api.orderDetails(x.orderId,this.customerId).subscribe({next:d=>this.orderDetails.set(d),error:()=>this.error.set('Order details could not be loaded.')});}
  refreshStatuses(){if(!this.customerId)return;this.api.notifications(this.customerId).subscribe({next:()=>this.loadOrders(),error:()=>this.error.set('Order statuses could not be refreshed.')});}
  place(){if(!this.customerId||!this.warehouseId||!this.items().length)return;this.busy.set(true);this.error.set('');this.api.order(this.customerId,this.warehouseId,this.items()).subscribe({next:x=>{this.order.set(x);this.quantities.clear();this.cart.set(null);this.busy.set(false);},error:()=>{this.error.set('The order could not be created. Check customer, warehouse, and stock availability.');this.busy.set(false);}});}
  reset(){this.quantities.clear();this.cart.set(null);this.order.set(null);this.orderDetails.set(null);this.selectedProduct.set(null);this.selectedCategory.set(null);this.search.set('');this.view.set('home');this.error.set('');}
  ngOnDestroy(){this.releaseImages();}
  private loadImages(data:DemoSetup){this.releaseImages();if(!data.products.length){this.setup.set(data);this.busy.set(false);return;}const images=data.products.map(product=>{const urls=product.imageUrls?.length?product.imageUrls:(product.imageUrl?[product.imageUrl]:[]);if(!urls.length)return of(product);return forkJoin(urls.slice(0,5).map(url=>this.api.productImageUrl(url).pipe(map(blob=>{const imageUrl=URL.createObjectURL(blob);this.imageUrls.push(imageUrl);return imageUrl;}),catchError(()=>of(''))))).pipe(map(imageUrls=>{const valid=imageUrls.filter(Boolean);return {...product,imageUrl:valid[0]??product.imageUrl,imageUrls:valid};}),catchError(()=>of(product)));});forkJoin(images).subscribe(products=>{const categories=data.categories.map(category=>({...category,imageUrl:products.find(p=>p.productId===category.imageProductId)?.imageUrl}));this.setup.set({...data,products,categories});this.busy.set(false);});}
  private releaseImages(){this.imageUrls.forEach(url=>URL.revokeObjectURL(url));this.imageUrls=[];}
  private calculate(){if(!this.warehouseId)return;this.api.cart(this.warehouseId,this.items()).subscribe({next:x=>this.cart.set(x),error:()=>this.error.set('Cart could not be recalculated from current ERP pricing and stock.')});}
  private items(){return [...this.quantities].map(([productId,quantity])=>({productId,quantity}));}
}
