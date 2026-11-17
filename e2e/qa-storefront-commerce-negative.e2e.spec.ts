import { expect, test, type Page, type Route } from '@playwright/test';

const apiOrigin = 'http://localhost:5184';
const customerId = 'synthetic-customer';
const productId = 'synthetic-product';

function product(tenant = 'guturgo', active = true) {
  return {
    id: productId, categoryId: 'synthetic-category', name: 'Synthetic Saree', description: 'Synthetic test item',
    imageUrl: `/api/store/${tenant}/products/${productId}/image`, sellingPrice: 285, availability: active ? 'IN_STOCK' : 'INACTIVE',
    isPurchasable: active, canOrderNow: active, packSize: '1 item',
  };
}

function quote(items: Array<{ productId: string; quantity: number }>, pincode: string | null, promoCode: string | null, serviceable = true) {
  const amount = items.reduce((sum, item) => sum + item.quantity * 285, 0);
  const unlocked = amount >= 499;
  const discount = promoCode === 'SAVE10' ? 10 : 0;
  const charge = unlocked ? 0 : 40;
  return {
    eligibleAmount: amount, freeDeliveryThreshold: 499, remainingAmount: Math.max(0, 499 - amount),
    progressPercent: Math.min(100, Math.round(amount / 499 * 100)), isFreeDeliveryUnlocked: unlocked,
    isDeliveryEnabled: true, isPincodeServiceable: pincode ? serviceable : false,
    merchandiseAmount: amount, merchandiseTaxAmount: 0, merchandiseSubtotal: amount,
    standardDeliveryCharge: 40, freeDeliveryEnabled: true, deliveryCharge: charge,
    promotionDiscount: discount, promotionId: discount ? 'synthetic-promo' : undefined,
    promotionCode: discount ? promoCode : undefined, promotionName: discount ? 'Synthetic offer' : undefined,
    finalPayableAmount: amount + charge - discount,
  };
}

async function mockApi(page: Page, options: { quoteStatus?: number; serviceable?: boolean; checkoutStatus?: number; checkoutDelayMs?: number; sessionDelayMs?: number; inactive?: boolean; imageFallback?: boolean } = {}) {
  const imageRequests: string[] = [];
  const sessionRequests: string[] = [];
  let checkoutRequests = 0;
  let deliveryMode: 'eligible' | 'ineligible' | 'failed' = options.serviceable === false ? 'ineligible' : 'eligible';
  await page.route(`${apiOrigin}/api/**`, async (route: Route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const path = url.pathname;
    const respond = (status: number, value: unknown, headers: Record<string, string> = {}) => route.fulfill({
      status, contentType: 'application/json', headers: { 'access-control-allow-origin': '*', ...headers }, body: JSON.stringify(value),
    });

    if (method === 'OPTIONS') return route.fulfill({ status: 204, headers: { 'access-control-allow-origin': '*', 'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS', 'access-control-allow-headers': '*' } });
    if (path === '/api/public/routes/guturgo' || path === '/api/public/routes/other-retailer') {
      const key = path.endsWith('other-retailer') ? 'other-retailer' : 'guturgo';
      return respond(200, { slug: key, type: 'RETAILER', storeKey: key, canonicalPath: `/${key}` });
    }

    const storeMatch = path.match(/^\/api\/store\/([^/]+)(.*)$/);
    if (!storeMatch) return respond(404, { message: 'Not found.' });
    const key = decodeURIComponent(storeMatch[1]);
    const tail = storeMatch[2];
    if (tail === '' && method === 'GET') return respond(200, {
      storeKey: key, name: key === 'guturgo' ? 'GuturGo' : 'Other Synthetic Retailer', tagline: 'Synthetic local test store',
      accentColor: '#145c43', freeDeliveryThreshold: 499, paymentMethods: [{ code: 'COD', provider: 'COD', label: 'Cash on Delivery', description: 'Pay on delivery', isDefault: true, usesHostedPaymentPage: false }],
      banners: [], featuredCategories: [], customerRewardsEnabled: false,
    });
    if (tail === '/categories' && method === 'GET') return respond(200, [{ id: 'synthetic-category', name: 'Synthetic Category' }]);
    if (tail === '/availability-announcements' && method === 'GET') return respond(200, []);
    if (tail === '/products' && method === 'GET') {
      const item = product(key, !options.inactive);
      return respond(200, { items: [item], pageNumber: Number(url.searchParams.get('page') ?? 1), pageSize: 24, totalCount: 1, hasMore: false, minimumPrice: 285, maximumPrice: 285, supportsNewestSort: false });
    }
    if (tail === `/products/${productId}` && method === 'GET') return respond(options.inactive ? 404 : 200, options.inactive ? { message: 'Product unavailable.' } : product(key));
    if (tail === `/products/${productId}/image` && method === 'GET') {
      imageRequests.push(url.searchParams.toString());
      const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/c9sAAAAASUVORK5CYII=', 'base64');
      if (url.searchParams.get('thumbnail') === 'true' && options.imageFallback) return route.fulfill({ status: 404, contentType: 'application/json', body: '{"message":"Thumbnail unavailable."}' });
      return route.fulfill({ status: 200, contentType: 'image/png', headers: { 'access-control-allow-origin': '*' }, body: png });
    }
    if (tail === '/session' && method === 'GET') {
      sessionRequests.push(key);
      if (options.sessionDelayMs && key === 'guturgo') await new Promise(resolve => setTimeout(resolve, options.sessionDelayMs));
      return respond(200, { id: customerId, name: 'Synthetic Customer', mobile: '9876543210' });
    }
    if (tail === '/wishlist/merge' && method === 'POST') return route.fulfill({ status: 204, headers: { 'access-control-allow-origin': '*' } });
    if (tail === '/wishlist' && method === 'GET') return respond(200, key === 'guturgo' ? [{ id: productId, categoryId: 'synthetic-category', name: 'Synthetic Saree', imageUrl: `/api/store/${key}/products/${productId}/image`, sellingPrice: 285, availability: 'IN_STOCK' }] : []);
    if (tail === '/addresses' && method === 'GET') return respond(200, []);
    if (tail === '/cart/preview' || tail === '/cart/quote') {
      if (options.quoteStatus || deliveryMode === 'failed') return respond(options.quoteStatus ?? 500, { message: 'Pricing service unavailable.' });
      const body = request.postDataJSON() as { items?: Array<{ productId: string; quantity: number }>; pincode?: string | null; promoCode?: string | null };
      const code = body.promoCode?.toUpperCase() ?? null;
      const amount = (body.items ?? []).reduce((sum, item) => sum + item.quantity * 285, 0);
      if (code && code !== 'SAVE10') {
        const messages: Record<string, string> = {
          EXPIRED: 'Offer code has expired.', MINIMUM: 'Offer code requires a higher minimum purchase.', CATEGORY: 'Offer code is not applicable to these products.', INVALID: 'Offer code is not valid.',
        };
        return respond(400, { message: messages[code] ?? 'Offer code is not valid.' });
      }
      if (code === 'SAVE10' && amount < 500) return respond(400, { message: 'Offer code requires a higher minimum purchase.' });
      return respond(200, quote(body.items ?? [], body.pincode ?? null, code, deliveryMode !== 'ineligible'));
    }
    if (tail === '/delivery/availability' && method === 'GET') return respond(200, { deliveryEnabled: true, isPincodeServiceable: options.serviceable !== false });
    if (tail === '/checkout' && method === 'POST') {
      checkoutRequests++;
      if (options.checkoutDelayMs) await new Promise(resolve => setTimeout(resolve, options.checkoutDelayMs));
      return respond(options.checkoutStatus ?? 503, { message: 'Synthetic checkout provider unavailable.' });
    }
    if (tail.startsWith('/orders/') && method === 'GET') return respond(404, { message: 'Order not found.' });
    return respond(404, { message: 'Synthetic route not configured.' });
  });
  return { imageRequests, sessionRequests, checkoutCount: () => checkoutRequests, setDeliveryMode: (mode: 'eligible' | 'ineligible' | 'failed') => { deliveryMode = mode; } };
}

async function seedCustomerCart(page: Page, quantity = 1): Promise<void> {
  await page.addInitScript(({ key, id, qty, api }) => {
    localStorage.setItem(`khatadhari.customer-session.${key}`, 'synthetic-session-token');
    localStorage.setItem(`khatadhari.customer-id.${key}`, 'synthetic-customer');
    localStorage.setItem(`khata-dhari-cart:${key}:synthetic-customer`, JSON.stringify([{ quantity: qty, product: {
      id, categoryId: 'synthetic-category', name: 'Synthetic Saree', description: 'Synthetic test item',
      imageUrl: `${api}/api/store/${key}/products/${id}/image`, sellingPrice: 285, available: true, packSize: '1 item',
    } }]));
  }, { key: 'guturgo', id: productId, qty: quantity, api: apiOrigin });
}

async function seedVerifiedGuestDelivery(page: Page, quantity: number): Promise<void> {
  await page.addInitScript(({ api, id, qty }) => {
    localStorage.setItem('khata-dhari-cart:guturgo:guest', JSON.stringify([{ quantity: qty, product: {
      id, categoryId: 'synthetic-category', name: 'Synthetic Saree', description: 'Synthetic test item',
      imageUrl: `${api}/api/store/guturgo/products/${id}/image`, sellingPrice: 285, available: true,
    } }]));
    sessionStorage.setItem('khata-dhari-storefront-serviceability:guturgo:guest', JSON.stringify({
      storeKey: 'guturgo', pincode: '560001', verified: true, verifiedAtUtc: Date.now(),
      quote: { isDeliveryEnabled: true, isPincodeServiceable: true },
    }));
  }, { api: apiOrigin, id: productId, qty: quantity });
}

test('promotions reject invalid, expired, minimum and category codes without discount, and celebrate valid code once', async ({ page }, testInfo) => {
  await mockApi(page);
  await seedCustomerCart(page, 2);
  await page.goto(`${apiOrigin}/guturgo/cart`, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: 'Shopping cart' })).toBeVisible();

  for (const [code, expected] of [['INVALID', 'not valid'], ['EXPIRED', 'expired'], ['MINIMUM', 'higher minimum'], ['CATEGORY', 'not applicable']]) {
    await page.locator('#cart-promo-code').fill(code);
    await page.getByRole('button', { name: 'Apply Offer' }).click();
    await expect(page.getByRole('alert')).toContainText(expected);
    await expect(page.locator('.offer-celebration')).toHaveCount(0);
    await expect(page.locator('.promo-applied')).toHaveCount(0);
    if (code === 'INVALID') await page.screenshot({ path: testInfo.outputPath('promo-rejected-mock.png'), fullPage: true });
    await expect(page.locator('.summary')).not.toContainText('-₹10.00');
  }

  await page.locator('#cart-promo-code').fill('SAVE10');
  await page.getByRole('button', { name: 'Apply Offer' }).click();
  await expect(page.locator('.offer-celebration')).toContainText('Offer applied!');
  await expect(page.locator('.promo-applied')).toContainText('SAVE10 applied');
  await page.getByRole('button', { name: 'Dismiss offer applied message' }).click();
  await page.getByRole('button', { name: 'Apply Offer' }).click();
  await expect(page.locator('.offer-celebration')).toHaveCount(0);

  await page.getByRole('button', { name: 'Decrease Synthetic Saree' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'higher minimum purchase' })).toBeVisible();
  await expect(page.locator('.promo-applied')).toHaveCount(0);
  await expect(page.locator('.summary')).not.toContainText('-₹10.00');
});

test('delivery toast appears only after a verified cart crosses the free-delivery threshold and can be dismissed', async ({ page }, testInfo) => {
  await mockApi(page);
  await seedVerifiedGuestDelivery(page, 1);
  await page.goto(`${apiOrigin}/guturgo`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);
  await page.locator('shop-product-card').filter({ hasText: 'Synthetic Saree' }).getByRole('button', { name: 'In cart (1)' }).click();
  await expect(page.locator('.unlocked-toast')).toContainText('Free delivery unlocked');
  await page.screenshot({ path: testInfo.outputPath('free-delivery-unlocked-mock.png'), fullPage: true });
  await page.getByRole('button', { name: 'Dismiss free delivery notification' }).click();
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);
  await page.locator('shop-product-card').filter({ hasText: 'Synthetic Saree' }).getByRole('button', { name: 'In cart (2)' }).click();
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);
});

test('empty cart, unverified or stale location, failed quote and ineligible location never show unlocked delivery', async ({ page }) => {
  const api = await mockApi(page);
  await page.goto(`${apiOrigin}/guturgo`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.unlocked-toast')).toHaveCount(0);

  for (const mode of ['unverified', 'stale', 'ineligible', 'failed'] as const) {
    api.setDeliveryMode(mode === 'ineligible' ? 'ineligible' : mode === 'failed' ? 'failed' : 'eligible');
    await page.evaluate(({ api, id, mode }) => {
      localStorage.setItem('khata-dhari-cart:guturgo:guest', JSON.stringify([{ quantity: 2, product: { id, categoryId: 'synthetic-category', name: 'Synthetic Saree', description: '', imageUrl: `${api}/api/store/guturgo/products/${id}/image`, sellingPrice: 285, available: true } }]));
      sessionStorage.setItem('khata-dhari-storefront-serviceability:guturgo:guest', JSON.stringify({ storeKey: 'guturgo', pincode: '560001', verified: true, verifiedAtUtc: Date.now() - (mode === 'stale' ? 60 * 60 * 1000 : 1000), quote: { isDeliveryEnabled: true, isPincodeServiceable: true } }));
    }, { api: apiOrigin, id: productId, mode });
    if (mode === 'unverified') await page.evaluate(() => sessionStorage.removeItem('khata-dhari-storefront-serviceability:guturgo:guest'));
    await page.reload({ waitUntil: 'domcontentloaded' });
    await expect(page.locator('shop-product-card')).toBeVisible();
    await expect(page.locator('.unlocked-toast')).toHaveCount(0);
  }
});

test('checkout stays disabled without address and reports a mocked checkout provider failure without duplicate submission', async ({ page }) => {
  const api = await mockApi(page, { checkoutStatus: 503, checkoutDelayMs: 700 });
  await seedCustomerCart(page);
  await page.goto(`${apiOrigin}/guturgo/checkout`, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: 'Payment and delivery' })).toBeVisible();
  const placeOrder = page.getByRole('button', { name: 'Place order' });
  await expect(placeOrder).toBeDisabled();
  await expect(page.locator('.checkout-hint')).toContainText('Complete the delivery address');

  await page.locator('[name="recipientName"]').fill('Synthetic Customer');
  await page.locator('[name="mobile"]').fill('9876543210');
  await page.locator('[name="addressLine1"]').fill('1 Synthetic Road');
  await page.locator('[name="city"]').fill('Test City');
  await page.locator('[name="state"]').selectOption('Karnataka');
  await page.locator('[name="pincode"]').last().fill('560001');
  await expect(placeOrder).toBeEnabled({ timeout: 10000 });
  await Promise.all([placeOrder.click(), placeOrder.click()]);
  await expect(page.getByRole('alert')).toContainText('Synthetic checkout provider unavailable');
  await page.screenshot({ path: 'test-results/playwright/checkout-provider-error-mock.png', fullPage: true });
  expect(api.checkoutCount()).toBe(1);
});

test('inactive product cannot be ordered and missing product image thumbnail falls back to the authorized original', async ({ page }) => {
  const api = await mockApi(page, { inactive: true, imageFallback: true });
  await page.goto(`${apiOrigin}/guturgo`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('shop-product-card').filter({ hasText: 'Synthetic Saree' })).toBeVisible();
  const card = page.locator('shop-product-card').filter({ hasText: 'Synthetic Saree' });
  await expect(card.getByRole('button', { name: /Add to cart/ })).toHaveCount(0);
  await expect(card.getByText('Ordering Closed')).toBeVisible();

  await page.reload({ waitUntil: 'domcontentloaded' });
  const reloadedCard = page.locator('shop-product-card').filter({ hasText: 'Synthetic Saree' });
  await expect(reloadedCard).toBeVisible();
  await reloadedCard.scrollIntoViewIfNeeded();
  await expect.poll(() => api.imageRequests).toContain('thumbnail=true');
  await expect.poll(() => api.imageRequests).toContain('');
  await expect.poll(() => reloadedCard.locator('img').evaluate((node: HTMLImageElement) => node.naturalWidth)).toBeGreaterThan(0);
});

test('retailer switching isolates cart, wishlist and delayed customer session restoration', async ({ page }) => {
  const api = await mockApi(page, { sessionDelayMs: 1500 });
  await page.addInitScript(({ id }) => {
    const product = { id, categoryId: 'synthetic-category', name: 'Tenant A Item', description: '', imageUrl: '/images/product-placeholder.svg', sellingPrice: 285, available: true };
    localStorage.setItem('khatadhari.customer-session.guturgo', 'tenant-a-token');
    localStorage.setItem('khata-dhari-cart:guturgo:guest', JSON.stringify([{ quantity: 2, product }]));
    localStorage.setItem('khata-dhari-cart:other-retailer:guest', '[]');
    localStorage.setItem('khatadhari.wishlist.guturgo:guest', JSON.stringify(['synthetic-product']));
    localStorage.setItem('khatadhari.wishlist.other-retailer:guest', '[]');
  }, { id: 'tenant-a-product' });

  await page.goto(`${apiOrigin}/guturgo`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.brand-copy strong')).toContainText('GuturGo');
  await expect(page.locator('.cart-badge')).toHaveAttribute('aria-label', '2 items in cart');
  await expect(page.locator('.wishlist-badge')).toHaveAttribute('aria-label', '1 items in wishlist');
  await expect.poll(() => api.sessionRequests).toContain('guturgo');

  await page.evaluate(() => {
    history.pushState({}, '', '/other-retailer');
    window.dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page.locator('.brand-copy strong')).toContainText('Other Synthetic Retailer');
  await expect(page.locator('.cart-badge')).toHaveCount(0);
  await expect(page.locator('.wishlist-badge')).toHaveCount(0);
  await page.waitForTimeout(1600);
  await expect(page.getByRole('link', { name: 'Sign in to your account' })).toBeVisible();

  await page.goBack();
  await expect(page.locator('.brand-copy strong')).toContainText('GuturGo');
  await expect(page.locator('.cart-badge')).toHaveAttribute('aria-label', '2 items in cart');
  await expect(page.locator('.wishlist-badge')).toHaveAttribute('aria-label', '1 items in wishlist');
});

test('storefront header and catalog have no horizontal overflow across supported viewport widths', async ({ page }, testInfo) => {
  await mockApi(page);
  await page.goto(`${apiOrigin}/guturgo`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('shop-product-card')).toBeVisible();

  for (const width of [320, 360, 390, 430, 768, 1366, 1920]) {
    await page.setViewportSize({ width, height: width <= 430 ? 844 : 960 });
    const layout = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      window: window.innerWidth,
      document: document.documentElement.scrollWidth,
      body: document.body.scrollWidth,
      farthestRight: Math.max(...Array.from(document.querySelectorAll('body *')).map(element => element.getBoundingClientRect().right)),
      overflowElements: Array.from(document.querySelectorAll('body *')).filter(element => element.getBoundingClientRect().right > document.documentElement.clientWidth + 1).slice(0, 8).map(element => ({ tag: element.tagName.toLowerCase(), className: String(element.className).slice(0, 80), right: Math.round(element.getBoundingClientRect().right), width: Math.round(element.getBoundingClientRect().width) })),
      imageFit: getComputedStyle(document.querySelector('shop-product-card .product-image img')!).objectFit,
    }));
    expect(Math.max(layout.document, layout.body, layout.farthestRight), `No horizontal overflow at ${width}px: ${JSON.stringify(layout)}`).toBeLessThanOrEqual(layout.viewport + 1);
    expect(layout.imageFit).toBe('contain');
    const screenshotPath = testInfo.outputPath(`tenant-header-${width}px.png`);
    await page.screenshot({ path: screenshotPath });
    await testInfo.attach(`tenant-header-${width}px`, { path: screenshotPath, contentType: 'image/png' });
  }
});
