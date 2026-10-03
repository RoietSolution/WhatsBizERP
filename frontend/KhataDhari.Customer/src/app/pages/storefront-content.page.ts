import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { StorefrontDataService } from '../data/storefront-data.service';
import { Store } from '../models/storefront.models';

interface StorefrontFaq { question: string; answer: string; }
interface StorefrontSection { heading: string; body: string; }

const FAQS: readonly StorefrontFaq[] = [
  { question: 'How do I place an order?', answer: 'Add available products to your cart, review your delivery details and payment method, and complete checkout. After the order is placed, you can track it from My Account > Orders where supported by the store.' },
  { question: 'Can I change or cancel an order?', answer: "Order cancellation or change availability depends on the order status and the retailer's policy. Use the available order actions or contact the retailer when an action is no longer available online." },
  { question: 'How do I know whether delivery is available?', answer: 'Enter your delivery pincode and use Check Delivery Availability. Delivery availability and applicable charges are determined by the retailer’s configured service area and current cart.' },
  { question: 'When is delivery free?', answer: 'Free delivery is applied when the current order satisfies the retailer’s configured free-delivery conditions. The Cart shows your current eligibility.' },
  { question: 'What payment methods are available?', answer: 'Available payment methods are shown during checkout and depend on the retailer’s payment configuration. Only methods currently enabled for the store can be selected.' },
  { question: 'What if an online payment fails?', answer: 'Do not repeatedly pay for the same order without checking its status. Review My Orders and, if the payment or order status is unclear, contact the retailer or support.' },
  { question: 'How do I use a promo code?', answer: 'Enter the code in the Cart and select Apply. Eligibility, validity and the discount amount are verified by the store before the total is updated.' },
  { question: 'Can I return an item?', answer: 'Return eligibility depends on the retailer’s configured return policy, product or order status and applicable return conditions. Review the return policy shown for the store or order before requesting a return.' },
  { question: 'Why do I need my mobile number?', answer: 'Your mobile number is used for Storefront customer authentication and to associate your orders and account with the correct customer profile.' },
  { question: 'Can I save products for later?', answer: 'Yes. Use Wishlist while signed in to save products for your Storefront account.' },
];

const TERMS: readonly StorefrontSection[] = [
  { heading: '1. About this Storefront', body: 'This online Storefront is operated by the displayed retailer and powered by KhataDhari technology. The retailer is the seller and merchant responsible for products, orders and fulfilment.' },
  { heading: '2. Customer Account', body: 'Provide accurate name, mobile and delivery information. You are responsible for use of your authenticated Storefront session and account.' },
  { heading: '3. Product Information', body: 'The retailer may update product availability, pricing, images, descriptions, taxes and stock. The final checkout and order values presented by the system are authoritative for the transaction, subject to retailer acceptance where applicable.' },
  { heading: '4. Orders', body: 'Submitting an order creates an order request or transaction through the Storefront. Order acceptance, fulfilment, cancellation and availability remain subject to retailer operations and applicable policy.' },
  { heading: '5. Pricing & Promotions', body: 'Promotions, free delivery and promo codes apply only when current eligibility conditions are satisfied. Expired or ineligible offers cannot be claimed merely because they were previously displayed.' },
  { heading: '6. Payments', body: 'Only payment methods enabled for the retailer are available. Treat an online payment as successful only after its status is successfully confirmed.' },
  { heading: '7. Delivery', body: 'Serviceability, delivery charges, free-delivery eligibility and delivery timelines depend on retailer configuration, delivery location and order.' },
  { heading: '8. Cancellation, Returns & Refunds', body: 'Cancellation, return and refund handling is governed by the retailer’s displayed policy and applicable law. No fixed period is promised unless configured and shown by the retailer.' },
  { heading: '9. Customer Responsibilities', body: 'Use accurate information and the Storefront lawfully. Do not abuse, defraud or manipulate promotions, payments, accounts or Storefront functionality.' },
  { heading: '10. Platform Availability', body: 'Reasonable availability may be interrupted for maintenance, network issues, third-party payment or service failures, or other technical issues.' },
  { heading: '11. Contact', body: 'For product, order or delivery issues, contact the retailer using the support details made available by the store.' },
];

const PRIVACY: readonly StorefrontSection[] = [
  { heading: 'Information you may provide', body: 'The Storefront may receive your name, mobile number, delivery addresses, pincode, order information, wishlist and account interactions, ratings or reviews where used, and support information.' },
  { heading: 'Transactional information', body: 'The Storefront processes cart and order totals, promotions, delivery or serviceability information, and payment status or reference information where applicable.' },
  { heading: 'How information is used', body: 'Information is used for authentication, account and profile operation, order processing, delivery and serviceability, customer support, fraud and security controls, transaction records and improving Storefront operation.' },
  { heading: 'Payments', body: 'Online payment processing may be handled by the configured payment provider. Relevant transaction status and reference data may be retained; this Storefront does not claim to store card or UPI credentials.' },
  { heading: 'Sharing', body: 'Information may be available to the retailer and service providers necessary for order, payment and delivery operation.' },
  { heading: 'Security and retention', body: 'Reasonable safeguards are used to protect Storefront information, but no method can promise absolute security. Information is retained as reasonably necessary for Storefront, transactional, legal, accounting and security purposes under applicable requirements and policies.' },
  { heading: 'Your choices', body: 'You can update available profile and address information and contact the retailer or support regarding account and privacy questions.' },
  { heading: 'Cookies and browser storage', body: 'The Storefront may use browser storage and session mechanisms needed for authentication, cart state, preferences and PWA functionality. It does not claim advertising tracking, sale of personal data or analytics systems unless separately disclosed by the retailer.' },
];

@Component({
  selector: 'shop-content-page',
  standalone: true,
  template: `
    <section class="account-section">
      <header class="section-header"><div><span class="eyebrow">STOREFRONT</span><h2>{{title}}</h2><p>{{kind==='support'?'Help with ordering, delivery, payments and your Storefront account.':kind==='terms'?'The terms that apply to this retailer Storefront.':'How this Storefront handles customer and transaction information.'}}</p></div></header>
      @if (kind === 'support') {
        <div class="content-card support-content">
          @if (store?.deliveryMessage) { <div class="store-note"><strong>Store delivery note</strong><p>{{store?.deliveryMessage}}</p></div> }
          <h3>Frequently asked questions</h3>
          @for (faq of faqs; track faq.question) { <details class="faq-card"><summary>{{faq.question}}</summary><p>{{faq.answer}}</p></details> }
        </div>
      } @else if (kind === 'terms') {
        <div class="content-card policy-content"><p class="policy-intro">These terms describe use of this retailer Storefront. KhataDhari provides the technology; the displayed retailer remains responsible for products, selling, fulfilment and retailer policies.</p>@for (section of terms; track section.heading) { <section><h3>{{section.heading}}</h3><p>{{section.body}}</p></section> }<p class="policy-note">Nothing in these terms excludes applicable consumer rights under law.</p></div>
      } @else if (kind === 'privacy') {
        <div class="content-card policy-content">@for (section of privacy; track section.heading) { <section><h3>{{section.heading}}</h3><p>{{section.body}}</p></section> }</div>
      } @else {
        <div class="content-card"><h3>Ratings &amp; Reviews</h3><p>Your product reviews are managed on the relevant product pages and remain associated with your authenticated Storefront account.</p></div>
      }
    </section>
  `,
  styleUrl: './account-section.css',
})
export class StorefrontContentPage implements OnInit {
  readonly faqs = FAQS;
  readonly terms = TERMS;
  readonly privacy = PRIVACY;
  storeKey = '';
  title = '';
  kind = '';
  store: Store | null = null;

  constructor(private readonly route: ActivatedRoute, private readonly data: StorefrontDataService) {}

  ngOnInit(): void {
    this.storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? this.route.parent?.parent?.snapshot.paramMap.get('storeKey') ?? '';
    this.kind = this.route.snapshot.data['kind'] ?? 'support';
    this.title = this.kind === 'support' ? 'Support & FAQs' : this.kind === 'terms' ? 'Terms & Conditions' : this.kind === 'reviews' ? 'Ratings & Reviews' : 'Privacy Policy';
    void this.data.getStore(this.storeKey).then(store => this.store = store);
  }
}