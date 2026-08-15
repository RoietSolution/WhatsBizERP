import { ChangeDetectionStrategy, Component, OnDestroy, signal } from '@angular/core';
import { catchError, forkJoin, map, of } from 'rxjs';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { DemoCart, DemoMessage, DemoOrder, DemoProduct, DemoReadiness, DemoSetup, OrderDetails, OrderSummary, WhatsAppCommerceDemoApiService } from './whatsapp-commerce-demo-api.service';

@Component({
  imports:[CurrencyPipe,DatePipe,FormsModule,RouterLink,MatButtonModule,MatFormFieldModule,MatSelectModule,PageContainerComponent,PageHeaderComponent,StatusChipComponent],
  templateUrl:'./whatsapp-commerce-demo.component.html',
  styleUrl:'./whatsapp-commerce-demo.component.scss',
  changeDetection:ChangeDetectionStrategy.OnPush,
})
export class WhatsAppCommerceDemoComponent implements OnDestroy {
  readonly readiness=signal<DemoReadiness|null>(null); readonly setup=signal<DemoSetup|null>(null); readonly messages=signal<DemoMessage[]>([]); readonly cart=signal<DemoCart|null>(null); readonly order=signal<DemoOrder|null>(null); readonly orders=signal<OrderSummary[]>([]); readonly orderDetails=signal<OrderDetails|null>(null); readonly selectedProduct=signal<DemoProduct|null>(null); readonly started=signal(false); readonly error=signal(''); readonly busy=signal(false); readonly showProducts=signal(false); readonly showCart=signal(false); readonly showOrders=signal(false);
  customerId=''; warehouseId=''; draftMessage=''; private quantities=new Map<string,number>(); private imageUrls:string[]=[];
  constructor(private readonly api:WhatsAppCommerceDemoApiService){this.checkReadiness();}
  checkReadiness(){this.busy.set(true);this.api.readiness().subscribe({next:x=>{this.readiness.set(x);this.busy.set(false);if(x.ready)this.load();},error:()=>{this.error.set('Demo readiness could not be checked.');this.busy.set(false);}});}
  load(warehouseId?:string){this.busy.set(true);this.api.setup(warehouseId).subscribe({next:x=>{this.loadImages(x);this.messages.set(x.messages);this.warehouseId=warehouseId??x.warehouses[0]?.warehouseId??'';this.customerId=x.customers[0]?.customerId??'';},error:()=>{this.error.set('MOCK mode must be enabled for this tenant before starting the simulator.');this.busy.set(false);}});}
  changeWarehouse(){this.quantities.clear();this.cart.set(null);this.load(this.warehouseId);}
  start(){this.reset();this.started.set(true);}
  browse(){this.showProducts.set(true);this.showCart.set(false);this.showOrders.set(false);this.say('Our Products','CATALOGUE');}
  view(product:DemoProduct){this.selectedProduct.set(product);}
  openCart(){this.showCart.set(true);this.showProducts.set(false);this.showOrders.set(false);this.selectedProduct.set(null);this.say('Here is your cart.','CART');}
  myOrders(){if(!this.customerId)return;this.showOrders.set(true);this.showProducts.set(false);this.showCart.set(false);this.api.orders(this.customerId).subscribe({next:x=>this.orders.set(x),error:()=>this.error.set('Orders could not be loaded.')});}
  openOrder(x:OrderSummary){this.api.orderDetails(x.orderId,this.customerId).subscribe({next:d=>this.orderDetails.set(d),error:()=>this.error.set('Order details could not be loaded.')});}
  refreshStatuses(){if(!this.customerId)return;this.api.notifications(this.customerId).subscribe({next:x=>{if(x.length)this.messages.update(m=>[...m,...x]);this.myOrders();},error:()=>this.error.set('Order statuses could not be refreshed.')});}
  sendMessage(){
    const text=this.draftMessage.trim();if(!text)return;
    this.messages.update(x=>[...x,new DemoMessageModel('CUSTOMER','TEXT',text)]);this.draftMessage='';
    const command=text.toLowerCase();
    if(command.includes('product')||command.includes('browse'))this.browse();
    else if(command.includes('cart'))this.openCart();
    else if(command.includes('order'))this.myOrders();
    else if(/^(hi|hello|hey)\b/.test(command))this.say('Hello! Choose Browse Products, My Cart, or My Orders below.','TEXT');
    else this.say('Thanks for your message. Use the options below to continue this MOCK shopping demo.','TEXT');
  }
  add(product:DemoProduct){this.quantities.set(product.productId,(this.quantities.get(product.productId)??0)+1);this.calculate();this.say(`${product.productName} added to cart.`,'CART');}
  quantity(productId:string,delta:number){const next=(this.quantities.get(productId)??0)+delta;if(next<=0)this.quantities.delete(productId);else this.quantities.set(productId,next);this.calculate();}
  remove(productId:string){this.quantities.delete(productId);this.calculate();}
  place(){if(!this.customerId||!this.warehouseId||!this.items().length)return;this.busy.set(true);this.error.set('');this.api.order(this.customerId,this.warehouseId,this.items()).subscribe({next:x=>{this.order.set(x);this.messages.update(m=>[...m,...x.messages]);this.showCart.set(false);this.quantities.clear();this.cart.set(null);this.busy.set(false);},error:()=>{this.error.set('The order could not be created. Check customer, warehouse, and stock availability.');this.busy.set(false);}});}
  reset(){this.quantities.clear();this.cart.set(null);this.order.set(null);this.orderDetails.set(null);this.selectedProduct.set(null);this.draftMessage='';this.showProducts.set(false);this.showCart.set(false);this.showOrders.set(false);this.error.set('');this.messages.set(this.setup()?.messages??[]);}
  ngOnDestroy(){this.releaseImages();}
  private loadImages(data:DemoSetup){
    this.releaseImages();
    if(!data.products.length){this.setup.set(data);this.busy.set(false);return;}
    const images=data.products.map(product=>product.imageUrl?this.api.productImage(product.productId).pipe(map(blob=>{const imageUrl=URL.createObjectURL(blob);this.imageUrls.push(imageUrl);return {...product,imageUrl};}),catchError(()=>of({...product,imageUrl:undefined}))):of(product));
    forkJoin(images).subscribe(products=>{this.setup.set({...data,products});this.busy.set(false);});
  }
  private releaseImages(){this.imageUrls.forEach(url=>URL.revokeObjectURL(url));this.imageUrls=[];}
  private calculate(){if(!this.warehouseId){return;}this.api.cart(this.warehouseId,this.items()).subscribe({next:x=>this.cart.set(x),error:()=>this.error.set('Cart could not be recalculated from current ERP pricing and stock.')});}
  private items(){return [...this.quantities].map(([productId,quantity])=>({productId,quantity}));}
  private say(text:string,kind:string){this.messages.update(x=>[...x,new DemoMessageModel('WHATS_BIZ',kind,text)]);}
}
class DemoMessageModel implements DemoMessage { constructor(public sender:string,public kind:string,public text:string){} }
