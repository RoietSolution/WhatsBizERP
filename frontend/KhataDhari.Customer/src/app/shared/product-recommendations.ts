import { Product } from '../models/storefront.models';

export type ProductRecommendationHeading =
  | 'Similar Products'
  | 'More From This Category'
  | 'Explore More Products';

export interface ProductRecommendationSet {
  heading: ProductRecommendationHeading;
  products: Product[];
}

const MAX_RECOMMENDATIONS = 8;
const IGNORED_NAME_WORDS = new Set([
  'assorted', 'best', 'combo', 'for', 'new', 'pack', 'piece', 'pieces',
  'premium', 'product', 'quality', 'set', 'size', 'the', 'with'
]);
const MEANINGFUL_SPECIFICATIONS = new Set([
  'brand', 'manufacturer', 'material', 'model', 'producttype', 'type'
]);

/**
 * Chooses one recommendation tier at a time. Price can order close matches,
 * but never makes a product similar by itself.
 */
export function selectProductRecommendations(
  current: Product,
  categoryProducts: readonly Product[],
  storeProducts: readonly Product[]
): ProductRecommendationSet | null {
  const category = uniqueCandidates(current, categoryProducts).sort(catalogueOrder);
  const similar = category
    .map(product => ({ product, score: similarityScore(current, product) }))
    .filter((entry): entry is { product: Product; score: SimilarityScore } => entry.score !== null)
    .sort((left, right) => compareSimilarProducts(left, right, current))
    .slice(0, MAX_RECOMMENDATIONS)
    .map(entry => entry.product);

  if (similar.length) return { heading: 'Similar Products', products: similar };
  if (category.length) return { heading: 'More From This Category', products: category.slice(0, MAX_RECOMMENDATIONS) };

  const explore = uniqueCandidates(current, storeProducts).sort(catalogueOrder).slice(0, MAX_RECOMMENDATIONS);
  return explore.length ? { heading: 'Explore More Products', products: explore } : null;
}

function uniqueCandidates(current: Product, products: readonly Product[]): Product[] {
  const seen = new Set<string>([current.id]);
  return products.filter(product => {
    if (!product.id || seen.has(product.id)) return false;
    seen.add(product.id);
    return true;
  });
}

function catalogueOrder(left: Product, right: Product): number {
  return left.name.localeCompare(right.name) || left.id.localeCompare(right.id);
}

interface SimilarityScore {
  sharedNameWords: number;
  sharedSpecificationValues: number;
}

function similarityScore(current: Product, candidate: Product): SimilarityScore | null {
  const currentWords = meaningfulWords(current.name);
  const candidateWords = meaningfulWords(candidate.name);
  const sharedNameWords = [...currentWords].filter(word => candidateWords.has(word)).length;
  const sharedSpecificationValues = matchingMeaningfulSpecifications(current, candidate);
  if (sharedNameWords === 0 && sharedSpecificationValues === 0) return null;
  return { sharedNameWords, sharedSpecificationValues };
}

function meaningfulWords(value: string): Set<string> {
  return new Set(value.normalize('NFKD').toLocaleLowerCase().match(/[a-z0-9]+/g)
    ?.filter(word => word.length >= 4 && !IGNORED_NAME_WORDS.has(word)) ?? []);
}

function matchingMeaningfulSpecifications(current: Product, candidate: Product): number {
  const values = new Set((current.specifications ?? [])
    .filter(spec => MEANINGFUL_SPECIFICATIONS.has(normalize(spec.name)) && spec.value.trim())
    .map(spec => `${normalize(spec.name)}:${normalize(spec.value)}`));
  return (candidate.specifications ?? []).filter(spec =>
    MEANINGFUL_SPECIFICATIONS.has(normalize(spec.name)) && values.has(`${normalize(spec.name)}:${normalize(spec.value)}`)
  ).length;
}

function normalize(value: string): string {
  return value.normalize('NFKD').toLocaleLowerCase().replace(/[^a-z0-9]/g, '');
}

function compareSimilarProducts(
  left: { product: Product; score: SimilarityScore },
  right: { product: Product; score: SimilarityScore },
  current: Product
): number {
  return right.score.sharedNameWords - left.score.sharedNameWords
    || right.score.sharedSpecificationValues - left.score.sharedSpecificationValues
    || priceDistance(current.sellingPrice, left.product.sellingPrice) - priceDistance(current.sellingPrice, right.product.sellingPrice)
    || left.product.name.localeCompare(right.product.name)
    || left.product.id.localeCompare(right.product.id);
}

function priceDistance(currentPrice: number, candidatePrice: number): number {
  if (currentPrice <= 0 || candidatePrice <= 0) return Number.POSITIVE_INFINITY;
  return Math.abs(Math.log(candidatePrice / currentPrice));
}
