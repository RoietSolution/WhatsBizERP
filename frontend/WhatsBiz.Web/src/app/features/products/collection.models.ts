import { PagedResult } from './product.models';
export interface CollectionListItem { collectionId: string; name: string; slug: string; description?: string; isActive: boolean; productCount: number; displayOrder: number; startDate?: string; endDate?: string; }
export interface CollectionProduct { productId: string; productCode: string; productName: string; categoryName: string; sellingPrice: number; imageUrl?: string; isActive: boolean; displayOrder: number; }
export interface CollectionDetail extends CollectionListItem { products: CollectionProduct[]; }
export interface CollectionInput { name: string; description?: string; isActive: boolean; displayOrder: number; startDate?: string; endDate?: string; }
export interface CollectionSendResult { succeeded: boolean; providerMessageId?: string; attemptedAt: string; nativeUsed: boolean; productsSent: number; recipient: string; safeMessage?: string; }
export type CollectionPage = PagedResult<CollectionListItem>;
