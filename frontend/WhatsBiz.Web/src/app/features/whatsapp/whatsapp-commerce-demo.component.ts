import { ChangeDetectionStrategy, Component, computed, OnDestroy, signal } from '@angular/core';
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
import { DemoCart, DemoCategory, DemoMessage, DemoOrder, DemoProduct, DemoReadiness, DemoSetup, OrderDetails, OrderSummary, WhatsAppCommerceDemoApiService } from './whatsapp-commerce-demo-api.service';

type CatalogView='home'|'categories'|'products'|'detail'|'cart'|'orders';
@Component({
  imports:[CurrencyPipe,DatePipe,FormsModule,RouterLink,MatButtonModule,MatFormFieldModule,MatSelectModule,PageContainerComponent,PageHeaderComponent,StatusChipComponent],
  templateUrl:'./whatsapp-commerce-demo.component.html', styleUrl:'./whatsapp-commerce-demo.component.scss', changeDetection:ChangeDetectionStrategy.OnPush,
})
export class WhatsAppCommerceDemoComponent implements OnDestroy {
  readonly readiness=signal<DemoReadiness|null>(null); readonly setup=signal<DemoSetup|null>(null); readonly messages=signal<DemoMessage[]>([]); readonly cart=signal<DemoCart|null>(null); readonly order=signal<DemoOrder|null>(null); readonly orders=signal<OrderSummary[]>([]); readonly orderDetails=signal<OrderDetails|null>(null); readonly selectedProduct=signal<DemoProduct|null>(null); readonly started=signal(false); readonly error=signal(''); readonly busy=signal(false); readonly view=signal<CatalogView>('home'); readonly search=signal(''); readonly selectedCategory=signal<DemoCategory|null>(null); readonly showCategories=signal(false); readonly showProducts=signal(false); readonly showCart=signal(false); readonly showOrders=signal(false);
  readonly selectedImageIndex=signal(0);
  readonly assistantProductIds=signal<Set<string>|null>(null);
  readonly visibleProducts=computed(()=>{const data=this.setup();if(!data)return[];const term=this.search().trim().toLowerCase();const category=this.selectedCategory()?.categoryId;const assistantIds=this.assistantProductIds();return data.products.filter(x=>(!assistantIds||assistantIds.has(x.productId))&&(!category||x.categoryId===category)&&(!term||`${x.productName} ${x.productCode} ${x.categoryName}`.toLowerCase().includes(term)));});
  customerId=''; warehouseId=''; draftMessage=''; private quantities=new Map<string,number>(); private imageUrls:string[]=[];
  constructor(private readonly api:WhatsAppCommerceDemoApiService){this.checkReadiness();}
  checkReadiness(){this.busy.set(true);this.api.readiness().subscribe({next:x=>{this.readiness.set(x);this.busy.set(false);if(x.ready)this.load();},error:()=>{this.error.set('Demo readiness could not be checked.');this.busy.set(false);}});}
  load(warehouseId?:string){this.busy.set(true);this.api.setup(warehouseId).subscribe({next:x=>{this.loadImages(x);this.messages.set(x.messages);this.warehouseId=warehouseId??x.warehouses[0]?.warehouseId??'';this.customerId=x.customers[0]?.customerId??'';},error:()=>{this.error.set('MOCK mode must be enabled for this tenant before starting the simulator.');this.busy.set(false);}});}
  changeWarehouse(){this.quantities.clear();this.cart.set(null);this.selectedCategory.set(null);this.load(this.warehouseId);}
  start(){this.reset();this.started.set(true);}
  browse(){this.assistantProductIds.set(null);this.selectedCategory.set(null);this.selectedProduct.set(null);this.showCategories.set(true);this.showProducts.set(false);this.showCart.set(false);this.showOrders.set(false);this.say('Choose a category to browse our products.','CATALOGUE');}
  openCart(){this.showCart.set(true);this.showCategories.set(false);this.showProducts.set(false);this.showOrders.set(false);this.selectedProduct.set(null);this.say('Here is your cart.','CART');}
  myOrders(){if(!this.customerId)return;this.showOrders.set(true);this.showCategories.set(false);this.showProducts.set(false);this.showCart.set(false);this.loadOrders();}
  sendMessage(){const text=this.draftMessage.trim();if(!text)return;this.messages.update(x=>[...x,{sender:'CUSTOMER',kind:'TEXT',text}]);this.draftMessage='';this.respondToMessage(text);}
  go(next:CatalogView){this.view.set(next);if(next!=='products')this.selectedCategory.set(null);if(next!=='detail')this.selectedProduct.set(null);if(next==='orders')this.loadOrders();}
  chooseCategory(category:DemoCategory){this.assistantProductIds.set(null);this.selectedCategory.set(category);this.selectedProduct.set(null);this.search.set('');this.showCategories.set(false);this.showProducts.set(true);this.view.set('products');this.say(`${category.categoryName} products`,'CATALOGUE');}
  showAllProducts(){this.selectedCategory.set(null);this.view.set('products');}
  openProduct(product:DemoProduct){this.selectedProduct.set(product);this.selectedImageIndex.set(0);}
  back(){if(this.view()==='detail')this.view.set('products');else if(this.view()==='products')this.view.set(this.selectedCategory()?'categories':'home');else this.view.set('home');}
  add(product:DemoProduct){if(product.availableQuantity<=0)return;this.quantities.set(product.productId,(this.quantities.get(product.productId)??0)+1);this.calculate();this.say(`${product.productName} added to cart.`,'CART');}
  quantity(productId:string,delta:number){const next=(this.quantities.get(productId)??0)+delta;if(next<=0)this.quantities.delete(productId);else this.quantities.set(productId,next);this.calculate();}
  remove(productId:string){this.quantities.delete(productId);this.calculate();}
  cartCount(){return [...this.quantities.values()].reduce((a,b)=>a+b,0);}
  quantityFor(id:string){return this.quantities.get(id)??0;}
  loadOrders(){if(!this.customerId)return;this.api.orders(this.customerId).subscribe({next:x=>this.orders.set(x),error:()=>this.error.set('Orders could not be loaded.')});}
  openOrder(x:OrderSummary){this.api.orderDetails(x.orderId,this.customerId).subscribe({next:d=>this.orderDetails.set(d),error:()=>this.error.set('Order details could not be loaded.')});}
  refreshStatuses(){if(!this.customerId)return;this.api.notifications(this.customerId).subscribe({next:x=>{if(x.length)this.messages.update(m=>[...m,...x]);this.myOrders();},error:()=>this.error.set('Order statuses could not be refreshed.')});}
  place(){if(!this.customerId||!this.warehouseId||!this.items().length)return;this.busy.set(true);this.error.set('');this.api.order(this.customerId,this.warehouseId,this.items()).subscribe({next:x=>{this.order.set(x);this.messages.update(m=>[...m,...x.messages]);this.showCart.set(false);this.quantities.clear();this.cart.set(null);this.busy.set(false);},error:()=>{this.error.set('The order could not be created. Check customer, warehouse, and stock availability.');this.busy.set(false);}});}
  reset(){this.quantities.clear();this.cart.set(null);this.order.set(null);this.orderDetails.set(null);this.selectedProduct.set(null);this.selectedCategory.set(null);this.assistantProductIds.set(null);this.search.set('');this.view.set('home');this.draftMessage='';this.showCategories.set(false);this.showProducts.set(false);this.showCart.set(false);this.showOrders.set(false);this.error.set('');this.messages.set(this.setup()?.messages??[]);}
  ngOnDestroy(){this.releaseImages();}
  private loadImages(data:DemoSetup){this.releaseImages();if(!data.products.length){this.setup.set(data);this.busy.set(false);return;}const images=data.products.map(product=>{const urls=product.imageUrls?.length?product.imageUrls:(product.imageUrl?[product.imageUrl]:[]);if(!urls.length)return of(product);return forkJoin(urls.slice(0,5).map(url=>this.api.productImageUrl(url).pipe(catchError(()=>this.api.productImage(product.productId)),map(blob=>{const imageUrl=URL.createObjectURL(blob);this.imageUrls.push(imageUrl);return imageUrl;}),catchError(()=>of(''))))).pipe(map(imageUrls=>{const valid=imageUrls.filter(Boolean);return {...product,imageUrl:valid[0]??undefined,imageUrls:valid};}),catchError(()=>of({...product,imageUrl:undefined,imageUrls:[]})));});forkJoin(images).subscribe(products=>{const categories=data.categories.map(category=>({...category,imageUrl:this.catalogAsset(category.categoryName)??products.find(p=>p.productId===category.imageProductId)?.imageUrl}));this.setup.set({...data,products,categories});this.busy.set(false);});}
  private catalogAsset(categoryName?:string){const key=(categoryName??'').toLowerCase();if(key.includes('grocery')||key.includes('staples'))return 'assets/commerce/grocery.png';if(key.includes('beverage'))return 'assets/commerce/beverages.png';if(key.includes('saree')||key.includes('ethnic'))return 'assets/commerce/sarees.png';if(key.includes('personal')||key.includes('care'))return 'assets/commerce/personal-care.png';if(key.includes('home')||key.includes('kitchen'))return 'assets/commerce/home-kitchen.png';if(key.includes('book')||key.includes('stationery'))return 'assets/commerce/books-stationery.png';return undefined;}
  private releaseImages(){this.imageUrls.forEach(url=>URL.revokeObjectURL(url));this.imageUrls=[];}
  private calculate(){if(!this.warehouseId)return;this.api.cart(this.warehouseId,this.items()).subscribe({next:x=>this.cart.set(x),error:()=>this.error.set('Cart could not be recalculated from current ERP pricing and stock.')});}
  private items(){return [...this.quantities].map(([productId,quantity])=>({productId,quantity}));}
  private say(text:string,kind:string){this.messages.update(x=>[...x,{sender:'WHATS_BIZ',kind,text}]);}
  private respondToMessage(text:string){
    const command=text.toLocaleLowerCase('en-IN').replace(/[,!?]/g,' ').replace(/\s+/g,' ').trim();
    const hindi=/[\u0900-\u097f]/.test(command);
    const hinglish=!hindi&&/\b(dikhao|chahiye|batao|namaste|namaskar|sasta|saste|mehnga|mehange|andar|wala|wali|ka|ke|se kam|se zyada)\b/.test(command);
    if(/^(hi|hii+|hello|hey|namaste|namaskar|नमस्ते|नमस्कार|हाय)\b/.test(command)){
      this.say(hindi?'नमस्ते! मैं कीमत, श्रेणी और उत्पाद के आधार पर सामान खोज सकता हूँ। जैसे: “₹300 से कम की साड़ी दिखाओ।”':hinglish?'Namaste! Main price, category aur product ke hisaab se items dhoondh sakta hoon. Jaise: “300 se kam saree dikhao.”':'Hello! I can find products by price, category, or name. Try “Show me products under 300.”','TEXT');return;
    }
    if(/\b(cart|basket|tokri|कार्ट)\b/.test(command)){this.openCart();return;}
    if(/\b(my orders?|order history|mere orders?|ऑर्डर|आर्डर)\b/.test(command)){this.myOrders();return;}
    const data=this.setup();if(!data){this.say(hindi?'कैटलॉग अभी उपलब्ध नहीं है।':'The catalog is not available yet.','TEXT');return;}
    const amountMatch=command.match(/(?:₹|rs\.?|inr)?\s*(\d+(?:\.\d+)?)/i);
    const amount=amountMatch?Number(amountMatch[1]):null;
    const above=/(above|over|more than|greater than|se zyada|se jyada|से अधिक|से ज्यादा|से ज़्यादा)/.test(command);
    const below=/(under|below|less than|up to|upto|within|andar|se kam|से कम|तक|के अंदर)/.test(command);
    const stopWords=new Set(['show','me','the','a','an','product','products','item','items','under','below','above','over','less','more','than','up','to','within','please','find','want','need','dikhao','dikhana','batao','chahiye','andar','wala','wali','ke','ka','ki','se','kam','zyada','jyada','mujhe','मुझे','दिखाओ','उत्पाद','सामान','कम','अधिक','के','की','से','तक']);
    const words=command.replace(/₹|rs\.?|inr|\d+(?:\.\d+)?/gi,' ').split(/\s+/).filter(x=>x.length>1&&!stopWords.has(x));
    let matches=data.products.filter(x=>x.availableQuantity>0);
    if(amount!==null&&(below||above))matches=matches.filter(x=>above?x.sellingPrice>amount:x.sellingPrice<=amount);
    if(words.length){const scored=matches.map(product=>{const haystack=`${product.productName} ${product.productCode} ${product.categoryName}`.toLocaleLowerCase('en-IN');return {product,score:words.filter(word=>haystack.includes(word)).length};});const best=Math.max(0,...scored.map(x=>x.score));if(best>0)matches=scored.filter(x=>x.score===best).map(x=>x.product);else if(amount===null)matches=[];}
    matches=matches.sort((a,b)=>a.sellingPrice-b.sellingPrice).slice(0,20);
    if(!matches.length){this.say(hindi?'माफ़ कीजिए, आपकी खोज से मिलता हुआ कोई उपलब्ध उत्पाद नहीं मिला। दूसरी कीमत या श्रेणी आज़माएँ।':hinglish?'Sorry, is search ke liye koi available product nahi mila. Dusra price ya category try karein.':'Sorry, I could not find an available product matching that request. Try another price or category.','TEXT');return;}
    this.assistantProductIds.set(new Set(matches.map(x=>x.productId)));this.selectedCategory.set(null);this.selectedProduct.set(null);this.showCategories.set(false);this.showProducts.set(true);this.showCart.set(false);this.showOrders.set(false);
    const priceText=amount!==null?(above?` above ₹${amount}`:` under ₹${amount}`):'';
    this.say(hindi?`मुझे ${matches.length} उपलब्ध उत्पाद मिले${amount!==null?` (₹${amount} ${above?'से अधिक':'तक'})`:''}।`:`I found ${matches.length} available product${matches.length===1?'':'s'}${priceText}.`,'CATALOGUE');
  }
}
