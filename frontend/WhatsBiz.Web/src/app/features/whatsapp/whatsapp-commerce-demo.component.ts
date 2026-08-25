import { ChangeDetectionStrategy, Component, computed, OnDestroy, signal } from '@angular/core';
import { catchError, from, map, mergeMap, of, Subscription, toArray } from 'rxjs';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { PageContainerComponent } from '../../shared/components/page-container/page-container.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { StatusChipComponent } from '../../shared/components/status-chip/status-chip.component';
import { CoinWallet, DemoCart, DemoCategory, DemoMessage, DemoOrder, DemoProduct, DemoReadiness, DemoSetup, OrderDetails, OrderSummary, WhatsAppCommerceDemoApiService } from './whatsapp-commerce-demo-api.service';
import { LocalCommerceIntentEngine } from './local-commerce-intent-engine';

type CatalogView='home'|'categories'|'products'|'detail'|'cart'|'orders';
type FulfillmentOption='WALK_IN'|'RETAILER_DELIVERY'|'COURIER';
type CheckoutPaymentType='ONLINE'|'COD';
@Component({
  imports:[CurrencyPipe,DatePipe,FormsModule,RouterLink,MatButtonModule,MatFormFieldModule,MatSelectModule,PageContainerComponent,PageHeaderComponent,StatusChipComponent],
  templateUrl:'./whatsapp-commerce-demo.component.html', styleUrl:'./whatsapp-commerce-demo.component.scss', changeDetection:ChangeDetectionStrategy.OnPush,
  styles:[`.order-success{align-items:center!important;padding:22px!important;border:1px solid #bfe8d7!important;border-left:4px solid #00a884!important;border-radius:16px!important;background:linear-gradient(135deg,#edfbf4,#f8fffb)!important;box-shadow:0 8px 20px #075e5412!important}.order-success>span{display:grid!important;width:42px!important;height:42px!important;border-radius:50%!important;background:#d7f5e5!important;color:#008069!important;place-items:center!important}.order-success div{gap:3px!important}.order-success strong{font-size:17px!important}.order-success button{display:flex!important;align-items:center!important;gap:7px!important;margin-left:auto!important;padding:9px 13px!important;border:1px solid #9ed8c1!important;border-radius:9px!important;background:#fff!important;color:#007b65!important;font:inherit!important;font-size:12px!important;font-weight:700!important;cursor:pointer!important;transition:.2s!important}.order-success button:hover{background:#eaf7f1!important;border-color:#008069!important}.order-receive{display:grid;gap:14px;margin-top:14px;padding:18px;border:1px solid #d9e8df;border-radius:16px;background:#fff}.order-receive h3{margin:3px 0 0;color:#243b30;font-size:18px}.order-receive p{margin:0;color:#718078;font-size:12px}.receive-options{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}.receive-options button{display:flex;min-height:106px;padding:14px;border:1px solid #d8e6de;border-radius:12px;background:#fff;color:#385347;text-align:left;align-items:flex-start;flex-direction:column;font:inherit;cursor:pointer;transition:.2s}.receive-options button:hover{border-color:#72c5a1;background:#f6fcf8;transform:translateY(-1px)}.receive-options button.active{border-color:#008069;background:#eaf7f1;box-shadow:0 0 0 2px #00806922;color:#075e54}.receive-options .material-symbols-rounded{color:#008069;font-size:25px}.receive-options strong{font-size:13px}.receive-options small{color:#718078;font-size:11px;line-height:1.35}.payment-choice{display:flex;align-items:center;gap:9px;padding-top:2px;flex-wrap:wrap}.payment-choice .eyebrow{flex-basis:100%}.payment-choice button{display:flex;align-items:center;gap:8px;padding:10px 14px;border:1px solid #d8e6de;border-radius:10px;background:#fff;color:#385347;font:inherit;font-size:13px;font-weight:700;cursor:pointer;transition:.2s}.payment-choice button:hover,.payment-choice button.active{border-color:#008069;background:#eaf7f1;color:#007b65}.payment-choice .material-symbols-rounded{font-size:19px}.selection-confirmed{display:flex!important;align-items:center;gap:7px;padding:10px 12px!important;border-radius:9px;background:#eaf7f1;color:#007b65!important;font-weight:700}.selection-confirmed .material-symbols-rounded{font-size:18px}.checkout-details{display:grid;gap:12px;padding:14px;border:1px solid #d9e8df;border-radius:12px;background:#fbfefc}.checkout-details label{display:grid;gap:6px;color:#385347;font-size:12px;font-weight:700}.checkout-details textarea{padding:10px;border:1px solid #cbded3;border-radius:9px;font:inherit;resize:vertical}.checkout-options{display:grid;gap:10px}.checkout-options div{display:flex;align-items:center;gap:7px;flex-wrap:wrap}.checkout-options strong{flex-basis:100%;font-size:12px}.checkout-options button{padding:7px 10px;border:1px solid #d8e6de;border-radius:16px;background:#fff;color:#385347;font:inherit;font-size:11px;cursor:pointer}.checkout-options button.active{border-color:#008069;background:#eaf7f1;color:#007b65}.checkout-summary{display:grid;gap:3px;padding:10px;border-radius:9px;background:#eaf7f1;color:#075e54;font-size:12px}.checkout-summary small{color:#4f6d5e}@media(max-width:700px){.order-success{align-items:flex-start!important;flex-wrap:wrap}.order-success button{width:100%;margin-left:0!important;justify-content:center}.receive-options{grid-template-columns:1fr}.receive-options button{min-height:0}.payment-choice button{flex:1;justify-content:center}}`],
})
export class WhatsAppCommerceDemoComponent implements OnDestroy {
  readonly readiness=signal<DemoReadiness|null>(null); readonly setup=signal<DemoSetup|null>(null); readonly messages=signal<DemoMessage[]>([]); readonly cart=signal<DemoCart|null>(null); readonly order=signal<DemoOrder|null>(null); readonly orders=signal<OrderSummary[]>([]); readonly orderDetails=signal<OrderDetails|null>(null); readonly selectedProduct=signal<DemoProduct|null>(null); readonly started=signal(false); readonly error=signal(''); readonly busy=signal(false); readonly view=signal<CatalogView>('home'); readonly search=signal(''); readonly selectedCategory=signal<DemoCategory|null>(null); readonly showCategories=signal(false); readonly showProducts=signal(false); readonly showCart=signal(false); readonly showOrders=signal(false);
  readonly selectedImageIndex=signal(0); readonly typing=signal(false);
  readonly fulfillmentOption=signal<FulfillmentOption|null>(null); readonly checkoutPaymentType=signal<CheckoutPaymentType|null>(null); readonly wallet=signal<CoinWallet|null>(null);
  readonly assistantProductIds=signal<Set<string>|null>(null);
  readonly visibleProducts=computed(()=>{const data=this.setup();if(!data)return[];const term=this.search().trim().toLowerCase();const category=this.selectedCategory()?.categoryId;const assistantIds=this.assistantProductIds();return data.products.filter(x=>(!assistantIds||assistantIds.has(x.productId))&&(!category||x.categoryId===category)&&(!term||`${x.productName} ${x.productCode} ${x.categoryName}`.toLowerCase().includes(term)));});
  customerId=''; warehouseId=''; draftMessage=''; deliveryAddress=''; deliveryStatus='PENDING'; courierName=''; trackingNumber=''; redeemCoins=0; private quantities=new Map<string,number>(); private imageUrls:string[]=[]; private imageLoad?:Subscription; private readonly intentEngine=new LocalCommerceIntentEngine();
  constructor(private readonly api:WhatsAppCommerceDemoApiService){this.checkReadiness();}
  checkReadiness(){this.busy.set(true);this.api.readiness().subscribe({next:x=>{this.readiness.set(x);this.busy.set(false);if(x.ready)this.load();},error:()=>{this.error.set('Demo readiness could not be checked.');this.busy.set(false);}});}
  load(warehouseId?:string){this.busy.set(true);this.api.setup(warehouseId).subscribe({next:x=>{this.loadImages(x);this.messages.set(x.messages);this.warehouseId=warehouseId??x.warehouses[0]?.warehouseId??'';this.customerId=x.customers[0]?.customerId??'';},error:()=>{this.error.set('MOCK mode must be enabled for this tenant before starting the simulator.');this.busy.set(false);}});}
  changeWarehouse(){this.quantities.clear();this.cart.set(null);this.selectedCategory.set(null);this.load(this.warehouseId);}
  start(){this.reset();this.started.set(true);this.loadWallet();}
  changeCustomer(){this.redeemCoins=0;this.wallet.set(null);this.order.set(null);this.orderDetails.set(null);this.loadWallet();}
  browse(){this.assistantProductIds.set(null);this.selectedCategory.set(null);this.selectedProduct.set(null);this.showCategories.set(true);this.showProducts.set(false);this.showCart.set(false);this.showOrders.set(false);this.say('Choose a category to browse our products.','CATALOGUE');}
  openCart(){this.showCart.set(true);this.showCategories.set(false);this.showProducts.set(false);this.showOrders.set(false);this.selectedProduct.set(null);this.loadWallet();this.say('Here is your cart.','CART');}
  myOrders(){if(!this.customerId)return;this.showOrders.set(true);this.showCategories.set(false);this.showProducts.set(false);this.showCart.set(false);this.loadOrders();}
  sendMessage(){const text=this.draftMessage.trim();if(!text||this.typing())return;this.messages.update(x=>[...x,{sender:'CUSTOMER',kind:'TEXT',text}]);this.draftMessage='';this.typing.set(true);window.setTimeout(()=>{this.respondToMessageV2(text);this.typing.set(false);},280);}
  quickPrompt(text:string){this.draftMessage=text;this.sendMessage();}
  collectionNames(product:DemoProduct){return this.setup()?.collections.filter(x=>x.productIds.includes(product.productId)).map(x=>x.name).join(' · ')||'';}
  go(next:CatalogView){this.view.set(next);if(next!=='products')this.selectedCategory.set(null);if(next!=='detail')this.selectedProduct.set(null);if(next==='orders')this.loadOrders();}
  chooseCategory(category:DemoCategory){this.assistantProductIds.set(null);this.selectedCategory.set(category);this.selectedProduct.set(null);this.search.set('');this.showCategories.set(false);this.showProducts.set(true);this.view.set('products');this.say(`${category.categoryName} products`,'CATALOGUE');}
  showAllProducts(){this.selectedCategory.set(null);this.view.set('products');}
  openProduct(product:DemoProduct){this.selectedProduct.set(product);this.selectedImageIndex.set(0);this.showProducts.set(false);this.view.set('detail');}
  closeProduct(){this.selectedProduct.set(null);this.showProducts.set(true);this.view.set('products');}
  back(){if(this.view()==='detail')this.view.set('products');else if(this.view()==='products')this.view.set(this.selectedCategory()?'categories':'home');else this.view.set('home');}
  add(product:DemoProduct){if(product.availableQuantity<=0)return;this.quantities.set(product.productId,(this.quantities.get(product.productId)??0)+1);this.calculate();this.say(`${product.productName} added to cart.`,'CART');}
  quantity(productId:string,delta:number){const next=(this.quantities.get(productId)??0)+delta;if(next<=0)this.quantities.delete(productId);else this.quantities.set(productId,next);this.calculate();}
  remove(productId:string){this.quantities.delete(productId);this.calculate();}
  cartCount(){return [...this.quantities.values()].reduce((a,b)=>a+b,0);}
  quantityFor(id:string){return this.quantities.get(id)??0;}
  loadOrders(){if(!this.customerId)return;this.api.orders(this.customerId).subscribe({next:x=>this.orders.set(x),error:()=>this.error.set('Orders could not be loaded.')});}
  openOrder(x:OrderSummary){this.deliveryStatus=x.deliveryStatus;this.courierName=x.courierName??'';this.trackingNumber=x.trackingNumber??'';this.api.orderDetails(x.orderId,this.customerId).subscribe({next:d=>this.orderDetails.set(d),error:()=>this.error.set('Order details could not be loaded.')});}
  updateDelivery(){const detail=this.orderDetails();if(!detail)return;this.busy.set(true);this.api.updateDelivery(detail.order.orderId,this.deliveryStatus,this.courierName,this.trackingNumber).subscribe({next:updated=>{this.orders.update(rows=>rows.map(row=>row.orderId===updated.orderId?updated:row));this.orderDetails.set({...detail,order:updated});this.busy.set(false);this.error.set('');},error:()=>{this.error.set('Delivery status could not be updated.');this.busy.set(false);}});}
  refreshStatuses(){if(!this.customerId)return;this.api.notifications(this.customerId).subscribe({next:x=>{if(x.length)this.messages.update(m=>[...m,...x]);this.myOrders();},error:()=>this.error.set('Order statuses could not be refreshed.')});}
  place(){if(!this.customerId||!this.warehouseId||!this.items().length||!this.deliveryAddress.trim()||!this.fulfillmentOption()||!this.checkoutPaymentType()){this.error.set('Enter the address and select fulfilment and payment before confirming the order.');return;}this.busy.set(true);this.error.set('');this.api.order(this.customerId,this.warehouseId,this.items(),this.deliveryAddress.trim(),this.fulfillmentOption()!,this.checkoutPaymentType()!,this.redeemCoins||0).subscribe({next:x=>{this.order.set(x);this.messages.update(m=>[...m,...x.messages]);this.showCart.set(false);this.quantities.clear();this.cart.set(null);this.redeemCoins=0;this.loadWallet();this.busy.set(false);},error:()=>{this.error.set('The order could not be created. Check checkout details, coin limits, customer, warehouse, and stock.');this.busy.set(false);}});}
  loadWallet(){if(!this.customerId)return;this.api.wallet(this.customerId).subscribe({next:x=>this.wallet.set(x),error:()=>this.wallet.set(null)});}
  selectFulfillment(option:FulfillmentOption){this.fulfillmentOption.set(option);}
  selectCheckoutPayment(type:CheckoutPaymentType){this.checkoutPaymentType.set(type);}
  reset(){this.quantities.clear();this.cart.set(null);this.order.set(null);this.orderDetails.set(null);this.selectedProduct.set(null);this.selectedCategory.set(null);this.assistantProductIds.set(null);this.fulfillmentOption.set(null);this.checkoutPaymentType.set(null);this.deliveryAddress='';this.search.set('');this.view.set('home');this.draftMessage='';this.typing.set(false);this.showCategories.set(false);this.showProducts.set(false);this.showCart.set(false);this.showOrders.set(false);this.error.set('');this.messages.set(this.setup()?.messages??[]);}
  printReceipt(){const placed=this.order();if(!placed)return;this.api.printReceipt(placed.orderId).subscribe({next:receipt=>{const url=URL.createObjectURL(receipt);const popup=window.open(url,'_blank');if(popup)popup.addEventListener('load',()=>URL.revokeObjectURL(url),{once:true});},error:()=>this.error.set('The billing receipt could not be opened.')});}
  ngOnDestroy(){this.imageLoad?.unsubscribe();this.releaseImages();}
  private loadImages(data:DemoSetup){this.imageLoad?.unsubscribe();this.releaseImages();if(!data.products.length){this.setup.set(data);this.busy.set(false);return;}this.imageLoad=from(data.products).pipe(mergeMap(product=>{const url=product.imageUrls?.[0]??product.imageUrl;if(!url)return of(product);return this.api.productImageUrl(url).pipe(map(blob=>{const imageUrl=URL.createObjectURL(blob);this.imageUrls.push(imageUrl);return {...product,imageUrl,imageUrls:[imageUrl]};}),catchError(()=>of({...product,imageUrl:undefined,imageUrls:[]})));},6),toArray()).subscribe({next:products=>{const categories=data.categories.map(category=>({...category,imageUrl:this.catalogAsset(category.categoryName)??products.find(p=>p.productId===category.imageProductId)?.imageUrl}));this.setup.set({...data,products,categories});this.busy.set(false);},error:()=>{this.setup.set(data);this.busy.set(false);}});}
  private catalogAsset(categoryName?:string){const key=(categoryName??'').toLowerCase();if(key.includes('grocery')||key.includes('staples'))return 'assets/commerce/grocery.png';if(key.includes('beverage'))return 'assets/commerce/beverages.png';if(key.includes('saree')||key.includes('ethnic'))return 'assets/commerce/sarees.png';if(key.includes('personal')||key.includes('care'))return 'assets/commerce/personal-care.png';if(key.includes('home')||key.includes('kitchen'))return 'assets/commerce/home-kitchen.png';if(key.includes('book')||key.includes('stationery'))return 'assets/commerce/books-stationery.png';return undefined;}
  private releaseImages(){this.imageUrls.forEach(url=>URL.revokeObjectURL(url));this.imageUrls=[];}
  private calculate(){if(!this.warehouseId)return;this.api.cart(this.warehouseId,this.items()).subscribe({next:x=>this.cart.set(this.withLoadedImages(x)),error:()=>this.error.set('Cart could not be recalculated from current ERP pricing and stock.')});}
  private withLoadedImages(cart:DemoCart){const images=new Map((this.setup()?.products??[]).map(product=>[product.productId,product.imageUrl]));return {...cart,items:cart.items.map(item=>({...item,imageUrl:images.get(item.productId)??item.imageUrl}))};}
  private items(){return [...this.quantities].map(([productId,quantity])=>({productId,quantity}));}
  private say(text:string,kind:string){this.messages.update(x=>[...x,{sender:'WHATS_BIZ',kind,text}]);}
  private respondToMessageV2(text:string){
    const command=text.trim();
    const normalized=command.toLocaleLowerCase('en-IN');
    const hindi=/[\u0900-\u097f]/.test(command);
    const language=hindi?'HI':/\b(dikhao|chahiye|batao|mujhe|andar|ke|se|kam|zyada|jyada)\b/i.test(normalized)?'HINGLISH':'EN';
    if(/^(hi|hii+|hello|hey|namaste|namaskar)\b/i.test(normalized)){
      this.say(language==='HI'?'नमस्ते! मैं श्रेणी, विशेषता और कीमत के आधार पर उत्पाद खोज सकता हूँ।':language==='HINGLISH'?'Namaste! Main category, attribute aur price ke hisaab se products dhoondh sakta hoon.':'Hello! I can find products by category, attributes, and price.','TEXT');return;
    }
    if(/\b(cart|basket|tokri|कार्ट)\b/i.test(command)){this.openCart();return;}
    if(/\b(my orders?|order history|mere orders?|मेरा ऑर्डर|ऑर्डर)\b/i.test(command)){this.myOrders();return;}
    const data=this.setup();
    if(!data){this.say(language==='HI'?'कैटलॉग अभी उपलब्ध नहीं है।':'The catalog is not available yet.','TEXT');return;}
    const criteria=this.intentEngine.parse(command,data.categories,data.products,data.collections);
    const result=this.intentEngine.search(criteria,data.products,data.categories,data.collections);
    this.api.analytics({eventType:result.clarificationCategories.length?'PRODUCT_SEARCH_CLARIFICATION':'PRODUCT_SEARCH',customerId:this.customerId,metadata:{language:criteria.language,confidence:criteria.confidence,confidenceScore:criteria.confidenceScore,category:criteria.category,collection:criteria.collection,resultCount:result.products.length,usedExternalAi:false}}).pipe(catchError(()=>of(void 0))).subscribe();
    if(result.clarificationCollections.length){this.say(language==='HI'?`कृपया Collection चुनें: ${result.clarificationCollections.map(x=>x.name).join(' • ')}`:language==='HINGLISH'?`Kaunsi collection chahiye? ${result.clarificationCollections.map(x=>x.name).join(' • ')}`:`Which collection would you like? ${result.clarificationCollections.map(x=>x.name).join(' • ')}`,'TEXT');return;}
    if(result.clarificationCategories.length){
      this.showCategories.set(true);this.showProducts.set(false);this.showCart.set(false);this.showOrders.set(false);
      const names=result.clarificationCategories.map(x=>x.categoryName).join(' • ');
      this.say(language==='HI'?`कृपया बताएं कि ₹${criteria.maxPrice ?? criteria.minPrice ?? ''} के अंदर आप क्या देखना चाहते हैं: ${names}`:language==='HINGLISH'?`₹${criteria.maxPrice ?? criteria.minPrice ?? ''} ke andar aap kya dekhna chahte hain? ${names}`:`What would you like to see under ₹${criteria.maxPrice ?? criteria.minPrice ?? ''}? ${names}`,'TEXT');return;
    }
    if(!result.products.length){
      const price=criteria.maxPrice!==undefined?`₹${criteria.maxPrice} ${criteria.language==='HI'?'के अंदर':'under'}`:criteria.minPrice!==undefined?`₹${criteria.minPrice} ${criteria.language==='HI'?'से अधिक':'above'}`:'';
      const subject=criteria.category??criteria.productName??criteria.searchText;
      this.api.analytics({eventType:'PRODUCT_SEARCH_NO_MATCH',customerId:this.customerId,metadata:{language:criteria.language,confidence:criteria.confidence,category:criteria.category,maxPrice:criteria.maxPrice,resultCount:0}}).pipe(catchError(()=>of(void 0))).subscribe();
      this.say(language==='HI'?`माफ़ कीजिए, ${price} ${subject} अभी स्टॉक में उपलब्ध नहीं है।`:language==='HINGLISH'?`Sorry, ${price} ${subject} abhi stock mein available nahi hai.`:`I couldn't find ${subject} ${price} currently in stock.`,'TEXT');
      if(result.suggestions.length){this.say(language==='HI'?'आप ये विकल्प देख सकते हैं: ':language==='HINGLISH'?'Aap ye relevant alternatives try kar sakte hain: ':'You can try these relevant alternatives: ','TEXT');this.assistantProductIds.set(new Set(result.suggestions.map(x=>x.productId)));this.showProducts.set(true);this.showCategories.set(false);}
      return;
    }
    this.assistantProductIds.set(new Set(result.products.map(x=>x.productId)));this.selectedCategory.set(null);this.selectedProduct.set(null);this.showCategories.set(false);this.showProducts.set(true);this.showCart.set(false);this.showOrders.set(false);
    const price=criteria.maxPrice!==undefined?` under ₹${criteria.maxPrice}`:criteria.minPrice!==undefined?` above ₹${criteria.minPrice}`:'';
    this.say(language==='HI'?`${result.products.length} उपलब्ध उत्पाद मिले${price}।`:language==='HINGLISH'?`${result.products.length} available products mile${price}.`:`I found ${result.products.length} available product${result.products.length===1?'':'s'}${price}.`,'CATALOGUE');
  }
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
    if(words.length){const scored=matches.map(product=>{const haystack=`${product.productName} ${product.productCode} ${product.categoryName}`.toLocaleLowerCase('en-IN');return {product,score:words.filter(word=>haystack.includes(word)).length};});const best=Math.max(0,...scored.map(x=>x.score));if(best>0)matches=scored.filter(x=>x.score===best).map(x=>x.product);else matches=[];}
    matches=matches.sort((a,b)=>a.sellingPrice-b.sellingPrice).slice(0,20);
    if(!matches.length){this.say(hindi?'माफ़ कीजिए, आपकी खोज से मिलता हुआ कोई उपलब्ध उत्पाद नहीं मिला। दूसरी कीमत या श्रेणी आज़माएँ।':hinglish?'Sorry, is search ke liye koi available product nahi mila. Dusra price ya category try karein.':'Sorry, I could not find an available product matching that request. Try another price or category.','TEXT');return;}
    this.assistantProductIds.set(new Set(matches.map(x=>x.productId)));this.selectedCategory.set(null);this.selectedProduct.set(null);this.showCategories.set(false);this.showProducts.set(true);this.showCart.set(false);this.showOrders.set(false);
    const priceText=amount!==null?(above?` above ₹${amount}`:` under ₹${amount}`):'';
    this.say(hindi?`मुझे ${matches.length} उपलब्ध उत्पाद मिले${amount!==null?` (₹${amount} ${above?'से अधिक':'तक'})`:''}।`:`I found ${matches.length} available product${matches.length===1?'':'s'}${priceText}.`,'CATALOGUE');
  }
}
