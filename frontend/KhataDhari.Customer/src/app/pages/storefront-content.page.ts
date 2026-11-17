import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { StorefrontDataService } from '../data/storefront-data.service';
import { Store } from '../models/storefront.models';
import { CustomerAuthService, CustomerPolicy } from '../customer-auth.service';

interface StorefrontFaq { question: string; answer: string; }

const FAQS: readonly StorefrontFaq[] = [
  { question: 'How do I place an order?', answer: 'Add available products to your cart, review your delivery details and payment method, and complete checkout. After the order is placed, you can track it from My Account > Orders where supported by the store.' },
  { question: 'Can I change or cancel an order?', answer: "Order cancellation or change availability depends on the order status and the retailer's policy. Use the available order actions or contact the retailer when an action is no longer available online." },
  { question: 'How do I know whether delivery is available?', answer: 'Enter your delivery pincode and use Check Delivery Availability. Delivery availability and applicable charges are determined by the retailer’s configured service area and current cart.' },
  { question: 'When is delivery free?', answer: 'Free delivery is applied when the current order satisfies the retailer’s configured free-delivery conditions. The Cart shows your current eligibility.' },
  { question: 'What payment methods are available?', answer: 'Available payment methods are shown during checkout and depend on the retailer’s payment configuration. Only methods currently enabled for the store can be selected.' },
  { question: 'What if an online payment fails?', answer: 'Do not repeatedly pay for the same order without checking its status. Review My Orders and, if the payment or order status is unclear, contact the retailer or support.' },
  { question: 'How do I use an offer code?', answer: 'Enter the offer code in the Cart and select Apply Offer. Eligibility, validity and the discount amount are verified by the store before the total is updated.' },
  { question: 'Can I return an item?', answer: 'Return eligibility depends on the retailer’s configured return policy, product or order status and applicable return conditions. Review the return policy shown for the store or order before requesting a return.' },
  { question: 'Why do I need my mobile number?', answer: 'Your mobile number is used for Storefront customer authentication and to associate your orders and account with the correct customer profile.' },
  { question: 'Can I save products for later?', answer: 'Yes. Use Wishlist while signed in to save products for your Storefront account.' },
];

@Component({
  selector: 'shop-content-page',
  standalone: true,
  template: `
    <section class="account-section">
      <header class="section-header"><div><span class="eyebrow">STOREFRONT</span><h2>{{title}}</h2><p>{{kind==='support'?'Help with ordering, delivery, payments and your Storefront account.':kind==='terms'?'The terms that apply to this retailer Storefront.':kind==='cancellation'?'Information about cancellation, returns, replacements and refunds.':kind==='reviews'?'Information about product ratings and reviews.':'How this Storefront handles customer and transaction information.'}}</p></div></header>
      @if (kind === 'support') {
        <div class="content-card support-content">
          @if (store?.deliveryMessage) { <div class="store-note"><strong>Store delivery note</strong><p>{{store?.deliveryMessage}}</p></div> }
          <h3>Frequently asked questions</h3>
          @for (faq of faqs; track faq.question) { <details class="faq-card"><summary>{{faq.question}}</summary><p>{{faq.answer}}</p></details> }
        </div>
      } @else if (kind === 'terms') {
        @if(policyLoading){<div class="content-card"><p>Loading the current policy…</p></div>}@else if(policyBlocks.length){<div class="content-card policy-content"><p class="policy-meta">Version {{termsPolicy?.version}} · {{termsPolicy?.languageCode}} · Effective {{policyEffectiveDate}}</p>@for(block of policyBlocks;track $index){@if(block.heading){<section><h3>{{block.heading}}</h3>@if(block.body){<p>{{block.body}}</p>}</section>}@else if(block.body){<p>{{block.body}}</p>}}</div>}@else{<div class="content-card"><p role="status">The store has not published its Terms &amp; Conditions yet. Please check back later.</p></div>}
      } @else if (kind === 'privacy') {
        @if(policyLoading){<div class="content-card"><p>Loading the current policy…</p></div>}@else if(policyBlocks.length){<div class="content-card policy-content"><p class="policy-meta">Version {{privacyPolicy?.version}} · {{privacyPolicy?.languageCode}} · Effective {{policyEffectiveDate}}</p>@for(block of policyBlocks;track $index){@if(block.heading){<section><h3>{{block.heading}}</h3>@if(block.body){<p>{{block.body}}</p>}</section>}@else if(block.body){<p>{{block.body}}</p>}}</div>}@else{<div class="content-card"><p role="status">The store has not published a Privacy Notice yet. Please check back later.</p></div>}
      } @else if(kind==='cancellation') {
        <div class="content-card policy-content"><p class="policy-intro">Cancellation, return, replacement and refund eligibility depends on the product, order status, retailer policy displayed for the order and applicable law.</p><p>Use the available order actions or contact the retailer using the support details shown in your order. The retailer’s applicable policy and order-specific terms govern the request. Nothing on this page limits a consumer right available under law.</p><p>Review product and checkout information before ordering. If a payment is debited but the order status is unclear, check My Orders and contact the retailer with the transaction reference.</p></div>
      } @else {
        <div class="content-card"><h3>Ratings &amp; Reviews</h3><p>Your product reviews are managed on the relevant product pages and remain associated with your authenticated Storefront account.</p></div>
      }
    </section>
  `,
  styleUrl: './account-section.css',
})
export class StorefrontContentPage implements OnInit {
  readonly faqs = FAQS;
  policyLoading = false;
  termsPolicy: CustomerPolicy | null = null;
  privacyPolicy: CustomerPolicy | null = null;
  policyEffectiveDate = '';
  policyBlocks: {heading:string;body:string}[] = [];
  storeKey = '';
  title = '';
  kind = '';
  store: Store | null = null;

  constructor(private readonly route: ActivatedRoute, private readonly data: StorefrontDataService, private readonly auth: CustomerAuthService) {}

  ngOnInit(): void {
    this.storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? this.route.parent?.parent?.snapshot.paramMap.get('storeKey') ?? '';
    this.kind = this.route.snapshot.data['kind'] ?? 'support';
    this.title = this.kind === 'support' ? 'Support & FAQs' : this.kind === 'terms' ? 'Terms & Conditions' : this.kind === 'cancellation' ? 'Cancellation, Return & Refund Policy' : this.kind === 'reviews' ? 'Ratings & Reviews' : 'Privacy Policy';
    void this.data.getStore(this.storeKey).then(store => this.store = store);
    if(this.kind==='terms'||this.kind==='privacy') void this.loadPolicy();
  }

  private async loadPolicy():Promise<void>{this.policyLoading=true;try{const policies=await this.auth.policies(this.storeKey);this.termsPolicy=policies.find(x=>x.policyType==='CUSTOMER_TERMS'&&x.isPublished&&x.acceptanceRequired)??null;this.privacyPolicy=policies.find(x=>x.policyType==='PRIVACY_NOTICE'&&x.isPublished&&x.acceptanceRequired)??null;const selected=this.kind==='terms'?this.termsPolicy:this.privacyPolicy;this.policyBlocks=selected?this.toBlocks(selected.content):[];this.policyEffectiveDate=selected?this.formatPolicyDate(selected.effectiveAtUtc):'';}catch{this.policyBlocks=[];this.policyEffectiveDate='';}finally{this.policyLoading=false;}}
  private formatPolicyDate(value:string):string{const date=new Date(value);return Number.isNaN(date.getTime())?'':new Intl.DateTimeFormat('en-IN',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'}).format(date);}
  private toBlocks(content:string):{heading:string;body:string}[]{const blocks:{heading:string;body:string}[]=[];for(const paragraph of content.split(/\r?\n\s*\r?\n/).map(x=>x.trim()).filter(Boolean)){const heading=paragraph.match(/^#{1,3}\s+(.+)$/m)?.[1];const body=paragraph.replace(/^#{1,3}\s+.+\r?\n?/m,'').trim();blocks.push({heading:heading??'',body});}return blocks;}
}
