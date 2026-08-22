import { DemoCategory, DemoCollection, DemoProduct } from './whatsapp-commerce-demo-api.service';
import { LocalCommerceIntentEngine } from './local-commerce-intent-engine';

describe('LocalCommerceIntentEngine', () => {
  const engine = new LocalCommerceIntentEngine();
  const categories: DemoCategory[] = [
    { categoryId: 'shirts', categoryName: 'T-Shirts', productCount: 1 },
    { categoryId: 'sarees', categoryName: 'Sarees', productCount: 2 },
    { categoryId: 'books', categoryName: 'Stationery', productCount: 1 },
  ];
  const products: DemoProduct[] = [
    product('tee-1', 'Red T-Shirt', 450, 'shirts', 'Red cotton T-Shirt'),
    product('saree-1', 'Red Silk Saree', 1400, 'sarees', 'Red silk Saree'),
    product('saree-2', 'Blue Saree', 1900, 'sarees', 'Blue Saree'),
    product('book-1', 'Notebook', 120, 'books', 'Ruled notebook'),
  ];
  const collections: DemoCollection[] = [
    { collectionId: 'wedding', name: 'Wedding Collection', slug: 'wedding-collection', productIds: ['saree-1', 'tee-1'] },
    { collectionId: 'wedding-sarees', name: 'Wedding Sarees', slug: 'wedding-sarees', productIds: ['saree-1'] },
  ];

  it('requires product/category relevance before applying price', () => {
    const result = search('Show me T-Shirts under ₹500');
    expect(result.products.map(x => x.productId)).toEqual(['tee-1']);
    expect(result.products.every(x => x.sellingPrice <= 500)).toBeTrue();
  });

  it('does not substitute unrelated products when there is no match', () => {
    const result = search('Show T-Shirts under ₹10');
    expect(result.products).toEqual([]);
  });

  it('parses English, Hindi, and Hinglish product search', () => {
    expect(engine.parse('Show sarees under ₹1500', categories, products)).toEqual(jasmine.objectContaining({ category: 'Sarees', maxPrice: 1500, language: 'EN' }));
    expect(engine.parse('\u0031\u0035\u0030\u0030 \u0930\u0941\u092a\u092f\u0947 \u0915\u0947 \u0905\u0902\u0926\u0930 \u0938\u093e\u0921\u093c\u0940 \u0926\u093f\u0916\u093e\u0913', categories, products)).toEqual(jasmine.objectContaining({ category: 'Sarees', maxPrice: 1500, language: 'HI' }));
    expect(engine.parse('1500 ke andar red saree dikhao', categories, products)).toEqual(jasmine.objectContaining({ category: 'Sarees', colour: 'Red', maxPrice: 1500, language: 'HINGLISH' }));
  });

  it('parses multiple attributes and common price expressions', () => {
    const criteria = engine.parse('red silk saree under 2000', categories, products);
    expect(criteria).toEqual(jasmine.objectContaining({ colour: 'Red', material: 'Silk', maxPrice: 2000 }));
    expect(engine.parse('blue shirt size XL under 1200', categories, products)).toEqual(jasmine.objectContaining({ category: 'T-Shirts', colour: 'Blue', size: 'XL', maxPrice: 1200 }));
    for (const expression of ['under 500', '500 ke andar', '500 se kam', '₹500', 'Rs. 500']) {
      expect(engine.parse(`saree ${expression}`, categories, products).maxPrice).toBe(500);
    }
  });

  it('clarifies an attribute and price without inventing a category', () => {
    const result = search('red under 1000');
    expect(result.products).toEqual([]);
    expect(result.clarificationCategories.map(x => x.categoryName)).toEqual(['T-Shirts', 'Sarees', 'Stationery']);
  });

  it('keeps results within the supplied tenant catalogue', () => {
    const tenantB = [product('tenant-b', 'Red T-Shirt', 100, 'shirts', '')];
    const result = engine.search(engine.parse('red t-shirt under 200', categories, tenantB), tenantB, categories);
    expect(result.products.map(x => x.productId)).toEqual(['tenant-b']);
    expect(result.products).not.toContain(jasmine.objectContaining({ productId: 'tee-1' }));
  });

  it('resolves English, Hindi, and Hinglish collection requests', () => {
    expect(engine.parse('Show wedding collection', categories, products, [collections[0]])).toEqual(jasmine.objectContaining({ intent: 'COLLECTION_SEARCH', collectionId: 'wedding', language: 'EN' }));
    expect(engine.parse('वेडिंग कलेक्शन भेजो', categories, products, [collections[0]])).toEqual(jasmine.objectContaining({ intent: 'COLLECTION_SEARCH', collectionId: 'wedding', language: 'HI' }));
    expect(engine.parse('wedding collection bhejo', categories, products, [collections[0]])).toEqual(jasmine.objectContaining({ intent: 'COLLECTION_SEARCH', collectionId: 'wedding', language: 'HINGLISH' }));
  });

  it('keeps collection membership mandatory with product filters', () => {
    const criteria = engine.parse('red wedding saree under 2000', categories, products, [collections[0]]);
    const result = engine.search(criteria, products, categories, [collections[0]]);
    expect(result.products.map(x => x.productId)).toEqual(['saree-1']);
  });

  it('clarifies ambiguous collection names', () => {
    const criteria = engine.parse('wedding collection', categories, products, collections);
    const result = engine.search(criteria, products, categories, collections);
    expect(result.products).toEqual([]);
    expect(result.clarificationCollections.map(x => x.collectionId)).toEqual(['wedding', 'wedding-sarees']);
  });

  function search(text: string) {
    const criteria = engine.parse(text, categories, products);
    return engine.search(criteria, products, categories);
  }
});

function product(productId: string, productName: string, sellingPrice: number, categoryId: string, description: string): DemoProduct {
  return { productId, productCode: productId, productName, description, sellingPrice, mrp: sellingPrice, taxPercentage: 0, availableQuantity: 5, categoryId, categoryName: categoryId === 'shirts' ? 'T-Shirts' : categoryId === 'sarees' ? 'Sarees' : 'Stationery' };
}
