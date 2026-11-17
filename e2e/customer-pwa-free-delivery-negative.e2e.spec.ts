import { expect, test, type Page, type Route } from '@playwright/test';

const app = 'http://localhost:4300';
const api = 'http://localhost:5184';
const productId = 'synthetic-delivery-item';
const threshold = 500;

function quote(amount: number, remaining = Math.max(0, threshold - amount), progress = Math.min(100, Math.round(amount / threshold * 100))) {
  return {
    eligibleAmount: amount, freeDeliveryThreshold: threshold, remainingAmount: remaining, progressPercent: progress,
    isFreeDeliveryUnlocked: false, isDeliveryEnabled: true, isPincodeServiceable: true,
    merchandiseAmount: amount, merchandiseTaxAmount: 0, merchandiseSubtotal: amount,
    standardDeliveryCharge: 40, freeDeliveryEnabled: true, deliveryCharge: 40, promotionDiscount: 0,
    finalPayableAmount: amount + 40,
  };
}

type QuoteMode = 'valid' | 'error' | 'timeout' | 'network' | 'race';

async function installSyntheticApi(page: Page) {
  let quoteMode: QuoteMode = 'valid';
  let failProductRead = false;
  let heldOldQuote: (() => Promise<void>) | null = null;
  const quoteRequests: Array<{ tenant: string; quantity: number }> = [];
  const productReads: string[] = [];

  await page.route(`${api}/api/**`, async (route: Route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    const respond = (status: number, value: unknown) => route.fulfill({
      status, contentType: 'application/json', headers: { 'access-control-allow-origin': '*' }, body: JSON.stringify(value),
    });
    if (method === 'OPTIONS') return route.fulfill({ status: 204, headers: { 'access-control-allow-origin': '*', 'access-control-allow-methods': 'GET,POST,OPTIONS', 'access-control-allow-headers': '*' } });

    const publicRoute = path.match(/^\/api\/public\/routes\/([^/]+)$/);
    if (publicRoute) {
      const tenant = decodeURIComponent(publicRoute[1]).toLowerCase();
      return respond(200, { slug: tenant, type: 'RETAILER', storeKey: tenant, canonicalPath: `/${tenant}` });
    }
    const storeRoute = path.match(/^\/api\/store\/([^/]+)(.*)$/);
    if (!storeRoute) return respond(404, { message: 'Synthetic route not configured.' });
    const tenant = decodeURIComponent(storeRoute[1]).toLowerCase();
    const tail = storeRoute[2];
    if (tail === '' && method === 'GET') return respond(200, {
      storeKey: tenant, name: tenant === 'retailer-a' ? 'Retailer A Synthetic' : 'Retailer B Synthetic',
      tagline: 'Synthetic test store', accentColor: '#145c43', freeDeliveryEnabled: true, freeDeliveryThreshold: threshold,
      paymentMethods: [], banners: [], featuredCategories: [], customerRewardsEnabled: false,
    });
    if (tail === '/categories' && method === 'GET') return respond(200, []);
    if (tail === '/availability-announcements' && method === 'GET') return respond(200, []);
    if (tail === '/products' && method === 'GET') return respond(200, {
      items: [{ id: productId, categoryId: 'synthetic-category', name: `Item for ${tenant}`, description: 'Synthetic item',
        imageUrl: '/images/product-placeholder.svg', sellingPrice: 100, availability: 'IN_STOCK', isPurchasable: true, canOrderNow: true }],
      pageNumber: 1, pageSize: 24, totalCount: 1, hasMore: false, minimumPrice: 100, maximumPrice: 100, supportsNewestSort: false,
    });
    if (tail === `/products/${productId}` && method === 'GET') {
      productReads.push(tenant);
      if (failProductRead) { failProductRead = false; return respond(500, { message: 'Synthetic product revalidation failure.' }); }
      return respond(200, { id: productId, categoryId: 'synthetic-category', name: `Item for ${tenant}`, description: 'Synthetic item',
        imageUrl: '/images/product-placeholder.svg', sellingPrice: 100, availability: 'IN_STOCK', isPurchasable: true, canOrderNow: true });
    }
    if ((tail === '/cart/quote' || tail === '/cart/preview') && method === 'POST') {
      const body = request.postDataJSON() as { items?: Array<{ productId: string; quantity: number }> };
      const quantity = body.items?.reduce((sum, item) => sum + item.quantity, 0) ?? 0;
      quoteRequests.push({ tenant, quantity });
      if (quoteMode === 'error') return respond(500, { message: 'Synthetic quote failure.' });
      if (quoteMode === 'timeout') return route.abort('timedout');
      if (quoteMode === 'network') return route.abort('internetdisconnected');
      if (quoteMode === 'race' && quantity === 6) {
        await new Promise<void>(resolve => { heldOldQuote = async () => { await respond(200, quote(400, 100, 80)); resolve(); }; });
        return;
      }
      if (quoteMode === 'race' && quantity === 7) return respond(200, quote(300, 200, 60));
      return respond(200, quote(quantity * 100));
    }
    if (tail === '/session' && method === 'GET') {
      const token = request.headers()['x-customer-session'] ?? '';
      const id = token.includes('customer-two') ? 'customer-two' : 'customer-one';
      return respond(200, { id, name: `Synthetic ${id}` });
    }
    if (tail === '/wishlist' && method === 'GET') return respond(200, []);
    if (tail === '/wishlist/merge' && method === 'POST') return route.fulfill({ status: 204, headers: { 'access-control-allow-origin': '*' } });
    if (tail === '/cart/quote' || tail === '/cart/preview') return respond(200, quote(0));
    return respond(404, { message: 'Synthetic route not configured.' });
  });

  return {
    quoteRequests, productReads,
    failNextProductRead: () => { failProductRead = true; },
    setQuoteMode: (mode: QuoteMode) => { quoteMode = mode; },
    releaseHeldQuote: async () => { const release = heldOldQuote; heldOldQuote = null; if (release) await release(); },
  };
}

async function seedTenant(page: Page, tenant: string, customerId = 'guest', quantity = 1): Promise<void> {
  await page.evaluate(({ tenant, customerId, quantity }) => {
    const cartKey = `khada-dhari-cart:${tenant}:${customerId}`.replace('khada-dhari', 'khata-dhari');
    const line = { quantity, product: { id: 'synthetic-delivery-item', categoryId: 'synthetic-category', name: `Item for ${tenant}`, description: 'Synthetic item', imageUrl: '/images/product-placeholder.svg', sellingPrice: 100, available: true } };
    localStorage.setItem(cartKey, JSON.stringify(quantity > 0 ? [line] : []));
    localStorage.setItem(`${cartKey}:revision`, '1');
    sessionStorage.setItem(`khata-dhari-storefront-serviceability:${tenant}:${customerId}`, JSON.stringify({
      storeKey: tenant, pincode: '560001', verified: true, verifiedAtUtc: Date.now(),
      quote: { isDeliveryEnabled: true, isPincodeServiceable: true },
    }));
    if (customerId !== 'guest') {
      localStorage.setItem(`khatadhari.customer-session.${tenant}`, `synthetic-${customerId}-token`);
      localStorage.setItem(`khatadhari.customer-id.${tenant}`, customerId);
    }
  }, { tenant, customerId, quantity });
}

function progress(page: Page) { return page.locator('shop-free-delivery-progress .delivery-progress.toast:not(.unlocked-toast)'); }
function cartKey(tenant: string, customerId: string) { return `khata-dhari-cart:${tenant}:${customerId}`; }
function dismissalKey(tenant: string, customerId: string) { return `khata-dhari-free-delivery-dismissal:${cartKey(tenant, customerId)}:revision`; }

test('multi-retailer and customer contexts keep free-delivery dismissal tenant-scoped through navigation history', async ({ page }) => {
  const mocked = await installSyntheticApi(page);
  await page.goto(`${app}/retailer-a`, { waitUntil: 'domcontentloaded' });
  await seedTenant(page, 'retailer-a', 'customer-one');
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(progress(page)).toBeVisible();
  await progress(page).getByRole('button', { name: 'Dismiss free delivery progress' }).click();
  await expect(progress(page)).toHaveCount(0);

  await page.goto(`${app}/retailer-b`, { waitUntil: 'domcontentloaded' });
  await seedTenant(page, 'retailer-b', 'customer-one', 2);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(progress(page)).toBeVisible();
  await progress(page).getByRole('button', { name: 'Dismiss free delivery progress' }).click();
  await expect(progress(page)).toHaveCount(0);
  await expect(page.locator('.cart-badge')).toHaveAttribute('aria-label', '2 items in cart');
  expect(await page.evaluate(key => localStorage.getItem(key), dismissalKey('retailer-b', 'customer-one'))).toBe('1');

  await page.goBack();
  await expect(page.locator('.brand-copy strong')).toContainText('Retailer A Synthetic');
  await expect(page.locator('.cart-badge')).toHaveAttribute('aria-label', '1 items in cart');
  const retailerACard = page.locator('shop-product-card').filter({ hasText: 'Item for retailer-a' });
  await retailerACard.getByRole('button', { name: /In cart \(1\)/ }).click();
  await expect(page.locator('.cart-badge')).toHaveAttribute('aria-label', '2 items in cart');
  await expect(progress(page)).toBeVisible();
  await page.goForward();
  await expect(page.locator('.brand-copy strong')).toContainText('Retailer B Synthetic');
  await expect(progress(page)).toHaveCount(0);
  await expect(page.locator('.cart-badge')).toHaveAttribute('aria-label', '2 items in cart');

  // A different customer has a separate cart, verified location and dismissal identity.
  await page.evaluate(() => {
    localStorage.setItem('khatadhari.customer-session.retailer-a', 'synthetic-customer-two-token');
    localStorage.setItem('khatadhari.customer-id.retailer-a', 'customer-two');
    localStorage.setItem('khata-dhari-cart:retailer-a:customer-two', JSON.stringify([{ quantity: 1, product: { id: 'synthetic-delivery-item', categoryId: 'synthetic-category', name: 'Customer two item', description: '', imageUrl: '/images/product-placeholder.svg', sellingPrice: 100, available: true } }]));
    localStorage.setItem('khata-dhari-cart:retailer-a:customer-two:revision', '1');
    sessionStorage.setItem('khata-dhari-storefront-serviceability:retailer-a:customer-two', JSON.stringify({ storeKey: 'retailer-a', pincode: '560001', verified: true, verifiedAtUtc: Date.now(), quote: { isDeliveryEnabled: true, isPincodeServiceable: true } }));
  });
  await page.goto(`${app}/retailer-a`, { waitUntil: 'domcontentloaded' });
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(progress(page)).toBeVisible();
  expect(mocked.quoteRequests.some(request => request.tenant === 'retailer-b')).toBeTruthy();
});

test('failed add revalidation preserves cart revision, badge and dismissal; next successful add reopens progress', async ({ page }) => {
  const mocked = await installSyntheticApi(page);
  await page.goto(`${app}/retailer-a`, { waitUntil: 'domcontentloaded' });
  await seedTenant(page, 'retailer-a');
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(progress(page)).toBeVisible();
  await progress(page).getByRole('button', { name: 'Dismiss free delivery progress' }).click();
  const revisionKey = `${cartKey('retailer-a', 'guest')}:revision`;
  const before = await page.evaluate(key => Number(localStorage.getItem(key)), revisionKey);
  const badge = page.locator('.cart-badge');
  const beforeBadge = await badge.getAttribute('aria-label');

  mocked.failNextProductRead();
  const card = page.locator('shop-product-card').filter({ hasText: 'Item for retailer-a' });
  await card.getByRole('button', { name: /In cart \(1\)/ }).click();
  await expect(page.locator('.store-toast')).toContainText('Unable to refresh availability');
  await expect(card.getByRole('button', { name: /In cart \(1\)/ })).toBeVisible();
  expect(await page.evaluate(key => Number(localStorage.getItem(key)), revisionKey)).toBe(before);
  await expect(badge).toHaveAttribute('aria-label', beforeBadge!);
  await expect(progress(page)).toHaveCount(0);
  expect(mocked.quoteRequests).toHaveLength(1);

  await card.getByRole('button', { name: /In cart \(1\)/ }).click();
  await expect(card.getByRole('button', { name: /In cart \(2\)/ })).toBeVisible();
  await expect.poll(() => page.evaluate(key => Number(localStorage.getItem(key)), revisionKey)).toBe(before + 1);
  await expect(progress(page)).toBeVisible();
});

test('quote errors, interruption and stale responses clear old progress and only the newest quote is shown', async ({ page }) => {
  const mocked = await installSyntheticApi(page);
  await page.goto(`${app}/retailer-a`, { waitUntil: 'domcontentloaded' });
  await seedTenant(page, 'retailer-a');
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(progress(page)).toBeVisible();
  await progress(page).getByRole('button', { name: 'Dismiss free delivery progress' }).click();

  mocked.setQuoteMode('error');
  const card = page.locator('shop-product-card').filter({ hasText: 'Item for retailer-a' });
  await card.getByRole('button', { name: /In cart \(1\)/ }).click();
  await expect(card.getByRole('button', { name: /In cart \(2\)/ })).toBeVisible();
  await expect.poll(() => mocked.quoteRequests.length).toBe(2);
  await expect(progress(page)).toHaveCount(0);
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);

  mocked.setQuoteMode('valid');
  await card.getByRole('button', { name: /In cart \(2\)/ }).click();
  await expect(card.getByRole('button', { name: /In cart \(3\)/ })).toBeVisible();
  await expect(progress(page)).toBeVisible();

  mocked.setQuoteMode('timeout');
  await card.getByRole('button', { name: /In cart \(3\)/ }).click();
  await expect(card.getByRole('button', { name: /In cart \(4\)/ })).toBeVisible();
  await expect.poll(() => mocked.quoteRequests.length).toBe(4);
  await expect(progress(page)).toHaveCount(0);
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);

  mocked.setQuoteMode('network');
  await card.getByRole('button', { name: /In cart \(4\)/ }).click();
  await expect(card.getByRole('button', { name: /In cart \(5\)/ })).toBeVisible();
  await expect.poll(() => mocked.quoteRequests.length).toBe(5);
  await expect(progress(page)).toHaveCount(0);
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);

  mocked.setQuoteMode('race');
  await card.getByRole('button', { name: /In cart \(5\)/ }).click();
  await expect(card.getByRole('button', { name: /In cart \(6\)/ })).toBeVisible();
  await expect.poll(() => mocked.quoteRequests.some(request => request.quantity === 6)).toBeTruthy();
  await card.getByRole('button', { name: /In cart \(6\)/ }).click();
  await expect(card.getByRole('button', { name: /In cart \(7\)/ })).toBeVisible();
  await expect.poll(() => mocked.quoteRequests.some(request => request.quantity === 7)).toBeTruthy();
  await expect(progress(page).locator('[role="progressbar"]')).toHaveAttribute('aria-valuenow', '60');
  await mocked.releaseHeldQuote();
  await expect(progress(page).locator('[role="progressbar"]')).toHaveAttribute('aria-valuenow', '60');
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);
});
