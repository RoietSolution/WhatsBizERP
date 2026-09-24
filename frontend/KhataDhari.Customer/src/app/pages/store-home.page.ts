import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap } from '@angular/router';
import { Category, Product } from '../models/storefront.models';
import { StorefrontDataService } from '../data/storefront-data.service';
import { ProductCardComponent } from '../shared/product-card.component';
import { environment } from '../../environments/environment';

@Component({
  standalone: true,
  imports: [ProductCardComponent],
  template: `
    @if (storeKey) {
      <section class="hero">
        <div class="hero-copy"><span class="hero-kicker">YOUR DAILY GOODNESS</span><h1>Fresh finds,<br /><em>right around you.</em></h1>
          <p>Picked with care. Delivered with a smile.</p><div class="hero-perk"><span>✦</span>{{ deliveryMessage() }}</div>
        </div>
        <div class="hero-art"><div class="sun"></div><div class="leaf leaf-a">✿</div><div class="leaf leaf-b">❋</div><div class="basket"><span>🥑</span><span>🥖</span><span>🍊</span><span>🥬</span></div><div class="hero-sticker">fresh<br /><b>daily</b></div></div>
        <div class="hero-grain"></div>
      </section>

      <section class="category-section">
        <div class="section-heading"><div><span class="section-kicker">A LITTLE BIT OF EVERYTHING</span><h2>Shop by category</h2></div></div>
        <div class="category-strip">
          @for (category of categories(); track category.id) {
            <button type="button" class="category-chip" [class.selected]="selectedCategory() === category.id" (click)="selectCategory(category.id)">
              <span>{{ category.emoji ?? '•' }}</span><small>{{ category.name }}</small>
            </button>
          }
        </div>
      </section>

      <section class="product-section" id="products">
        <div class="section-heading product-heading"><div><span class="section-kicker">PICKED FOR YOU</span><h2>{{ sectionTitle() }}</h2></div><span class="result-count">{{ filteredProducts().length }} items</span></div>
        @if (loading()) {
          <div class="catalog-state" role="status">Loading products…</div>
        } @else if (loadFailed()) {
          <div class="catalog-state" role="alert"><h3>We could not load the catalog</h3><p>Check your connection and try again.</p><button type="button" (click)="load()">Try again</button></div>
        } @else if (filteredProducts().length) {
          <div class="product-grid">
            @for (product of filteredProducts(); track product.id) { <shop-product-card [product]="product" [storeKey]="storeKey" /> }
          </div>
        } @else if (!products().length) {
          <div class="catalog-state"><h3>Nothing on the shelves just yet</h3><p>Please check back soon.</p></div>
        } @else {
          <div class="empty-search"><span>🔎</span><h3>No matches just yet</h3><p>Try a different search or browse all the good stuff.</p><button type="button" (click)="resetFilters()">Show all products</button></div>
        }
      </section>
      @if (isMockData) { <div class="source-note">GuturGo development demo · Prices and availability are sample data</div> }
    }
  `,
  styles: [`
    :host { display:block; }
    .hero { position:relative; display:flex; min-height:260px; overflow:hidden; padding:30px 38px; border-radius:24px; background:#e9f0df; }
    .hero-copy { position:relative; z-index:2; align-self:center; }.hero-kicker,.section-kicker { color:#6d8169;font-size:9px;letter-spacing:1.7px;font-weight:800; }
    .hero h1 { margin:11px 0 9px;color:#1c3828;font:800 clamp(29px,5vw,44px)/1.05 Manrope,sans-serif;letter-spacing:-1.9px; }.hero h1 em{color:#bd852c;font-style:normal}.hero p{margin:0;color:#728071;font-size:12px}
    .hero-perk{display:inline-flex;align-items:center;gap:7px;margin-top:18px;padding:7px 10px;border:1px solid #dae5d1;border-radius:999px;color:#285d44;background:#f6f7ef;font-size:10px;font-weight:700}.hero-perk span{color:#c9902e}
    .hero-art { position:absolute;right:0;top:0;width:43%;height:100%; }.sun { position:absolute;right:16%;top:19px;width:180px;height:180px;border-radius:50%;background:#f8e5a4;opacity:.8; }
    .basket { position:absolute;right:19%;bottom:-21px;display:flex;align-items:flex-end;justify-content:space-evenly;width:205px;height:143px;padding:12px 12px 22px;border:9px solid #aa7446;border-top:0;border-radius:13px 13px 40px 40px;background:#d9a76f;transform:rotate(-5deg);box-shadow:0 18px 25px #745b3020; }.basket:before{content:'';position:absolute;left:36px;right:36px;top:-46px;height:75px;border:9px solid #aa7446;border-bottom:0;border-radius:65px 65px 0 0}.basket span{position:relative;z-index:1;font-size:35px;filter:drop-shadow(0 4px 2px #543b251f)}
    .hero-sticker{position:absolute;right:9%;top:42px;display:grid;place-content:center;width:68px;height:68px;border-radius:50%;color:#fff;background:#d68058;text-align:center;font:700 11px/1.05 Manrope,sans-serif;transform:rotate(12deg)}.hero-sticker b{font-size:14px}.leaf{position:absolute;color:#79936c;font-size:31px}.leaf-a{right:5%;bottom:43px}.leaf-b{right:58%;top:54px;color:#8ba477;font-size:22px}.hero-grain{position:absolute;inset:0;opacity:.13;background-image:radial-gradient(#476742 0.7px,transparent .7px);background-size:9px 9px;mask-image:linear-gradient(90deg,transparent 45%,#000)}
    .category-section,.product-section{margin-top:27px}.section-heading{display:flex;align-items:end;justify-content:space-between;margin-bottom:13px}.section-heading h2{margin:4px 0 0;color:#253128;font:800 20px Manrope,sans-serif;letter-spacing:-.6px}.category-strip{display:flex;gap:10px;overflow:auto;padding:2px 0 6px;scrollbar-width:none}.category-strip::-webkit-scrollbar{display:none}.category-chip{display:flex;flex:0 0 auto;align-items:center;gap:8px;min-height:47px;padding:7px 13px;border:1px solid #e7e9e1;border-radius:14px;color:#4d594f;background:#fff;cursor:pointer;transition:.15s}.category-chip span{font-size:20px}.category-chip small{font-size:11px;font-weight:700}.category-chip:hover,.category-chip.selected{border-color:#b8d1bd;color:var(--green);background:#f0f7ef}
    .product-heading{margin-bottom:14px}.result-count{padding-bottom:3px;color:#8c9088;font-size:11px}.product-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:13px}.empty-search{display:grid;justify-items:center;padding:45px 15px;border:1px dashed #dfe3d8;border-radius:18px;text-align:center}.empty-search>span{font-size:32px}.empty-search h3{margin:10px 0 0;font:700 17px Manrope,sans-serif}.empty-search p{margin:7px 0;color:var(--muted);font-size:12px}.empty-search button{border:0;color:var(--green);background:transparent;font-size:12px;font-weight:700;cursor:pointer}.source-note{padding:26px 0 0;color:#a0a398;text-align:center;font-size:10px}
    @media(max-width:760px){.hero{min-height:220px;padding:24px 22px;border-radius:20px}.hero-art{right:-2%;width:45%}.sun{right:6%;width:145px;height:145px}.basket{right:4%;width:165px;height:120px}.basket span{font-size:29px}.basket:before{left:27px;right:27px;top:-38px;height:62px}.hero-sticker{right:4%;top:24px;width:57px;height:57px;font-size:9px}.hero-sticker b{font-size:12px}.hero h1{font-size:34px}.product-grid{grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}}
    @media(max-width:520px){.hero{min-height:205px;padding:20px 18px}.hero-copy{max-width:64%}.hero h1{font-size:30px;letter-spacing:-1.3px}.hero p{max-width:170px;font-size:11px}.hero-art{right:-13%;width:53%;transform:scale(.88);transform-origin:right center}.hero-perk{margin-top:12px;font-size:9px}.category-section,.product-section{margin-top:22px}.section-heading h2{font-size:18px}.product-grid{grid-template-columns:repeat(2,minmax(0,1fr));gap:11px}.category-chip{min-height:43px;padding:6px 11px}.category-chip small{font-size:10px}}
  `],
})
export class StoreHomePage implements OnInit {
  readonly categories = signal<Category[]>([]);
  readonly products = signal<Product[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly isMockData = environment.useMockData;
  readonly selectedCategory = signal('all');
  readonly query = signal('');
  storeKey = '';
  readonly deliveryMessage = signal('Fresh picks, close to home.');
  private readonly destroyRef = inject(DestroyRef);
  readonly filteredProducts = computed(() => {
    const term = this.query().toLocaleLowerCase().trim();
    return this.products().filter((product) => (this.selectedCategory() === 'all' || product.categoryId === this.selectedCategory())
      && (!term || `${product.name} ${product.description}`.toLocaleLowerCase().includes(term)));
  });
  readonly sectionTitle = computed(() => this.query() ? `Results for “${this.query()}”` : this.selectedCategory() === 'all' ? 'Today’s good picks' : this.categories().find((category) => category.id === this.selectedCategory())?.name ?? 'Products');

  constructor(private readonly route: ActivatedRoute, private readonly data: StorefrontDataService) {}

  ngOnInit(): void {
    this.storeKey = this.route.parent?.snapshot.paramMap.get('storeKey') ?? '';
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params: ParamMap) => {
      this.query.set(params.get('q') ?? '');
      this.selectedCategory.set(params.get('category') ?? 'all');
    });
    void this.load();
  }

  selectCategory(id: string): void { this.selectedCategory.set(id); }
  resetFilters(): void { this.selectedCategory.set('all'); this.query.set(''); }

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    try {
      const [store, categories, products] = await Promise.all([
        this.data.getStore(this.storeKey),
        this.data.getCategories(this.storeKey),
        this.data.getProducts(this.storeKey),
      ]);
      if (store) this.deliveryMessage.set(store.deliveryMessage);
      this.categories.set(categories);
      this.products.set(products);
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
