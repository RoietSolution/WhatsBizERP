import { DefaultUrlSerializer, UrlMatcher, UrlSegment, UrlSerializer, UrlTree } from '@angular/router';

let resolvedStoreKey: string | null = null;

export function setCustomStoreDomain(storeKey: string | null): void {
  resolvedStoreKey = storeKey?.trim().toLowerCase() || null;
}

export function getCustomStoreDomain(): string | null { return resolvedStoreKey; }

export function customStoreDomainMatcher(segments: UrlSegment[]) {
  if (!resolvedStoreKey) return null;
  const consumesKeyPrefix = segments[0]?.path.toLowerCase() === resolvedStoreKey;
  return {
    consumed: consumesKeyPrefix ? [segments[0]] : [],
    posParams: { storeKey: new UrlSegment(resolvedStoreKey, {}) },
  };
}

/** Keeps existing components' /{storeKey}/ links working while presenting prefixless URLs on mapped domains. */
export class StorefrontUrlSerializer implements UrlSerializer {
  private readonly defaultSerializer = new DefaultUrlSerializer();

  parse(url: string): UrlTree {
    return this.defaultSerializer.parse(this.stripStorePrefix(url));
  }

  serialize(tree: UrlTree): string {
    return this.stripStorePrefix(this.defaultSerializer.serialize(tree));
  }

  private stripStorePrefix(url: string): string {
    if (!resolvedStoreKey) return url;
    const prefix = `/${encodeURIComponent(resolvedStoreKey)}`;
    if (url === prefix) return '/';
    if (!url.startsWith(`${prefix}/`) && !url.startsWith(`${prefix}?`) && !url.startsWith(`${prefix}#`)) return url;
    const remainder = url.slice(prefix.length);
    return remainder.startsWith('?') || remainder.startsWith('#') ? `/${remainder}` : remainder || '/';
  }
}
