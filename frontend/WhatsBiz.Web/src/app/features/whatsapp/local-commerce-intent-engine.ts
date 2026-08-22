import { DemoCategory, DemoCollection, DemoProduct, DemoProductVariant } from './whatsapp-commerce-demo-api.service';

export type CommerceLanguage = 'EN' | 'HI' | 'HINGLISH';

export interface ProductSearchCriteria {
  intent: 'PRODUCT_SEARCH' | 'COLLECTION_SEARCH';
  searchText: string;
  productName?: string;
  category?: string;
  categoryId?: string;
  subCategory?: string;
  brand?: string;
  gender?: string;
  colour?: string;
  size?: string;
  material?: string;
  style?: string;
  occasion?: string;
  minPrice?: number;
  maxPrice?: number;
  inStockOnly: boolean;
  sort: 'price-asc';
  limit: number;
  language: CommerceLanguage;
  confidence: 'high' | 'medium' | 'low';
  confidenceScore: number;
  relevantTerms: string[];
  categoryConcepts: string[];
  collection?: string;
  collectionId?: string;
  collectionCandidates?: DemoCollection[];
}

export interface ProductSearchResult {
  criteria: ProductSearchCriteria;
  products: DemoProduct[];
  clarificationCategories: DemoCategory[];
  clarificationCollections: DemoCollection[];
  suggestions: DemoProduct[];
}

type Concept = 'tshirt' | 'shirt' | 'saree' | 'red' | 'blue' | 'silk' | 'men' | 'women';

const CONCEPT_ALIASES: Record<Concept, string[]> = {
  tshirt: ['tshirt', 't-shirt', 't shirt', 'tee', '\u091f\u0940\u0936\u0930\u094d\u091f'],
  shirt: ['shirt', 'shirts', '\u0936\u0930\u094d\u091f'],
  saree: ['saree', 'sarees', 'sari', 'saris', '\u0938\u093e\u095c\u0940', '\u0938\u093e\u0921\u093c\u0940'],
  red: ['red', 'lal', 'laal', 'red colour', '\u0932\u093e\u0932'],
  blue: ['blue', 'nila', 'neela', 'blue colour', '\u0928\u0940\u0932\u093e'],
  silk: ['silk', 'resham', '\u0938\u093f\u0932\u094d\u0915', '\u0930\u0947\u0936\u092e'],
  men: ['men', "men's", 'mens', 'man', '\u092a\u0941\u0930\u0941\u0937'],
  women: ['women', "women's", 'womens', 'woman', '\u092e\u0939\u093f\u0932\u093e'],
};

const STOP_WORDS = new Set([
  'show', 'me', 'the', 'a', 'an', 'some', 'find', 'want', 'need', 'please', 'product', 'products',
  'item', 'items', 'under', 'below', 'less', 'than', 'up', 'to', 'within', 'between', 'above',
  'over', 'more', 'and', 'or', 'of', 'for', 'in', 'on', 'at', 'is', 'are', 'i', 'would', 'like',
  'dikhao', 'dikhana', 'batao', 'chahiye', 'mujhe', 'ke', 'ka', 'ki', 'se', 'kam', 'zyada', 'jyada',
  'tak', 'andar', 'rupees', 'rupee', 'rs', 'inr', 'colour', 'color',
  '\u092e\u0941\u091d\u0947', '\u0926\u093f\u0916\u093e\u0913', '\u091a\u093e\u0939\u093f\u090f',
  '\u0915\u0947', '\u0915\u093e', '\u0915\u0940', '\u0938\u0947', '\u0915\u092e', '\u091c\u094d\u092f\u093e\u0926\u093e',
  '\u0924\u0915', '\u0915\u0947', '\u0905\u0902\u0926\u0930', '\u0914\u0930', '\u0938\u093f\u0932\u094d\u0915',
]);

const HINGLISH_WORDS = /\b(dikhao|dikhana|chahiye|batao|mujhe|andar|tak|kam|zyada|jyada|saree|sari|shirt|tshirt)\b/i;
const DEVANAGARI = /[\u0900-\u097f]/;

export class LocalCommerceIntentEngine {
  parse(text: string, categories: DemoCategory[], products: DemoProduct[], collections: DemoCollection[] = []): ProductSearchCriteria {
    const searchText = text.trim();
    const normalized = normalize(searchText);
    const language = this.detectLanguage(searchText);
    const price = parsePrice(normalized);
    const concepts = this.concepts(normalized);
    const size = parseSize(normalized);
    const categoryMatch = this.resolveCategory(normalized, concepts, categories, products);
    const productMatch = this.resolveProduct(normalized, concepts, products);
    const collectionMatches = this.resolveCollections(normalized, collections);
    const relevantTerms = this.relevantTerms(normalized, concepts, price);
    const attribute = (concept: Concept) => concepts.has(concept) ? concept : undefined;
    const confidenceScore = this.scoreConfidence(!!categoryMatch, !!productMatch, price.min !== undefined || price.max !== undefined,
      [attribute('red'), attribute('blue'), attribute('silk'), attribute('men'), attribute('women'), size].filter(Boolean).length, collectionMatches.length, relevantTerms.length);
    const confidence = confidenceScore >= 0.8 ? 'high' : confidenceScore >= 0.5 ? 'medium' : 'low';

    return {
      intent: collectionMatches.length ? 'COLLECTION_SEARCH' : 'PRODUCT_SEARCH', searchText, category: categoryMatch?.categoryName,
      categoryId: categoryMatch?.categoryId, productName: productMatch?.productName,
      colour: attribute('red') ? 'Red' : attribute('blue') ? 'Blue' : undefined,
      material: attribute('silk') ? 'Silk' : undefined,
      gender: attribute('men') ? 'Men' : attribute('women') ? 'Women' : undefined,
      size,
      minPrice: price.min, maxPrice: price.max, inStockOnly: true, sort: 'price-asc', limit: 5,
      language, confidence, confidenceScore, relevantTerms,
      categoryConcepts: [...concepts].filter(concept => concept === 'tshirt' || concept === 'shirt' || concept === 'saree'),
      collection: collectionMatches.length === 1 ? collectionMatches[0].name : undefined,
      collectionId: collectionMatches.length === 1 ? collectionMatches[0].collectionId : undefined,
      collectionCandidates: collectionMatches,
    };
  }

  search(criteria: ProductSearchCriteria, products: DemoProduct[], categories: DemoCategory[], collections: DemoCollection[] = []): ProductSearchResult {
    const category = criteria.categoryId ? categories.find(x => x.categoryId === criteria.categoryId) : undefined;
    const filtered = products
      .filter(product => !criteria.inStockOnly || (this.matchingVariant(product, criteria)?.availableQuantity ?? product.availableQuantity) > 0)
      .filter(product => !criteria.collectionId || (collections.find(x => x.collectionId === criteria.collectionId)?.productIds ?? []).includes(product.productId))
      .filter(product => !criteria.categoryId || product.categoryId === criteria.categoryId)
      .filter(product => !criteria.productName || contains(normalize(product.productName), normalize(criteria.productName)))
      .filter(product => { const variant = this.matchingVariant(product, criteria); return criteria.minPrice === undefined || (variant?.sellingPrice ?? product.sellingPrice) >= criteria.minPrice; })
      .filter(product => { const variant = this.matchingVariant(product, criteria); return criteria.maxPrice === undefined || (variant?.sellingPrice ?? product.sellingPrice) <= criteria.maxPrice; })
      .filter(product => this.matchesAttributes(product, criteria))
      .filter(product => this.matchesCategoryConcepts(product, criteria))
      .filter(product => this.isRelevant(product, criteria, !!category))
      .sort((a, b) => this.score(b, criteria) - this.score(a, criteria) || a.sellingPrice - b.sellingPrice)
      .slice(0, criteria.limit);

    const clarificationCategories = !criteria.collectionId && !criteria.categoryId && !criteria.productName && criteria.confidence !== 'high'
      ? categories.filter(x => x.productCount > 0 && products.some(p => p.categoryId === x.categoryId && p.availableQuantity > 0 && this.matchesAttributes(p, criteria))).slice(0, 5)
      : [];
    const clarificationCollections = criteria.collectionCandidates && criteria.collectionCandidates.length > 1 ? criteria.collectionCandidates : [];
    const suggestions = !filtered.length && !clarificationCategories.length && !clarificationCollections.length
      ? products.filter(p => p.availableQuantity > 0 && (!criteria.categoryId || p.categoryId === criteria.categoryId) && this.matchesAttributes(p, { ...criteria, minPrice: undefined, maxPrice: undefined })).sort((a,b) => a.sellingPrice - b.sellingPrice).slice(0, 3)
      : [];
    return { criteria, products: filtered, clarificationCategories, clarificationCollections, suggestions };
  }

  private scoreConfidence(category:boolean, product:boolean, hasPrice:boolean, attributes:number, collections:number, terms:number): number {
    if (collections > 1) return 0.35;
    let score = 0.2;
    if (category || product) score += 0.55;
    if (hasPrice) score += 0.1;
    if (attributes > 0) score += 0.1;
    if (terms > 0) score += 0.05;
    return Math.min(score, 1);
  }

  private matchingVariant(product: DemoProduct, criteria: ProductSearchCriteria): DemoProductVariant | undefined {
    if (!product.variants?.length) return undefined;
    const requiresVariant = !!criteria.colour || !!criteria.size || !!criteria.material || criteria.minPrice !== undefined || criteria.maxPrice !== undefined;
    if (!requiresVariant) return product.variants.find(x => x.availableQuantity > 0) ?? product.variants[0];
    return product.variants.find(variant =>
      (!criteria.colour || this.hasAlias(normalize(variant.colour ?? ''), criteria.colour)) &&
      (!criteria.size || normalize(variant.size ?? '') === normalize(criteria.size)) &&
      (!criteria.material || this.hasAlias(normalize(variant.material ?? ''), criteria.material)) &&
      (!criteria.inStockOnly || variant.availableQuantity > 0) &&
      (criteria.minPrice === undefined || variant.sellingPrice >= criteria.minPrice) &&
      (criteria.maxPrice === undefined || variant.sellingPrice <= criteria.maxPrice));
  }

  private resolveCollections(normalized: string, collections: DemoCollection[]): DemoCollection[] {
    return collections.filter(collection => {
      const name = normalizeCollectionText(collection.name);
      const slug = normalizeCollectionText(collection.slug);
      const query = normalizeCollectionText(normalized);
      return contains(query, name) || contains(query, slug) || name.split(/\s+/).filter(x => x.length > 2 && x !== 'collection').some(x => contains(query, x));
    });
  }

  private detectLanguage(text: string): CommerceLanguage {
    if (DEVANAGARI.test(text)) return 'HI';
    return HINGLISH_WORDS.test(text) ? 'HINGLISH' : 'EN';
  }

  private concepts(normalized: string): Set<Concept> {
    return new Set((Object.keys(CONCEPT_ALIASES) as Concept[]).filter(concept =>
      CONCEPT_ALIASES[concept].some(alias => contains(normalized, normalize(alias)))));
  }

  private resolveCategory(normalized: string, concepts: Set<Concept>, categories: DemoCategory[], products: DemoProduct[]) {
    for (const category of categories) {
      const categoryText = normalize(category.categoryName);
      const conceptMatch = [...concepts].some(concept =>
        CONCEPT_ALIASES[concept].some(alias => contains(categoryText, normalize(alias))) &&
        CONCEPT_ALIASES[concept].some(alias => contains(normalized, normalize(alias))));
      if (conceptMatch || contains(normalized, categoryText)) return category;
    }
    for (const concept of concepts) {
      const productCategoryIds = products
        .filter(product => CONCEPT_ALIASES[concept].some(alias => contains(normalize(product.productName), normalize(alias))))
        .map(product => product.categoryId);
      const category = categories.find(item => productCategoryIds.includes(item.categoryId));
      if (category) return category;
    }
    return undefined;
  }

  private resolveProduct(normalized: string, concepts: Set<Concept>, products: DemoProduct[]) {
    const terms = normalized.split(/\s+/).filter(x => x.length > 1 && !STOP_WORDS.has(x) && !/^\d/.test(x));
    if (!terms.length || [...concepts].some(x => x === 'tshirt' || x === 'shirt' || x === 'saree')) return undefined;
    return products.find(product => terms.some(term => contains(normalize(product.productName), term)));
  }

  private relevantTerms(normalized: string, concepts: Set<Concept>, price: PriceRange): string[] {
    const withoutPrice = normalized.replace(/\d+(?:\.\d+)?/g, ' ');
    return withoutPrice.split(/\s+/).filter(x => x.length > 1 && !STOP_WORDS.has(x) &&
      ![...concepts].some(concept => CONCEPT_ALIASES[concept].some(alias => normalize(alias) === x)) &&
      !price.consumedTokens.includes(x));
  }

  private matchesAttributes(product: DemoProduct, criteria: ProductSearchCriteria): boolean {
    if (product.variants?.length && (criteria.colour || criteria.size || criteria.material || criteria.minPrice !== undefined || criteria.maxPrice !== undefined)) return !!this.matchingVariant(product, criteria);
    const haystack = normalize(`${product.productName} ${product.description ?? ''} ${product.categoryName}`);
    return (!criteria.colour || this.hasAlias(haystack, criteria.colour)) &&
      (!criteria.material || this.hasAlias(haystack, criteria.material)) &&
      (!criteria.gender || this.hasAlias(haystack, criteria.gender)) &&
      (!criteria.size || contains(haystack, normalize(criteria.size)));
  }

  private matchesCategoryConcepts(product: DemoProduct, criteria: ProductSearchCriteria): boolean {
    if (!criteria.categoryConcepts.length) return true;
    const text = normalize(`${product.productName} ${product.categoryName}`);
    return criteria.categoryConcepts.some(concept =>
      CONCEPT_ALIASES[concept as Concept].some(alias => contains(text, normalize(alias))));
  }

  private hasAlias(text: string, value: string): boolean {
    const concept = (Object.keys(CONCEPT_ALIASES) as Concept[]).find(x =>
      x === value.toLowerCase() || CONCEPT_ALIASES[x].some(alias => normalize(alias) === normalize(value)));
    return concept ? CONCEPT_ALIASES[concept].some(alias => contains(text, normalize(alias))) : contains(text, normalize(value));
  }

  private isRelevant(product: DemoProduct, criteria: ProductSearchCriteria, categoryResolved: boolean): boolean {
    if (criteria.categoryId || categoryResolved) return true;
    if (criteria.productName) return contains(normalize(product.productName), normalize(criteria.productName));
    if (!criteria.relevantTerms.length) return false;
    const text = normalize(`${product.productName} ${product.categoryName}`);
    return criteria.relevantTerms.every(term => contains(text, term));
  }

  private score(product: DemoProduct, criteria: ProductSearchCriteria): number {
    const text = normalize(`${product.productName} ${product.categoryName}`);
    return criteria.relevantTerms.reduce((score, term) => score + (contains(text, term) ? 2 : 0), 0) +
      (criteria.categoryId === product.categoryId ? 10 : 0) + (product.availableQuantity > 0 ? 1 : 0);
  }
}

interface PriceRange { min?: number; max?: number; consumedTokens: string[]; }

function parsePrice(text: string): PriceRange {
  const currency = text.match(/(?:₹|rs\.?|inr)\s*(\d+(?:\.\d+)?)/i);
  const range = text.match(/(\d+(?:\.\d+)?)\s*(?:and|to|se|से)\s*(\d+(?:\.\d+)?)/i);
  if (range) return { min: Number(range[1]), max: Number(range[2]), consumedTokens: range.slice(1) };
  const amount = currency ? Number(currency[1]) : text.match(/(\d+(?:\.\d+)?)\s*(?:rupees?|रुपये|रुपए)/i)?.[1];
  const standalone = amount ? Number(amount) : undefined;
  const lower = /under|below|less than|up to|upto|within|ke andar|andar|se kam|तक|के अंदर|से कम/i.test(text);
  const upper = /above|over|more than|greater than|se zyada|se jyada|से ज्यादा|से अधिक/i.test(text);
  if (standalone !== undefined || (lower || upper) && /\d/.test(text)) {
    const value = standalone ?? Number(text.match(/\d+(?:\.\d+)?/)?.[0]);
    return { min: upper ? value : undefined, max: lower ? value : undefined, consumedTokens: [String(value)] };
  }
  return { consumedTokens: [] };
}

function parseSize(text: string): string | undefined {
  return text.match(/\bsize\s*(xs|s|m|l|xl|xxl|\d{2})\b/i)?.[1].toUpperCase() ??
    text.match(/\b(xxl|xl|xs)\b/i)?.[1].toUpperCase();
}

function normalize(value: string): string {
  return value.normalize('NFKC').toLocaleLowerCase('en-IN').replace(/[.,!?;:()\[\]{}]/g, ' ').replace(/\s+/g, ' ').trim();
}

function normalizeCollectionText(value: string): string {
  return normalize(value).replace(/वेडिंग/g, 'wedding').replace(/कलेक्शन/g, 'collection').replace(/दिवाली/g, 'diwali');
}

function contains(text: string, value: string): boolean {
  return text === value || text.includes(` ${value} `) || text.startsWith(`${value} `) || text.endsWith(` ${value}`) || text.includes(value);
}
