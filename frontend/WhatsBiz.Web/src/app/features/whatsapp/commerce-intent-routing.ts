import { DemoCategory, DemoCollection, DemoProduct } from './whatsapp-commerce-demo-api.service';
import { LocalCommerceIntentEngine, ProductSearchCriteria } from './local-commerce-intent-engine';

export type CommerceIntentMode = 'LOCAL_ONLY' | 'HYBRID';

export interface CommerceIntentProvider {
  parse(text: string, categories: DemoCategory[], products: DemoProduct[], collections: DemoCollection[]): Promise<ProductSearchCriteria>;
}

export class LocalCommerceIntentProvider implements CommerceIntentProvider {
  constructor(private readonly engine = new LocalCommerceIntentEngine()) {}
  async parse(text: string, categories: DemoCategory[], products: DemoProduct[], collections: DemoCollection[]) {
    return this.engine.parse(text, categories, products, collections);
  }
}

/** External providers may only return validated intent criteria; they never return products. */
export interface ExternalCommerceIntentProvider extends CommerceIntentProvider {
  readonly enabled: boolean;
}

export class CommerceIntentRouter {
  constructor(private readonly local: CommerceIntentProvider, private readonly external?: ExternalCommerceIntentProvider,
    private readonly mode: CommerceIntentMode = 'LOCAL_ONLY') {}

  async parse(text: string, categories: DemoCategory[], products: DemoProduct[], collections: DemoCollection[]) {
    const local = await this.local.parse(text, categories, products, collections);
    if (local.confidence === 'high' || this.mode === 'LOCAL_ONLY' || !this.external?.enabled) return { criteria: local, usedExternalAi: false };
    try {
      const criteria = await this.external.parse(text, categories, products, collections);
      return { criteria: this.validate(criteria, categories, collections), usedExternalAi: true };
    } catch {
      return { criteria: local, usedExternalAi: false };
    }
  }

  private validate(criteria: ProductSearchCriteria, categories: DemoCategory[], collections: DemoCollection[]): ProductSearchCriteria {
    const category = criteria.categoryId ? categories.find(x => x.categoryId === criteria.categoryId) : undefined;
    const collection = criteria.collectionId ? collections.find(x => x.collectionId === criteria.collectionId) : undefined;
    if (criteria.minPrice !== undefined && (!Number.isFinite(criteria.minPrice) || criteria.minPrice < 0) ||
      criteria.maxPrice !== undefined && (!Number.isFinite(criteria.maxPrice) || criteria.maxPrice < 0) ||
      criteria.minPrice !== undefined && criteria.maxPrice !== undefined && criteria.minPrice > criteria.maxPrice ||
      criteria.limit < 1 || criteria.limit > 20) throw new Error('Invalid structured commerce criteria.');
    return { ...criteria, categoryId: category?.categoryId, category: category?.categoryName, collectionId: collection?.collectionId, collection: collection?.name, limit: Math.min(criteria.limit, 20) };
  }
}
