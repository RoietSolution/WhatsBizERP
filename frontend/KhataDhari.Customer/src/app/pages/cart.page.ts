import { CurrencyPipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, HostListener, OnDestroy, OnInit, effect, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CartService } from '../cart/cart.service';
import { CustomerSessionService } from '../customer-session.service';
import { WishlistService } from '../wishlist.service';
import { StorefrontDataService } from '../data/storefront-data.service';
import { CartLine, CartQuote, CustomerAddress, StorePaymentMethod } from '../models/storefront.models';
import { FreeDeliveryProgressComponent } from '../shared/free-delivery-progress.component';
import { StorefrontActivityService } from '../storefront-activity.service';

interface CartServiceabilityState {
  storeKey: string;
  pincode: string;
  verified: true;
  verifiedAtUtc: number;
  quote: CartQuote;
}

const SERVICEABILITY_TTL_MS = 15 * 60 * 1000;

const INDIAN_STATES = ['Andaman and Nicobar Islands','Andhra Pradesh','Arunachal Pradesh','Assam','Bihar','Chandigarh','Chhattisgarh','Dadra and Nagar Haveli and Daman and Diu','Delhi','Goa','Gujarat','Haryana','Himachal Pradesh','Jammu and Kashmir','Jharkhand','Karnataka','Kerala','Ladakh','Lakshadweep','Madhya Pradesh','Manipur','Meghalaya','Mizoram','Nagaland','Odisha','Puducherry','Punjab','Rajasthan','Sikkim','Tamil Nadu','Telangana','Tripura','Uttar Pradesh','Uttarakhand','West Bengal'];

@Component({
  standalone: true,
  imports: [CurrencyPipe, NgTemplateOutlet, RouterLink, FreeDeliveryProgressComponent],
  template: `
    <section class="cart-page">
    @if (!isCheckoutPage()) { <header class="page-heading"><div><span class="eyebrow">YOUR ORDER</span><h1>Shopping cart</h1></div>@if (cart.itemCount()) { <span>{{ cart.itemCount() }} {{ cart.itemCount() === 1 ? 'item' : 'items' }}</span> }</header> }
    @if (checking()) { <div class="notice checking" role="status"><i></i>Checking current prices and availability...</div> }
    @else if (refreshFailed()) { <div class="notice error" role="alert"><b>!</b><span>We couldn't refresh availability. Please retry before checkout.</span><button type="button" (click)="refresh()">Retry</button></div> }
    @if (quoteLoadError()) { <div class="notice error" role="alert"><b>!</b><span>{{quoteLoadError()}}</span><button type="button" (click)="retryQuote()">Retry</button></div> }
        @if (isCheckoutPage()) {
      <header class="page-heading"><div><span class="eyebrow">CHECKOUT</span><h1>Payment and delivery</h1></div></header>
      @if(orderNumber() && selectedPayment()==='COD'){<p class="checkout-success order-placed" role="status">Order {{orderNumber()}} placed. Cash will be collected on delivery.</p>}
      <a class="continue checkout-back" [routerLink]="['/', cart.storeKey(), 'cart']"><span aria-hidden="true">←</span> Back to cart</a>
      <div class="checkout-layout">        <aside class="checkout-card">
          <section class="payment-total" aria-labelledby="amount-to-pay-title">
            <span id="amount-to-pay-title">Amount to Pay</span>
            @if(pricingQuote(); as payable){<strong>{{payable.finalPayableAmount | currency:'INR':'symbol':'1.2-2'}}</strong>}
            @else{<small role="status">{{quoteLoadError() ? 'Pricing is unavailable. Use Retry above.' : 'Loading your total…'}}</small>}
          </section>
<section class="payment"><div class="section-title"><span>Payment method</span><b>1</b></div>
            @for(method of paymentMethods();track method.code){<label class="payment-option" [class.selected]="selectedPayment()===method.code"><input type="radio" name="paymentMethod" [value]="method.code" [checked]="selectedPayment()===method.code" (change)="selectedPayment.set(method.code)"/><span><strong>{{method.label}}</strong><small>{{method.provider==='RAZORPAY'?'':method.description}}</small>@if(method.provider==='RAZORPAY'){<em>Powered by Razorpay</em>}</span></label>}
            @if(!paymentMethods().length&&!checking()){<p class="checkout-error">No payment method is currently available.</p>}
          </section>
          <form (submit)="submitCheckout($event)">
            <div class="section-title"><span>Delivery details</span><b>2</b></div>@if(addresses().length){<label><span>Saved address</span><select [value]="selectedAddressId" (change)="selectAddress($event)"><option value="">Add a new delivery address</option>@for(address of addresses();track address.addressId){<option [value]="address.addressId">{{addressSummary(address)}}{{address.isDefault ? ' · Default' : ''}}</option>}</select></label>}<label><span>Delivery pincode</span><input name="pincode" [value]="pincode" (input)="pincodeChanged($event)" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" autocomplete="postal-code" placeholder="6-digit pincode" required /></label>@if(deliveryQuote() && !deliveryQuote()!.isDeliveryEnabled){<p class="checkout-error" role="alert">Delivery is currently unavailable for this store.</p>}@else if(pincodeChecked() && deliveryQuote() && !deliveryQuote()!.isPincodeServiceable){<p class="checkout-error" role="alert">Sorry, delivery is not available at this pincode yet.</p>}
            <div class="address-grid"><label><span>Recipient name</span><input name="recipientName" [value]="recipientName" (input)="recipientName=fieldValue($event);customerName=recipientName" autocomplete="name" maxlength="250" placeholder="Name for this order" required /></label>
            <label><span>Mobile number</span><input name="mobile" [value]="mobile" (input)="mobile=fieldValue($event)" autocomplete="tel" inputmode="tel" maxlength="18" placeholder="10-digit mobile number" required /></label>
            <label><span>Email <small>optional</small></span><input name="email" [value]="email" (input)="email=fieldValue($event)" autocomplete="email" type="email" maxlength="256" placeholder="For payment updates" /></label>
            <label class="full-field"><span>Address line 1</span><input name="addressLine1" [value]="addressLine1" (input)="addressLine1=fieldValue($event)" autocomplete="address-line1" maxlength="500" placeholder="House/shop no., building, street" required /></label>
            <label><span>Address line 2 <small>optional</small></span><input name="addressLine2" [value]="addressLine2" (input)="addressLine2=fieldValue($event)" autocomplete="address-line2" maxlength="500" placeholder="Area or locality" /></label>
            <label><span>Landmark <small>optional</small></span><input name="landmark" [value]="landmark" (input)="landmark=fieldValue($event)" maxlength="250" placeholder="Nearby landmark" /></label>
            <label><span>City</span><input name="city" [value]="city" (input)="city=fieldValue($event)" autocomplete="address-level2" maxlength="150" placeholder="City" required /></label>
            <label><span>District <small>optional</small></span><input name="district" [value]="district" (input)="district=fieldValue($event)" maxlength="150" placeholder="District" /></label>
            <label><span>State</span><select name="state" [value]="state" (change)="state=fieldValue($event)" autocomplete="address-level1" required><option value="">Select state / UT</option>@for(item of indianStates;track item){<option [value]="item">{{item}}</option>}</select></label>
            <label><span>PIN code</span><input name="pincode" [value]="pincode" (input)="pincodeChanged($event)" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" autocomplete="postal-code" placeholder="6-digit PIN code" required /></label>
            <label><span>Country</span><input name="country" [value]="country" readonly aria-readonly="true" /></label></div>
            @if (checkoutError()) { <p class="checkout-error" role="alert"><b>!</b>{{ checkoutError() }}</p> } @if(checkoutNotice()){<p class="checkout-success" role="status">{{checkoutNotice()}}</p>}
            @if (orderNumber()) { <p class="checkout-success" role="status">Order {{ orderNumber() }} created. @if(selectedPayment()!=='COD'){Opening secure payment...}</p> }
            <button class="pay-button" type="submit" [disabled]="!canCheckout()"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 11V8a5 5 0 0 1 10 0v3m-11 0h12v9H6z"/></svg>{{ submitting() ? 'Creating your order...' : actionLabel() }}</button>@if(placeOrderReason();as reason){<p class="checkout-hint" role="status">{{reason}}</p>}
          </form>
          <p class="secure-note">@if(selectedPayment()!=='COD'){Online payment is processed securely by Razorpay. }Stock and prices are verified before your order is created.</p>
        </aside></div>} @else if (cart.lines().length) {
      <div class="cart-layout">
        <section class="lines" aria-label="Cart items">
          @for (line of cart.lines(); track line.product.id) {
            <article class="line" [class.unavailable]="!line.product.available">
              <a class="line-image" [routerLink]="['/', cart.storeKey(), 'products', line.product.id]"><img [src]="line.product.imageUrl" [alt]="line.product.name" (error)="imageFailed($event)" /></a>
              <div class="line-info"><a [routerLink]="['/', cart.storeKey(), 'products', line.product.id]">{{ line.product.name }}</a><small>@if(line.product.packSize){<span>{{line.product.packSize}} · </span>}{{ line.product.sellingPrice | currency:'INR':'symbol':'1.0-2' }} each @if(line.product.gstPricingType === 'EXCLUSIVE' && (line.product.gstPercentage ?? 0) > 0){<span>+ GST</span>}</small><div class="line-prices">@if(line.product.compareAtPrice && line.product.compareAtPrice > line.product.sellingPrice){<del>{{line.product.compareAtPrice | currency:'INR':'symbol':'1.0-2'}}</del><span>Save {{(line.product.compareAtPrice - line.product.sellingPrice) | currency:'INR':'symbol':'1.0-2'}}</span>}</div>@if (!line.product.available) { <em>Out of stock — remove to continue</em> }</div>
              <strong class="line-total">{{ line.product.sellingPrice * line.quantity | currency:'INR':'symbol':'1.0-2' }}</strong>
              <div class="line-actions"><div class="quantity"><button type="button" (click)="cart.adjust(line.product.id,-1)" [attr.aria-label]="'Decrease ' + line.product.name">−</button><b>{{ line.quantity }}</b><button type="button" (click)="cart.adjust(line.product.id,1)" [disabled]="!line.product.available" [attr.aria-label]="'Increase ' + line.product.name">+</button></div><button class="remove" type="button" (click)="cart.remove(line.product.id)">Remove</button></div>
            </article>
          }

        </section>
        <aside class="checkout-card"><section class="delivery-check" aria-labelledby="delivery-check-title"><div class="section-title" id="delivery-check-title"><svg class="section-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 21s7-6.2 7-12A7 7 0 1 0 5 9c0 5.8 7 12 7 12Z"/><circle cx="12" cy="9" r="2.2"/></svg><span>Delivery Availability</span></div><label for="cart-pincode">Pincode</label><div class="pincode-row"><input id="cart-pincode" name="cartPincode" [value]="pincode" (input)="pincodeChanged($event)" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" autocomplete="postal-code" placeholder="Enter 6-digit pincode" /><button type="button" (click)="checkPincode()" [disabled]="checkingPincode() || pincode.length !== 6 || retryBlocked()">{{ checkingPincode() ? "Checking..." : retryBlocked() ? "Retry shortly" : "Check" }}</button></div>@if (pincodeError()) { <p class="checkout-error" role="alert">{{ pincodeError() }}</p> }@if (pincodeChecked()) { @if (deliveryQuote(); as checkedQuote) { @if (checkedQuote.isDeliveryEnabled && checkedQuote.isPincodeServiceable) { <p class="checkout-success" role="status">Delivery available to {{ pincode }}</p> } @else if (!checkedQuote.isDeliveryEnabled) { <p class="checkout-error" role="alert">Delivery is currently unavailable for this store.</p> } @else { <p class="checkout-error" role="alert">Delivery is currently unavailable for this pincode.</p> } } }@else if (!pincodeError()) { <small>Enter a valid six-digit pincode. We’ll check delivery automatically.</small> }</section><section class="promo-section cart-promo" aria-labelledby="cart-promo-title"><div class="section-title" id="cart-promo-title"><span>Offer Code</span></div><div class="promo-row"><input id="cart-promo-code" name="cartPromoCode" [value]="promoCode" (input)="promoCodeChanged($event)" (keydown.enter)="applyPromoCode()" autocomplete="off" maxlength="50" placeholder="Enter offer code" aria-describedby="cart-promo-help" [attr.aria-invalid]="promoError() ? 'true' : null" /><button type="button" (click)="applyPromoCode()" [disabled]="promoBusy() || !promoCode.trim()">{{promoBusy()?'Applying...':'Apply Offer'}}</button></div><small id="cart-promo-help">Enter an offer code to check store discounts.</small>@if(promoError()){<p class="checkout-error" role="alert">{{promoError()}}</p>}@if(offerCelebration()){<div class="offer-celebration" role="status" aria-live="polite"><span class="offer-celebration-mark" aria-hidden="true">✓</span><span class="offer-celebration-copy"><strong>Offer applied!</strong><small>{{offerCelebration()}} is now active.</small></span><button type="button" aria-label="Dismiss offer applied message" (click)="dismissOfferCelebration()">×</button></div>}@if(appliedPromoCode();as applied){<div class="promo-applied checkout-success" role="status"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg><span><strong>Offer {{promoError() ? applied + ' remains applied' : applied + ' applied'}}</strong><small>You saved {{pricingQuote()?.promotionDiscount|currency:'INR':'symbol':'1.0-2'}}</small></span><button type="button" (click)="removePromoCode()" [disabled]="promoBusy()">Remove</button></div>}</section><ng-container *ngTemplateOutlet="cartSummary"></ng-container></aside></div>
      @if(deliveryQuote()?.mayArriveSeparately){<p class="checkout-success" role="status">Items in this order may arrive in separate deliveries.</p>}
      <div class="cart-actions"><button class="proceed-button" type="button" [disabled]="!canProceedToCheckout()" (click)="focusCheckout()"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h13m-5-5 5 5-5 5"/></svg>Proceed to Payment</button><a class="continue" [routerLink]="['/', cart.storeKey()]"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 18-6-6 6 6M9 12h11"/></svg> Add More Items</a></div>
    } @else if (!checking()) {
      <section class="empty"><span><svg viewBox="0 0 24 24"><path d="M3 4h2l2.1 10.2a2 2 0 0 1 2 1.6h7.8a2 2 0 0 0 2-1.6L20 8H6M10 20h.01M17 20h.01"/></svg></span><h2>Your cart is empty</h2><p>Add products from the store and they'll appear here.</p><a [routerLink]="['/', cart.storeKey()]">Browse products</a></section>
    }
        <ng-template #cartSummary>

          @if(pricingQuote();as progress){<shop-free-delivery-progress [quote]="progress" mode="cart" />}
          <section class="summary"><h2><svg class="section-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M6 3h12v18l-3-2-3 2-3-2-3 2V3Z"/><path d="M9 8h6M9 12h6M9 16h3"/></svg>Bill Summary</h2>@if (pricingQuote(); as priced) { @if (priced.productSavings !== undefined && priced.productSavings > 0) { <p class="savings-banner" role="status">You save <strong>{{ priced.productSavings | currency:'INR':'symbol':'1.0-2' }}</strong> on products.</p> } @for(warning of priced.fulfillmentWarnings ?? []; track warning.code) { <p class="checkout-error" role="status">{{warning.message}}</p> } }<div><span>Item Total</span><b>{{ amountLabel(displaySubtotal()) }}</b></div><div><span>Product Savings</span><b>{{ amountLabel(displayProductSavings()) }}</b></div><div><span>Tax / GST</span><b>{{ amountLabel(displayTax()) }}</b></div><div><span>Offer Discount</span><b>{{ discountLabel(displayPromotionDiscount()) }}</b></div><div><span>Coin Discount</span><b>{{ coinDiscountLabel() }}</b></div><div><span>Delivery Fee</span><b>{{ amountLabel(displayDelivery()) }}</b></div><hr/><div class="total"><span>{{ authoritativeQuote() ? 'Total' : 'Estimated Total' }}</span><b>{{ amountLabel(displayTotal()) }}</b></div>@if(summaryNote();as note){<small>{{note}}</small>}</section>
                  </ng-template>
    </section>
  `,
  styles: [`
    .payment-total{display:grid;gap:7px;min-width:0;margin:0 0 20px;padding:18px;border:1px solid var(--store-border);border-radius:14px;background:var(--store-primary-soft);color:var(--store-primary-dark)}.payment-total>span{font:750 12px/1.4 Manrope,sans-serif}.payment-total>strong{font:800 clamp(24px,5vw,32px)/1.2 Manrope,sans-serif;font-variant-numeric:tabular-nums;overflow-wrap:anywhere}.payment-total>small{color:var(--store-muted);font-size:11px;line-height:1.5}@media(max-width:340px){.payment-total{padding:14px}}
    .checkout-hint{display:flex;grid-column:1/-1;align-items:center;gap:7px;margin:0;border-radius:8px;padding:8px;color:var(--store-muted);background:var(--store-surface-muted);font-size:9px;line-height:1.35}
    .address-grid{display:grid;grid-template-columns:1fr 1fr;gap:9px}.address-grid .full-field{grid-column:1/-1}.checkout-card select{width:100%;min-height:44px;box-sizing:border-box;border:1px solid var(--store-border);border-radius:9px;padding:9px 10px;color:var(--store-text);background:#fbfcfa;outline:0;font-size:11px}.checkout-card select:focus{border-color:var(--store-primary);box-shadow:0 0 0 3px var(--store-primary-soft)}@media(max-width:560px){.address-grid{grid-template-columns:1fr}.address-grid .full-field{grid-column:auto}}
    .payment-option>span{min-width:0;line-height:1.35}.payment-option strong,.payment-option small,.payment-option em{overflow-wrap:anywhere}.payment-option em{white-space:nowrap}.checkout-card label>span{min-height:17px;align-items:center;line-height:1.35}.checkout-card input,.checkout-card textarea,.checkout-card select{min-height:44px;box-sizing:border-box}.checkout-card textarea{min-height:84px}.pay-button{transition:background .16s,box-shadow .16s}.pay-button:not(:disabled):hover{box-shadow:0 8px 18px rgba(12,70,39,.18)}
    :host{display:block}.address-grid{display:grid;grid-template-columns:1fr 1fr;gap:9px}.address-grid .full-field{grid-column:1/-1}.address-grid select{min-height:44px}.address-grid input[readonly]{color:var(--store-muted);background:var(--store-surface-muted);cursor:default}.page-heading{display:flex;align-items:end;justify-content:space-between;margin-bottom:17px}.eyebrow{color:var(--store-primary);font-size:9px;font-weight:800;letter-spacing:1.5px}.page-heading h1{margin:3px 0 0;font:800 24px Manrope,sans-serif;letter-spacing:-.7px}.page-heading>span{border-radius:99px;padding:6px 9px;color:var(--store-muted);background:var(--store-surface-muted);font-size:10px;font-weight:700}.notice{display:flex;align-items:center;gap:9px;margin-bottom:12px;border-radius:11px;padding:10px 12px;font-size:11px}.notice.checking{color:var(--store-primary-dark);background:var(--store-primary-soft)}.notice.checking i{width:13px;height:13px;border:2px solid #b9d8c3;border-top-color:var(--store-primary);border-radius:50%;animation:spin .8s linear infinite}.notice.error{color:var(--store-danger);background:#fff0ed}.notice.error b{display:grid;width:20px;height:20px;place-items:center;border-radius:50%;color:#fff;background:var(--store-danger)}.notice.error button{margin-left:auto;border:0;color:var(--store-danger);background:transparent;font-weight:800;cursor:pointer}@keyframes spin{to{transform:rotate(360deg)}}
    .cart-layout{display:grid;grid-template-columns:minmax(0,1fr) 390px;align-items:start;gap:18px}.lines,.checkout-card{border:1px solid var(--store-border);border-radius:18px;background:var(--store-surface)}.lines{overflow:hidden}.line{display:grid;grid-template-columns:82px minmax(0,1fr) auto;grid-template-rows:auto auto;align-items:center;gap:5px 13px;padding:14px;border-bottom:1px solid var(--store-border)}.line-image{grid-row:1/3;width:82px;height:82px;overflow:hidden;border-radius:12px;background:var(--store-surface-muted)}.line-image img{width:100%;height:100%;object-fit:contain;padding:4px}.line-info{display:grid;align-self:end}.line-info>a{overflow:hidden;color:var(--store-text);font-size:12px;font-weight:750;text-decoration:none;text-overflow:ellipsis;white-space:nowrap}.line-info small{margin-top:4px;color:var(--store-muted);font-size:9px}.line-prices{display:flex;align-items:center;gap:7px;margin-top:4px;font-size:9px}.line-prices del{color:var(--store-muted)}.line-prices span{color:var(--store-primary-dark);font-weight:800}.line-info em{margin-top:4px;color:var(--store-danger);font-size:9px;font-style:normal;font-weight:700}.line-total{align-self:end;font:800 13px Manrope,sans-serif}.line-actions{display:flex;align-items:center;align-self:start;gap:10px}.quantity{display:grid;grid-template-columns:27px 25px 27px;height:29px;overflow:hidden;border-radius:8px;color:#fff;background:var(--store-primary)}.quantity button{display:grid;place-items:center;width:100%;height:100%;padding:0;border:0;color:inherit;background:transparent;font-size:17px;line-height:1;cursor:pointer}.quantity button:disabled{opacity:.45}.quantity b{display:grid;place-items:center;width:100%;height:100%;font-size:10px;line-height:1}.remove{border:0;padding:4px;color:var(--store-danger);background:transparent;font-size:9px;font-weight:700;cursor:pointer}.unavailable{background:#fffafa}.cart-actions{display:grid;justify-items:center;gap:8px;margin:18px auto 0;max-width:390px}.proceed-button{display:flex;width:100%;min-height:48px;align-items:center;justify-content:center;gap:8px;border:0;border-radius:12px;padding:0 18px;color:#fff;background:var(--store-primary);font-size:12px;font-weight:800;box-shadow:0 7px 16px rgba(12,70,39,.16);cursor:pointer}.proceed-button:hover{background:var(--store-primary-dark)}.proceed-button:disabled{background:#abb3ad;box-shadow:none;cursor:not-allowed}.proceed-button svg,.continue svg{width:17px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.continue{display:inline-flex;align-items:center;gap:6px;color:var(--store-primary-dark);font-size:10px;font-weight:800;text-decoration:none}.delivery-check{display:grid;gap:7px;padding:16px 18px;border-bottom:1px solid var(--store-border)}.delivery-check label{color:var(--store-muted);font-size:9px;font-weight:700}.delivery-check small{color:var(--store-muted);font-size:9px}.pincode-row{display:flex;gap:7px}.pincode-row input{flex:1;min-width:0!important}.pincode-row button{min-width:70px;border:0;border-radius:9px;color:#fff;background:var(--store-primary);font-size:10px;font-weight:800;cursor:pointer}.pincode-row button:disabled{background:#abb3ad;cursor:not-allowed}
    .checkout-card{position:sticky;top:94px;overflow:hidden}.summary,.payment,.checkout-card form{padding:17px 18px}.summary,.payment,.promo-section{border-bottom:1px solid var(--store-border)}.promo-section{display:grid;gap:8px;padding:17px 18px}.promo-section .section-title{margin-bottom:1px}.promo-row{display:flex;gap:7px}.promo-row input{flex:1;min-width:0!important}.promo-row button,.promo-applied button{border:0;border-radius:9px;padding:0 12px;color:#fff;background:var(--store-primary);font-size:10px;font-weight:800;cursor:pointer}.promo-row button:disabled,.promo-applied button:disabled{background:#abb3ad;cursor:not-allowed}.promo-section>small{color:var(--store-muted);font-size:9px}.promo-applied{display:flex!important;align-items:center;gap:8px}.promo-applied svg{width:17px;flex:0 0 auto;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:2}.promo-applied>span{display:grid;flex:1;gap:2px}.promo-applied small{color:var(--store-muted);font-size:9px}.promo-applied button{min-height:28px;color:var(--store-danger);background:transparent;padding:0 4px}.summary h2{display:flex;align-items:center;gap:7px;margin:0 0 14px;font:800 15px Manrope,sans-serif}.section-title .section-icon,.summary h2 .section-icon{width:18px;height:18px;flex:0 0 auto;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.7}.savings-banner{display:flex!important;align-items:center;justify-content:center;gap:5px;margin:0 0 12px!important;border-radius:9px;padding:9px 10px;color:var(--store-primary-dark);background:var(--store-primary-soft);font-size:10px!important;font-weight:700}.savings-banner strong{font-size:13px!important}.summary>div{display:flex;justify-content:space-between;margin-top:9px;color:var(--store-muted);font-size:10px}.summary>div b{color:var(--store-text);font-size:11px}.summary>div small{font-size:9px}.summary hr{border:0;border-top:1px dashed var(--store-border);margin:13px 0}.summary .total{align-items:center;color:var(--store-text);font-size:12px;font-weight:800}.summary .total b{font:800 16px Manrope,sans-serif}.section-title{display:flex;align-items:center;gap:7px;margin-bottom:11px;color:var(--store-text);font-size:11px;font-weight:800}.section-title b{display:grid;order:-1;width:20px;height:20px;place-items:center;border-radius:7px;color:var(--store-primary-dark);background:var(--store-primary-soft);font-size:9px}.razorpay{display:flex;align-items:center;gap:9px;border:1px solid var(--store-primary);border-radius:11px;padding:10px;background:var(--store-primary-soft)}.payment-mark{display:grid;width:34px;height:34px;place-items:center;border-radius:9px;color:#fff;background:#2d2a77;font:800 17px Manrope,sans-serif}.razorpay>span:nth-child(2){display:grid;flex:1}.razorpay strong{font-size:11px}.razorpay small{margin-top:2px;color:var(--store-muted);font-size:8px}.razorpay i{display:grid;width:18px;height:18px;place-items:center;border-radius:50%;color:#fff;background:var(--store-primary);font-size:10px;font-style:normal}.checkout-card form{display:grid;gap:9px}.checkout-card label{display:grid;gap:4px;color:var(--store-muted);font-size:9px;font-weight:700}.checkout-card label>span{display:flex;justify-content:space-between}.checkout-card label small{font-weight:500}.checkout-card input,.checkout-card textarea{width:100%;border:1px solid var(--store-border);border-radius:9px;padding:9px 10px;color:var(--store-text);background:#fbfcfa;outline:0;font-size:11px}.checkout-card input:focus,.checkout-card textarea:focus{border-color:var(--store-primary);box-shadow:0 0 0 3px var(--store-primary-soft)}.checkout-card textarea{resize:vertical}.checkout-error,.checkout-success{display:flex;align-items:center;gap:7px;margin:0;border-radius:8px;padding:8px;font-size:9px}.checkout-error{color:var(--store-danger);background:#fff0ed}.checkout-error b{display:grid;width:16px;height:16px;place-items:center;border-radius:50%;color:#fff;background:var(--store-danger)}.checkout-success{color:var(--store-primary-dark);background:var(--store-primary-soft)}.pay-button{display:flex;min-height:46px;align-items:center;justify-content:center;gap:8px;margin-top:4px;border:0;border-radius:11px;color:#fff;background:var(--store-primary);font-size:11px;font-weight:800;cursor:pointer}.pay-button svg{width:17px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.8}.pay-button:disabled{background:#abb3ad;cursor:not-allowed}.secure-note{margin:0;padding:0 18px 17px;color:var(--store-muted);font-size:8px;line-height:1.5;text-align:center}.empty{display:grid;min-height:430px;align-content:center;justify-items:center;border:1px solid var(--store-border);border-radius:20px;padding:30px;background:#fff;text-align:center}.empty>span{display:grid;width:62px;height:62px;place-items:center;border-radius:20px;color:var(--store-primary-dark);background:var(--store-primary-soft)}.empty svg{width:30px;fill:none;stroke:currentColor;stroke-linecap:round;stroke-linejoin:round;stroke-width:1.6}.empty h2{margin:15px 0 4px;font:800 19px Manrope,sans-serif}.empty p{margin:0;color:var(--store-muted);font-size:11px}.empty a{margin-top:16px;border-radius:11px;padding:11px 16px;color:#fff;background:var(--store-primary);font-size:11px;font-weight:800;text-decoration:none}
    .delivery-progress{display:grid;gap:7px;padding:13px 18px;border-bottom:1px solid var(--store-border);background:var(--store-primary-soft);min-width:0}.delivery-progress strong{font-size:12px;color:var(--store-primary-dark)}.delivery-progress>span,.delivery-progress small{font-size:10px;color:var(--store-muted)}.progress-track{height:7px;overflow:hidden;border-radius:99px;background:#d9e5dc}.progress-track span{display:block;height:100%;border-radius:99px;background:var(--store-primary);transition:width .2s}.delivery-progress small{overflow-wrap:anywhere}
    /* Keep each quantity cell deterministic despite inherited button typography. */
    .cart-page .quantity{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));width:87px;height:34px;align-items:stretch}
    .cart-page .quantity>button,.cart-page .quantity>b{display:grid;place-items:center;align-items:center;justify-items:center;min-width:0;min-height:0;width:auto;height:34px;box-sizing:border-box;margin:0;padding:0;border:0;line-height:1;transform:none}
    .cart-page .quantity>b{font-size:10px;font-weight:800}
    .cart-page .quantity>button{font-size:17px}
    @media(max-width:850px){.cart-layout{grid-template-columns:1fr}.checkout-card{position:static;order:-1}.checkout-card form{grid-template-columns:1fr 1fr}.checkout-card .section-title,.checkout-error,.checkout-success,.pay-button{grid-column:1/-1}.secure-note{grid-column:1/-1}.line-info>a{white-space:normal}}
    @media(max-width:560px){.cart-actions{max-width:none}.pincode-row button{min-height:42px}.page-heading h1{font-size:21px}.cart-layout{gap:12px}.lines,.checkout-card{border-radius:15px}.line{grid-template-columns:66px minmax(0,1fr) auto;gap:5px 9px;padding:11px}.line-image{width:66px;height:66px}.line-total{font-size:11px}.checkout-card form{grid-template-columns:1fr}.checkout-card form>*{grid-column:1/-1}.pay-button{position:sticky;bottom:8px;z-index:2;box-shadow:0 8px 22px rgba(12,70,39,.2)}}
    @media(max-width:360px){.line{grid-template-columns:58px minmax(0,1fr)}.line-image{width:58px;height:58px}.line-total{grid-column:2;grid-row:2;justify-self:end}.line-actions{grid-column:2}.page-heading>span{display:none}}
  `, `.payment-option{display:flex!important;align-items:flex-start!important;justify-content:flex-start!important;gap:10px!important;margin-top:8px;border:1px solid var(--store-border);border-radius:11px;padding:11px!important;background:#fff;cursor:pointer}.payment-option.selected{border-color:var(--store-primary);background:var(--store-primary-soft)}.payment-option input{width:auto!important;margin-top:3px}.payment-option>span{display:grid}.payment-option strong{color:var(--store-text);font-size:11px}.payment-option small,.payment-option em{margin-top:2px;color:var(--store-muted);font-size:9px;font-style:normal}.payment-option em{font-size:8px}`, `.cart-layout .delivery-check{display:none}.checkout-card form>label:has(>input[name="pincode"]){display:none}`,
     `.offer-celebration{display:flex;align-items:center;gap:10px;min-width:0;margin-top:3px;border:1px solid #b8dfc3;border-radius:10px;padding:10px 11px;color:var(--store-primary-dark);background:linear-gradient(110deg,#eaf7ee,#f8fcf8);box-shadow:0 5px 16px rgba(25,90,51,.10);animation:offer-pop .22s ease-out}.offer-celebration-mark{display:grid;flex:0 0 28px;width:28px;height:28px;place-items:center;border-radius:50%;color:#fff;background:var(--store-primary);font-size:15px;font-weight:800}.offer-celebration-copy{display:grid;flex:1;min-width:0;gap:2px}.offer-celebration-copy strong{color:var(--store-primary-dark);font-size:10px;font-weight:800}.offer-celebration-copy small{color:var(--store-muted);font-size:9px;line-height:1.35;overflow-wrap:anywhere}.offer-celebration button{display:grid;flex:0 0 30px;width:30px;height:30px;place-items:center;border:0;border-radius:8px;color:var(--store-muted);background:transparent;font-size:19px;cursor:pointer}.offer-celebration button:hover{color:var(--store-primary-dark);background:var(--store-primary-soft)}.offer-celebration button:focus-visible{outline:2px solid var(--store-primary);outline-offset:1px}@keyframes offer-pop{from{opacity:0;transform:translateY(4px) scale(.98)}to{opacity:1;transform:translateY(0) scale(1)}}@media(prefers-reduced-motion:reduce){.offer-celebration{animation:none}}`],
})
export class CartPage implements OnInit, OnDestroy {
  readonly isCheckoutPage=signal(false);readonly checking=signal(true);readonly refreshFailed=signal(false);readonly submitting=signal(false);readonly checkoutError=signal('');readonly checkoutNotice=signal('');readonly orderNumber=signal('');
  readonly quote=signal<CartQuote|null>(null);readonly checkingPincode=signal(false);readonly pincodeChecked=signal(false);readonly pincodeError=signal('');readonly quoteLoadError=signal('');readonly retryBlockedUntil=signal(0);readonly promoBusy=signal(false);readonly promoError=signal('');readonly appliedPromoCode=signal('');readonly offerCelebration=signal('');private offerCelebrationTimer?:ReturnType<typeof setTimeout>;private promoRequest=0;private quoteRequest=0;private readonly celebratedPromoIdentities=new Set<string>();
  readonly paymentMethods=signal<StorePaymentMethod[]>([]);readonly selectedPayment=signal('');
  readonly indianStates=INDIAN_STATES; customerName='';recipientName='';mobile='';email='';pincode='';deliveryAddress='';addressLine1='';addressLine2='';landmark='';city='';district='';state='';country='India';selectedAddressId='';readonly addresses=signal<CustomerAddress[]>([]);private checkoutKey='';private checkoutFingerprint='';private validatedPincode='';private quoteFingerprint='';private lastPricingError:unknown=null;private pincodeDebounce?:ReturnType<typeof setTimeout>;private quoteInFlight:{signature:string;request:number;promise:Promise<CartQuote|null>}|null=null;private readonly initialized=signal(false);
  readonly deliveryQuote=signal<CartQuote|null>(null);
  readonly pricingLoading=signal(false);
  private deliveryRequest=0;
  private deliveryFingerprint='';
  private deliveryInFlight:{signature:string;request:number;promise:Promise<CartQuote|null>}|null=null;
  constructor(readonly cart:CartService,private readonly route:ActivatedRoute,private readonly router:Router,private readonly data:StorefrontDataService,private readonly session:CustomerSessionService,private readonly wishlist:WishlistService,private readonly activity:StorefrontActivityService){
    effect(()=>{
      if(!this.initialized())return;
      const key=this.cart.storeKey(),lines=this.cart.lines(),promo=this.appliedPromoCode();
      if(!key||!lines.length){this.quote.set(null);this.quoteFingerprint='';this.invalidateDelivery();return;}
      if(this.isCheckoutPage()&&this.deliveryFingerprint!==this.deliverySignature(key,lines,this.pincode,promo)){
        this.pincodeChecked.set(false);
        if(this.pincode.length===6)void this.loadDeliveryQuote(key,lines,this.pincode,promo);
      }
      if(this.quoteFingerprint!==this.pricingSignature(key,lines,promo))void this.loadQuote(key,lines,'',promo);
    });
  }
  ngOnDestroy():void{++this.quoteRequest;++this.deliveryRequest;if(this.pincodeDebounce)clearTimeout(this.pincodeDebounce);if(this.offerCelebrationTimer)clearTimeout(this.offerCelebrationTimer);}
  @HostListener('document:visibilitychange') onResume():void{if(document.visibilityState==='visible'&&this.initialized())void this.refreshPricing();}
  private async refreshPricing():Promise<void>{const key=this.cart.storeKey();if(!key||!this.cart.lines().length)return;await this.loadQuote(key,this.cart.lines());if(this.isCheckoutPage()&&/^[0-9]{6}$/.test(this.pincode))await this.loadDeliveryQuote(key,this.cart.lines(),this.pincode);}
  private pricingSignature(key:string,lines:readonly CartLine[],promo=this.appliedPromoCode()):string{return JSON.stringify([key,this.linesFingerprint(lines),promo||'']);}
  private deliverySignature(key:string,lines:readonly CartLine[],pin:string,promo=this.appliedPromoCode()):string{return JSON.stringify([key,this.linesFingerprint(lines),pin,promo||'']);}
  pricingQuote():CartQuote|null{return this.quoteFingerprint===this.pricingSignature(this.cart.storeKey(),this.cart.lines())?this.quote():null;}
  private invalidateDelivery():void{++this.deliveryRequest;this.deliveryQuote.set(null);this.deliveryFingerprint='';this.validatedPincode='';this.pincodeChecked.set(false);this.checkingPincode.set(false);}
  private savePromo():void{try{sessionStorage.setItem(this.storageKey(this.cart.storeKey())+':promo',this.appliedPromoCode());}catch{}}
  ngOnInit():void{this.isCheckoutPage.set(this.route.snapshot.url.some(segment=>segment.path==='checkout'));void this.initialize();}
  private async initialize():Promise<void>{
    const key=this.cart.storeKey();await this.refresh();
    if(this.pincodeDebounce)clearTimeout(this.pincodeDebounce);
    try{const promo=sessionStorage.getItem(this.storageKey(key)+':promo')??'';this.appliedPromoCode.set(promo);this.promoCode=promo;}catch{}
    if(this.isCheckoutPage())this.pincode=this.pincode||this.readServiceabilityState(key)?.pincode||this.readSavedPincode(key);
    this.invalidateDelivery();this.initialized.set(true);
    void this.loadQuote(key,this.cart.lines());
    if(this.isCheckoutPage()&&this.pincode.length===6)this.checkPincode();
  }
  focusCheckout(): void { const key=this.cart.storeKey(); if(key) void this.router.navigate(['/',key,'checkout']); }
  private async loadQuote(key:string,lines:readonly CartLine[],_pincode='',promoCode=this.appliedPromoCode()):Promise<CartQuote|null>{
    const items=lines.map(line=>({...line}));
    const signature=this.pricingSignature(key,items,promoCode);
    if(this.quoteInFlight?.signature===signature&&this.quoteInFlight.request===this.quoteRequest)return this.quoteInFlight.promise;
    const request=++this.quoteRequest;
    if(this.quoteFingerprint!==signature)this.quote.set(null);
    this.quoteLoadError.set('');this.lastPricingError=null;this.pricingLoading.set(true);
    const promise=(async()=>{
      try{
        const result=await this.data.previewQuote(key,items,promoCode||undefined);
        if(request!==this.quoteRequest||key!==this.cart.storeKey()||this.linesFingerprint(items)!==this.linesFingerprint(this.cart.lines()))return null;
        if(!result||!Number.isFinite(result.finalPayableAmount)||result.finalPayableAmount<0)throw new Error('Invalid pricing response');
        this.quote.set(result);this.quoteFingerprint=signature;return result;
      }catch(error){
        if(request===this.quoteRequest&&key===this.cart.storeKey()){
          this.quote.set(null);this.quoteFingerprint='';
          this.lastPricingError=error;
          if(promoCode&&this.appliedPromoCode().toLowerCase()===promoCode.toLowerCase()&&this.isPromoValidationFailure(error)){
            this.appliedPromoCode.set('');this.promoCode='';this.savePromo();this.dismissOfferCelebration();this.promoError.set(this.promoMessage(error));
            try{
              const base=await this.data.previewQuote(key,items);
              if(request===this.quoteRequest&&key===this.cart.storeKey()&&this.linesFingerprint(items)===this.linesFingerprint(this.cart.lines())&&base&&Number.isFinite(base.finalPayableAmount)&&base.finalPayableAmount>=0){
                this.quote.set(base);this.quoteFingerprint=this.pricingSignature(key,items,'');this.quoteLoadError.set('');this.lastPricingError=null;return base;
              }
            }catch{/* Keep pricing unavailable if the undiscounted quote also fails. */}
          }
          this.quoteLoadError.set(this.quoteError(error));
        }
        return null;
      }finally{if(request===this.quoteRequest)this.pricingLoading.set(false);}
    })();
    this.quoteInFlight={signature,request,promise};
    try{return await promise;}finally{if(this.quoteInFlight?.promise===promise)this.quoteInFlight=null;}
  }
  private async loadDeliveryQuote(key:string,lines:readonly CartLine[],pin:string,promo=this.appliedPromoCode()):Promise<CartQuote|null>{
    const items=lines.map(line=>({...line})),signature=this.deliverySignature(key,items,pin,promo);
    if(this.deliveryInFlight?.signature===signature&&this.deliveryInFlight.request===this.deliveryRequest)return this.deliveryInFlight.promise;
    const request=++this.deliveryRequest;
    this.deliveryQuote.set(null);this.deliveryFingerprint='';this.pincodeChecked.set(false);this.checkingPincode.set(true);this.pincodeError.set('');
    const promise=(async()=>{
      try{
        const result=await this.data.quote(key,items,pin,promo||undefined);
        if(request!==this.deliveryRequest||signature!==this.deliverySignature(this.cart.storeKey(),this.cart.lines(),this.pincode))return null;
        this.deliveryQuote.set(result);this.deliveryFingerprint=signature;this.pincodeChecked.set(!!result);
        const serviceable=!!result?.isDeliveryEnabled&&!!result.isPincodeServiceable;
        this.validatedPincode=serviceable?pin:'';
        if(serviceable&&result)this.saveValidatedState(key,pin,result);else this.clearValidatedState(key);
        return result;
      }catch(error){
        if(request===this.deliveryRequest){this.pincodeError.set(this.quoteError(error,true));this.clearValidatedState(key);}
        return null;
      }finally{if(request===this.deliveryRequest)this.checkingPincode.set(false);}
    })();
    this.deliveryInFlight={signature,request,promise};
    try{return await promise;}finally{if(this.deliveryInFlight?.promise===promise)this.deliveryInFlight=null;}
  }
  async refresh():Promise<void>{this.checking.set(true);this.refreshFailed.set(false);try{const key=this.route.parent?.snapshot.paramMap.get('storeKey')??'';const[products,store]=await Promise.all([this.data.getProducts(key),this.data.getStore(key),this.session.refresh(key)]);const customer=this.session.customer();if(customer){this.customerName=customer.name;this.mobile=customer.mobile??'';this.email=customer.email??'';try{const addresses=await this.data.getAddresses(key);this.addresses.set(addresses);const preferred=addresses.find(x=>x.isDefault)??addresses[0];if(this.isCheckoutPage()&&preferred&&!this.pincode)this.applyAddress(preferred);}catch{}}this.cart.refreshProducts(products.items);this.paymentMethods.set(store?.paymentMethods??[]);if(!this.paymentMethods().some(x=>x.code===this.selectedPayment()))this.selectedPayment.set(this.paymentMethods().find(x=>x.isDefault)?.code??this.paymentMethods()[0]?.code??'');if(this.initialized()&&this.cart.lines().length){await this.loadQuote(key,this.cart.lines());if(this.isCheckoutPage()&&/^[0-9]{6}$/.test(this.pincode))await this.loadDeliveryQuote(key,this.cart.lines(),this.pincode);}}catch{this.refreshFailed.set(true);}finally{this.checking.set(false);}}
  selectAddress(event:Event):void{const id=(event.target as HTMLSelectElement).value;this.selectedAddressId=id;const address=this.addresses().find(x=>x.addressId===id);if(address){this.applyAddress(address);return;}this.recipientName=this.customerName;this.addressLine1='';this.addressLine2='';this.landmark='';this.city='';this.district='';this.state='';this.country='India';this.deliveryAddress='';this.pincodeChanged({target:{value:''}} as unknown as Event);}
  private applyAddress(address:CustomerAddress):void{this.selectedAddressId=address.addressId;this.customerName=address.recipientName;this.recipientName=address.recipientName;this.mobile=address.mobile;this.addressLine1=address.addressLine1;this.addressLine2=address.addressLine2??'';this.landmark=address.landmark??'';this.city=address.city;this.district='';this.state=address.state;this.country='India';this.deliveryAddress=this.formatDeliveryAddress();this.pincodeChanged({target:{value:address.pincode}} as unknown as Event);}
  pincodeChanged(event:Event):void{
    if(!this.isCheckoutPage())return;
    const next=this.fieldValue(event).replace(/[^0-9]/g,'').slice(0,6);
    const signature=this.deliverySignature(this.cart.storeKey(),this.cart.lines(),next);
    if(next===this.pincode&&((this.pincodeChecked()&&this.deliveryFingerprint===signature)||(this.deliveryInFlight?.signature===signature&&this.checkingPincode())))return;
    this.pincode=next;this.invalidateDelivery();this.pincodeError.set('');this.clearValidatedState(this.cart.storeKey());
    if(this.pincodeDebounce)clearTimeout(this.pincodeDebounce);
    if(next.length===6&&this.cart.lines().length)this.pincodeDebounce=setTimeout(()=>this.checkPincode(),350);
  }
  promoCode='';
  promoCodeChanged(event:Event):void{const restorePricing=this.promoBusy();++this.promoRequest;this.promoBusy.set(false);this.promoCode=this.fieldValue(event);this.promoError.set('');this.dismissOfferCelebration();if(restorePricing&&this.cart.storeKey()&&this.cart.lines().length)void this.loadQuote(this.cart.storeKey(),this.cart.lines());}
  async applyPromoCode():Promise<void>{
    const code=this.promoCode.trim();this.promoCode=code;this.promoError.set('');this.dismissOfferCelebration();
    if(!code){this.promoError.set('Enter an offer code.');return;}
    if(this.promoBusy())return;
    const request=++this.promoRequest,key=this.cart.storeKey(),items=this.cart.lines().map(line=>({...line})),lines=this.linesFingerprint(items);
    this.promoBusy.set(true);
    try{
      let result:CartQuote|null=null;let validationError:unknown=null;
      try{result=await this.data.previewQuote(key,items,code||undefined);}catch(error){validationError=error;}
      if(!this.isCurrentPromoAttempt(request,key,lines,code))return;
      if(result?.promotionId&&result.promotionCode?.toLowerCase()===code.toLowerCase()&&Number.isFinite(result.finalPayableAmount)&&result.finalPayableAmount>=0){
        const alreadyApplied=this.appliedPromoCode().toLowerCase()===result.promotionCode.toLowerCase()&&this.pricingQuote()?.promotionId===result.promotionId;
        ++this.quoteRequest;this.quoteInFlight=null;this.invalidateDelivery();
        this.appliedPromoCode.set(result.promotionCode);this.promoCode=result.promotionCode;
        this.quote.set(result);this.quoteFingerprint=this.pricingSignature(key,items,result.promotionCode);
        this.quoteLoadError.set('');this.pricingLoading.set(false);this.savePromo();if(!alreadyApplied&&this.claimPromoCelebration(key,result.promotionId,result.promotionCode))this.showOfferCelebration(result.promotionCode);
        return;
      }
      this.promoError.set(validationError?this.promoMessage(validationError):'Offer code is not valid.');
      const confirmedPromo=this.appliedPromoCode();
      const restored=await this.loadQuote(key,items,'',confirmedPromo);
      if(!restored&&confirmedPromo&&this.isCurrentPromoAttempt(request,key,lines,code)&&this.isPromoValidationFailure(this.lastPricingError)){
        this.appliedPromoCode.set('');this.savePromo();
        await this.loadQuote(key,items,'','');
      }
    }finally{if(request===this.promoRequest)this.promoBusy.set(false);}
  }
  async removePromoCode():Promise<void>{if(this.promoBusy())return;const request=++this.promoRequest,key=this.cart.storeKey(),items=this.cart.lines().map(line=>({...line})),lines=this.linesFingerprint(items);this.promoBusy.set(true);this.promoError.set('');this.dismissOfferCelebration();this.appliedPromoCode.set('');this.promoCode='';this.savePromo();this.quote.set(null);this.quoteFingerprint='';this.quoteLoadError.set('');this.invalidateDelivery();try{const result=await this.loadQuote(key,items,'','');if(request!==this.promoRequest||key!==this.cart.storeKey()||lines!==this.linesFingerprint(this.cart.lines()))return;if(!result&&!this.quoteLoadError())this.quoteLoadError.set('Unable to calculate cart pricing. Please try again.');}finally{if(request===this.promoRequest)this.promoBusy.set(false);}}
  private isCurrentPromoAttempt(request:number,key:string,lines:string,code:string):boolean{return request===this.promoRequest&&key===this.cart.storeKey()&&lines===this.linesFingerprint(this.cart.lines())&&code===this.promoCode.trim();}
  private claimPromoCelebration(storeKey:string,promotionId:string,code:string):boolean{const identity=`${promotionId}:${code.trim().toLowerCase()}`;if(this.celebratedPromoIdentities.has(identity))return false;const key=this.storageKey(storeKey)+':celebrated-promo';try{if(sessionStorage.getItem(key)===identity)return false;sessionStorage.setItem(key,identity);}catch{}this.celebratedPromoIdentities.add(identity);return true;}
  private promoMessage(error:unknown):string{const body=error instanceof HttpErrorResponse?error.error as {message?:string;detail?:string;title?:string}|null:null;return (body?.message??body?.detail??body?.title??'Offer code could not be applied. Please try again.').replace(/promo code/gi,'offer code');}
  private isPromoValidationFailure(error:unknown):boolean{const message=this.promoMessage(error).toLocaleLowerCase();return message.includes('offer code')&&/not valid|expired|not applicable|no longer available|more to use|minimum (?:purchase|order)|minimum spend/.test(message);}
  dismissOfferCelebration():void{if(this.offerCelebrationTimer)clearTimeout(this.offerCelebrationTimer);this.offerCelebrationTimer=undefined;this.offerCelebration.set('');}
  private showOfferCelebration(code:string):void{this.dismissOfferCelebration();this.offerCelebration.set(code);this.offerCelebrationTimer=setTimeout(()=>{this.offerCelebration.set('');this.offerCelebrationTimer=undefined;},4500);}
  private quoteError(error:unknown,delivery=false):string{
    if(error instanceof HttpErrorResponse&&error.status===429){const retryAfter=Number(error.headers.get('Retry-After'));const delay=Number.isFinite(retryAfter)&&retryAfter>0?retryAfter*1000:10000;if(delivery){this.retryBlockedUntil.set(Date.now()+delay);setTimeout(()=>this.retryBlockedUntil.set(0),delay);return `Too many delivery checks. Please try again in ${Math.ceil(delay/1000)} seconds.`;}return 'Cart pricing is temporarily busy. Please try again shortly.';}
    return delivery?'Unable to check delivery availability. Please try again.':'Unable to calculate cart pricing. Please try again.';
  }
  private linesFingerprint(lines:readonly CartLine[]):string{return lines.map(line=>`${line.product.id}:${line.quantity}`).join('|');}
  retryBlocked():boolean{return !this.isCheckoutPage()||this.retryBlockedUntil()>Date.now();}
  checkPincode():void{
    if(!this.isCheckoutPage())return;
    const pin=this.pincode;
    if(!/^[0-9]{6}$/.test(pin)){this.pincodeError.set('Enter a valid 6-digit pincode.');this.pincodeChecked.set(false);return;}
    if(this.retryBlocked()||!this.cart.lines().length)return;
    if(this.authoritativeQuote())return;
    void this.loadDeliveryQuote(this.cart.storeKey(),this.cart.lines(),pin);
  }
  authoritativeQuote():CartQuote|null{
    const priced=this.deliveryQuote();
    return priced&&this.pincodeChecked()&&this.validatedPincode===this.pincode&&this.deliveryFingerprint===this.deliverySignature(this.cart.storeKey(),this.cart.lines(),this.pincode)?priced:null;
  }
  canProceedToCheckout():boolean{return!this.checking()&&!this.pricingLoading()&&!this.promoBusy()&&!this.quoteLoadError()&&!!this.pricingQuote()&&!this.hasFulfillmentWarning(this.pricingQuote())&&!this.refreshFailed()&&this.cart.lines().length>0&&this.cart.lines().every(line=>line.product.available);}
  canCheckout():boolean{
    const priced=this.authoritativeQuote();
    return!this.checking()&&!this.submitting()&&!this.pricingLoading()&&!this.promoBusy()&&!this.quoteLoadError()&&!this.refreshFailed()&&!!this.pricingQuote()&&!this.hasFulfillmentWarning(this.pricingQuote())&&!this.hasFulfillmentWarning(priced)&&!!this.selectedPayment()&&this.cart.lines().length>0&&this.cart.lines().every(line=>line.product.available)&&!!(this.recipientName.trim()||this.customerName.trim())&&this.mobileDigits().length>=10&&this.mobileDigits().length<=15&&!!this.addressLine1.trim()&&!!this.city.trim()&&!!this.state.trim()&&/^[0-9]{6}$/.test(this.pincode)&&!!priced?.isDeliveryEnabled&&!!priced.isPincodeServiceable;
  }
  private mobileDigits():string{return this.mobile.replace(/\D/g,'');}
  addressSummary(address:CustomerAddress):string{return [address.addressType,address.recipientName,address.addressLine1,address.addressLine2,address.landmark,address.city,address.state,address.pincode].map(value=>value?.trim()).filter(Boolean).join(' · ');}
  placeOrderReason():string{if(this.checking())return 'Refreshing checkout details...';if(this.refreshFailed())return 'Unable to refresh order details. Retry.';if(this.submitting())return 'Placing order...';if(this.pricingLoading())return 'Refreshing your total...';if(!this.pricingQuote())return this.quoteLoadError()||'Loading your total...';if(this.hasFulfillmentWarning(this.pricingQuote()))return this.pricingQuote()?.fulfillmentWarnings?.[0]?.message??'Items are not available for this delivery address.';if(!this.selectedPayment())return 'Select a payment method.';if(!this.cart.lines().length)return 'Add a product before placing the order.';if(this.cart.lines().some(line=>!line.product.available))return 'Cart items are unavailable.';if(!(this.recipientName.trim()||this.customerName.trim()))return 'Complete recipient details.';if(this.mobileDigits().length<10||this.mobileDigits().length>15)return 'Enter a valid mobile number.';if(!this.addressLine1.trim()||!this.city.trim()||!this.state.trim())return 'Complete the delivery address.';if(!/^[0-9]{6}$/.test(this.pincode))return 'Enter a valid six-digit PIN.';if(this.checkingPincode())return 'Checking delivery availability...';if(this.pincodeError())return this.pincodeError();const delivery=this.deliveryQuote();if(!this.pincodeChecked()||!delivery)return 'Delivery is being validated for the selected address.';if(this.hasFulfillmentWarning(delivery))return delivery.fulfillmentWarnings?.[0]?.message??'Items are not available for this delivery address.';if(!delivery.isDeliveryEnabled)return 'Delivery is unavailable for this store.';if(!delivery.isPincodeServiceable)return 'Delivery is not available at this pincode. Please choose another address.';return '';}
  private hasFulfillmentWarning(quote:CartQuote|null):boolean{return!!quote?.fulfillmentWarnings?.length;}
  displaySubtotal():number|null{return this.pricingQuote()?.merchandiseSubtotal??null;}
  displayProductSavings():number|null{return this.pricingQuote()?.productSavings??null;}
  displayTax():number|null{return this.pricingQuote()?.merchandiseTaxAmount??null;}
  displayPromotionDiscount():number|null{return this.pricingQuote()?.promotionDiscount??null;}
  displayDelivery():number|null{return this.pricingQuote()?.deliveryCharge??null;}
  displayTotal():number|null{return this.pricingQuote()?.finalPayableAmount??null;}
  amountLabel(value:number|null):string{return value===null?(this.quoteLoadError()?'Unavailable':'Loading…'):`₹${value.toFixed(2)}`;}
  discountLabel(value:number|null):string{return value===null?(this.quoteLoadError()?'Unavailable':'Loading…'):value>0?`-₹${value.toFixed(2)}`:'₹0.00';}
  coinDiscountLabel():string{return 'Not applied';}
  summaryNote():string{return this.quoteLoadError()||(!this.pricingQuote()?'Bill details are being calculated by the store.':'');}
  retryQuote():void{if(this.cart.storeKey()&&this.cart.lines().length)void this.loadQuote(this.cart.storeKey(),this.cart.lines());}
  private checkoutInProgress=false;
  private storageKey(storeKey:string):string{return 'khata-dhari-storefront-serviceability:'+storeKey+':'+(this.session.customer()?.id?.toLowerCase()??'guest');}private readServiceabilityState(storeKey:string):CartServiceabilityState|null{if(!storeKey||typeof sessionStorage==='undefined')return null;try{const raw=sessionStorage.getItem(this.storageKey(storeKey));const state=raw?JSON.parse(raw) as Partial<CartServiceabilityState>:null;const quote=state?.quote;return state?.storeKey===storeKey&&state.verified===true&&typeof state.verifiedAtUtc==='number'&&Date.now()-state.verifiedAtUtc<SERVICEABILITY_TTL_MS&&typeof state.pincode==='string'&&/^[0-9]{6}$/.test(state.pincode)&&!!quote&&quote.isDeliveryEnabled===true&&quote.isPincodeServiceable===true?state as CartServiceabilityState:null;}catch{return null;}}
  private readSavedPincode(storeKey:string):string{if(!storeKey||typeof localStorage==='undefined')return '';try{const raw=localStorage.getItem(this.storageKey(storeKey));const state=raw?JSON.parse(raw) as {storeKey?:string;pincode?:string}:null;return state?.storeKey===storeKey&&typeof state.pincode==='string'&&/^[0-9]{6}$/.test(state.pincode)?state.pincode:'';}catch{return '';}}
  private saveValidatedState(storeKey:string,pincode:string,quote:CartQuote):void{if(!storeKey)return;try{sessionStorage?.setItem(this.storageKey(storeKey),JSON.stringify({storeKey,pincode,verified:true,verifiedAtUtc:Date.now(),quote} satisfies CartServiceabilityState));}catch{}try{localStorage?.setItem(this.storageKey(storeKey),JSON.stringify({storeKey,pincode}));}catch{}}
  private clearValidatedState(storeKey:string):void{if(!storeKey)return;try{sessionStorage?.removeItem(this.storageKey(storeKey));}catch{}try{localStorage?.removeItem(this.storageKey(storeKey));}catch{}}
  actionLabel():string{return this.selectedPayment()==='COD'?'Place order':'Pay securely';}
  submitCheckout(event:Event):void{event.preventDefault();void this.checkout();}fieldValue(event:Event):string{return(event.target as HTMLInputElement|HTMLTextAreaElement).value;}
  private formatDeliveryAddress():string{return [this.addressLine1,this.addressLine2,this.landmark,this.city,this.district,this.state,this.country].map(x=>x.trim()).filter(Boolean).join(', ');}
  private checkoutContext():string{return JSON.stringify([this.pricingSignature(this.cart.storeKey(),this.cart.lines()),this.pincode,this.selectedAddressId,this.customerName,this.recipientName,this.mobile,this.email,this.addressLine1,this.addressLine2,this.landmark,this.city,this.district,this.state,this.country,this.selectedPayment()]);}
  async checkout():Promise<void>{
    if(this.checkoutInProgress||!this.canCheckout())return;
    this.checkoutInProgress=true;
    const displayed=this.pricingQuote()!,context=this.checkoutContext(),key=this.cart.storeKey();
    this.submitting.set(true);this.activity.record(key,'CHECKOUT_STARTED');this.checkoutError.set('');this.checkoutNotice.set('');this.orderNumber.set('');
    const review='Your total has been updated. Review Amount to Pay and confirm again before continuing.';
    try{
      const preview=await this.loadQuote(key,this.cart.lines());
      if(!preview)return;
      if(this.hasFulfillmentWarning(preview)){this.checkoutError.set(preview.fulfillmentWarnings?.[0]?.message??'Items are not available for this delivery address.');return;}
      if(context!==this.checkoutContext()){this.checkoutNotice.set('Checkout details changed. Please review and confirm again.');return;}
      if(preview.finalPayableAmount!==displayed.finalPayableAmount){this.checkoutNotice.set(review);return;}
      const finalQuote=await this.loadDeliveryQuote(key,this.cart.lines(),this.pincode);
      if(context!==this.checkoutContext()){this.checkoutNotice.set('Checkout details changed. Please review and confirm again.');return;}
      if(!finalQuote?.isDeliveryEnabled||!finalQuote.isPincodeServiceable){this.checkoutError.set(this.pincodeError()||'Delivery is not available at this pincode. Please choose another address.');return;}
      if(this.hasFulfillmentWarning(finalQuote)){this.checkoutError.set(finalQuote.fulfillmentWarnings?.[0]?.message??'Items are not available for this delivery address.');return;}
      if(finalQuote.finalPayableAmount!==preview.finalPayableAmount){await this.loadQuote(key,this.cart.lines());this.checkoutNotice.set(review);return;}
      const recipient=(this.recipientName||this.customerName).trim();this.deliveryAddress=this.formatDeliveryAddress();
      const fingerprint=JSON.stringify([context,preview.finalPayableAmount]);
      if(fingerprint!==this.checkoutFingerprint){this.checkoutFingerprint=fingerprint;this.checkoutKey=crypto.randomUUID();}
      const result=await this.data.checkout(key,{customerName:recipient,recipientName:recipient,mobile:this.mobile.trim(),email:this.email.trim()||undefined,deliveryAddress:this.deliveryAddress,pincode:this.pincode,addressLine1:this.addressLine1.trim(),addressLine2:this.addressLine2.trim()||undefined,landmark:this.landmark.trim()||undefined,city:this.city.trim(),district:this.district.trim()||undefined,state:this.state.trim(),country:this.country,promoCode:this.appliedPromoCode()||undefined,expectedPayableAmount:preview.finalPayableAmount},this.cart.lines(),this.checkoutKey,this.selectedPayment());
      this.orderNumber.set(result.orderNumber);this.activity.record(key,'ORDER_PLACED');
      if(result.customerSessionToken){this.session.establish(key,result.customerSessionToken);await this.session.refresh(key);await this.wishlist.switchCustomer(key,this.session.customer()?.id??null);}
      if(result.checkoutUrl){const checkoutUrl=new URL(result.checkoutUrl);if(!['https:','upi:'].includes(checkoutUrl.protocol))throw new Error('Invalid checkout URL.');window.location.assign(checkoutUrl);}
      else{this.cart.clear();this.checkoutNotice.set(result.customerMessage??'Your order has been created.');await this.router.navigate(['/',key,'orders',result.orderId]);}
    }catch(error){
      const body=error instanceof HttpErrorResponse?error.error as{message?:string;detail?:string;title?:string}|null:null;
      const message=body?.message??body?.detail??body?.title??'Checkout could not be started. Please try again.';
      this.checkoutError.set(message);
      if(message==='The order total has changed. Please review the updated amount and confirm again.') {await this.loadQuote(key,this.cart.lines());this.checkoutNotice.set(review);}
    }finally{this.checkoutInProgress=false;this.submitting.set(false);}
  }
  imageFailed(event:Event):void{(event.target as HTMLImageElement).src='/images/product-placeholder.svg';}
}
