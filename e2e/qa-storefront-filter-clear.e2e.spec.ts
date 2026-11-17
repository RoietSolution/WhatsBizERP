import { expect, test } from '@playwright/test';

async function useLocalMockStorefront(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/public/routes/guturgo', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ slug: 'guturgo', type: 'RETAILER', storeKey: 'guturgo', canonicalPath: '/guturgo' }),
  }));
  await page.route('https://images.unsplash.com/**', route => route.fulfill({
    status: 200,
    contentType: 'image/png',
    body: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/c9sAAAAASUVORK5CYII=', 'base64'),
  }));
}

test('Filter & Sort Clear All immediately restores catalog defaults and URL state', async ({ page }, testInfo) => {
  const configuredUrl = process.env.QA_STOREFRONT_URL?.trim();
  test.skip(!configuredUrl, 'Set QA_STOREFRONT_URL to an authorized QA storefront URL.');

  const url = new URL(configuredUrl!);
  const localMock = url.protocol === 'http:' && url.hostname === '127.0.0.1' && url.port === '4300';
  if (!localMock) {
    expect(url.protocol).toBe('https:');
    expect(url.hostname).toMatch(/^(?:qa|qa-[a-z0-9-]+)\.khatadhari\.com$/i);
  }
  expect(url.username + url.password + url.hash).toBe('');
  if (localMock) {
    // The demo provider supplies tenant data, but public route resolution is still an API call.
    await useLocalMockStorefront(page);
  }

  url.searchParams.set('filterResetProbe', 'preserve-me');
  await page.goto(url.href, { waitUntil: 'domcontentloaded' });
  const filterButton = page.getByRole('button', { name: /Filter/ });
  await expect(filterButton).toBeVisible();
  await filterButton.click();
  const categoryId = await page.locator('#catalog-filter-category option').evaluateAll(options =>
    options.map(option => (option as HTMLOptionElement).value).find(value => value !== 'all'));
  expect(categoryId).toBeTruthy();
  await page.locator('#catalog-filter-category').selectOption(categoryId!);
  await page.locator('#catalog-filter-minimum').fill('500');
  await page.locator('#catalog-filter-maximum').fill('900');
  await page.getByLabel('In-stock products only').check();
  await page.locator('#catalog-filter-sort').selectOption('PRICE_HIGH_TO_LOW');
  await page.getByRole('button', { name: 'Apply Filters' }).click();
  await expect(filterButton.locator('.active-filter-count')).toHaveText('4');
  await expect.poll(() => new URL(page.url()).searchParams.get('category')).toBe(categoryId!);

  await filterButton.click();
  await expect(page.getByRole('dialog', { name: 'Filter & Sort' })).toBeVisible();

  const defaultProductsResponse = localMock ? null : page.waitForResponse(response => {
    const responseUrl = new URL(response.url());
    return responseUrl.pathname.endsWith('/products')
      && !responseUrl.searchParams.has('categoryId')
      && !responseUrl.searchParams.has('minPrice')
      && !responseUrl.searchParams.has('maxPrice')
      && !responseUrl.searchParams.has('inStockOnly')
      && !responseUrl.searchParams.has('sortBy');
  });
  await page.getByRole('button', { name: 'Clear All' }).click();
  if (defaultProductsResponse) expect((await defaultProductsResponse).status()).toBe(200);

  await expect(page.locator('#catalog-filter-category')).toHaveValue('all');
  await expect(page.locator('#catalog-filter-minimum')).toHaveValue('');
  await expect(page.locator('#catalog-filter-maximum')).toHaveValue('');
  await expect(page.getByLabel('In-stock products only')).not.toBeChecked();
  await expect(page.locator('#catalog-filter-sort')).toHaveValue('RECOMMENDED');
  await expect(page.locator('.category-chip.selected').first()).toContainText('All');
  await expect(page.getByRole('button', { name: /Filter/ }).locator('.active-filter-count')).toHaveCount(0);
  if (localMock) await expect(page.locator('.result-count')).toContainText(/\d+ products?/);
  await expect.poll(() => {
    const current = new URL(page.url());
    return ['category', 'minPrice', 'maxPrice', 'inStockOnly', 'sortBy'].some(key => current.searchParams.has(key));
  }).toBe(false);
  expect(new URL(page.url()).searchParams.get('filterResetProbe')).toBe('preserve-me');

  await page.goBack();
  await expect.poll(() => new URL(page.url()).searchParams.get('category')).toBe(categoryId!);
  await expect(page.getByRole('button', { name: /Filter/ }).locator('.active-filter-count')).toHaveText('4');
  await page.goForward();
  await expect.poll(() => new URL(page.url()).searchParams.has('category')).toBe(false);
  await expect(page.getByRole('button', { name: /Filter/ }).locator('.active-filter-count')).toHaveCount(0);

  for (const width of [320, 360, 390, 430, 768, 1366, 1920]) {
    await page.setViewportSize({ width, height: width <= 430 ? 844 : 960 });
    const dimensions = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      content: document.documentElement.scrollWidth,
    }));
    expect(dimensions.content, `No horizontal overflow at ${width}px`).toBeLessThanOrEqual(dimensions.viewport);
    await page.screenshot({ path: testInfo.outputPath(`filter-clear-${width}px.png`), fullPage: true });
  }
});

test('filter price validation rejects negative and inverted ranges', async ({ page }) => {
  await useLocalMockStorefront(page);
  await page.goto('http://127.0.0.1:4300/guturgo', { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: /Filter/ }).click();

  const minimum = page.locator('#catalog-filter-minimum');
  const maximum = page.locator('#catalog-filter-maximum');
  const apply = page.getByRole('button', { name: 'Apply Filters' });
  await minimum.fill('-1');
  await expect(page.getByRole('alert')).toContainText('non-negative minimum price');
  await expect(apply).toBeDisabled();

  await minimum.fill('20');
  await maximum.fill('10');
  await expect(page.getByRole('alert')).toContainText('Minimum price must not exceed maximum price');
  await expect(apply).toBeDisabled();

  await maximum.fill('30');
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(apply).toBeEnabled();
});

test('catalog shows an empty state for unmatched prices and can recover with Clear filters', async ({ page }) => {
  await useLocalMockStorefront(page);
  await page.goto('http://127.0.0.1:4300/guturgo', { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: /Filter/ }).click();
  await page.locator('#catalog-filter-maximum').fill('1');
  await page.getByRole('button', { name: 'Apply Filters' }).click();

  await expect(page.getByRole('heading', { name: 'No products match these filters' })).toBeVisible();
  await page.getByRole('button', { name: 'Clear filters' }).click();
  await expect(page.locator('.result-count')).toContainText(/\d+ products?/);
  await expect(page.getByRole('heading', { name: 'No products match these filters' })).toHaveCount(0);
});

test('mock catalog rejects out-of-stock adds, preserves cart and wishlist, and renders badge count boundaries', async ({ page }, testInfo) => {
  await useLocalMockStorefront(page);
  await page.goto('http://127.0.0.1:4300/guturgo', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.cart-badge')).toHaveCount(0);
  await expect(page.locator('.wishlist-badge')).toHaveCount(0);

  const firstCard = page.locator('shop-product-card').filter({ hasText: 'Amul Gold Milk' });
  await firstCard.getByRole('button', { name: 'Add to cart' }).click();
  await firstCard.getByRole('button', { name: 'In cart (1)' }).click();
  await expect(firstCard.getByRole('button', { name: 'In cart (2)' })).toBeVisible();
  await expect(page.locator('.cart-badge')).toHaveText('2');
  await firstCard.getByRole('button', { name: 'Add Amul Gold Milk to wishlist' }).click();
  await expect(page.locator('.wishlist-badge')).toHaveText('1');

  const unavailable = page.locator('shop-product-card').filter({ hasText: 'Mango Greek Yoghurt' });
  await expect(unavailable.getByText('Currently unavailable')).toBeVisible();
  await expect(unavailable.getByRole('button', { name: /Add to cart/ })).toHaveCount(0);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(page.locator('.cart-badge')).toHaveText('2');
  await expect(page.locator('.wishlist-badge')).toHaveText('1');

  for (const count of [0, 1, 9, 10, 100]) {
    await page.evaluate(value => {
      localStorage.setItem('khata-dhari-cart:guturgo:guest', JSON.stringify([{ quantity: value, product: { id: 'badge-counter', sellingPrice: 1 } }]));
      localStorage.setItem('khatadhari.wishlist.guturgo:guest', JSON.stringify(Array.from({ length: value }, (_, index) => `wish-${index}`)));
    }, count);
    await page.reload({ waitUntil: 'domcontentloaded' });
    const expected = count === 0 ? null : count > 99 ? '99+' : String(count);
    if (expected === null) {
      await expect(page.locator('.cart-badge')).toHaveCount(0);
      await expect(page.locator('.wishlist-badge')).toHaveCount(0);
    } else {
      const cartBadge = page.locator('.cart-badge');
      const wishlistBadge = page.locator('.wishlist-badge');
      await expect(cartBadge).toHaveText(expected);
      await expect(wishlistBadge).toHaveText(expected);
      const positions = await page.evaluate(() => ['.cart-badge', '.wishlist-badge'].map(selector => {
        const badge = document.querySelector<HTMLElement>(selector)!;
        const wrapper = badge.parentElement!.getBoundingClientRect();
        const rect = badge.getBoundingClientRect();
        return { topOffset: rect.top - wrapper.top, rightOffset: wrapper.right - rect.right };
      }));
      expect(Math.abs(positions[0].topOffset - positions[1].topOffset)).toBeLessThanOrEqual(1);
      expect(Math.abs(positions[0].rightOffset - positions[1].rightOffset)).toBeLessThanOrEqual(1);
    }
    if (count === 100) {
      await page.setViewportSize({ width: 390, height: 844 });
      await page.screenshot({ path: testInfo.outputPath('header-badges-390px.png'), fullPage: true });
    }
  }
});

test('sign-in failures stay generic and mock OTP resend is blocked during its cooldown', async ({ page }, testInfo) => {
  await useLocalMockStorefront(page);
  await page.route('**/api/store/guturgo/customer-auth/password/signin', route => route.fulfill({
    status: 401,
    contentType: 'application/json',
    body: JSON.stringify({ message: 'The mobile number or password is incorrect.' }),
  }));
  await page.route('**/api/store/guturgo/customer-auth/otp/request', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ challengeId: 'mock-challenge', expiresAt: new Date(Date.now() + 60_000).toISOString(), resendAfterSeconds: 60 }),
  }));
  await page.route('**/api/store/guturgo/customer-auth/otp/verify', route => route.fulfill({
    status: 400,
    contentType: 'application/json',
    body: JSON.stringify({ message: 'The verification code is invalid or expired.' }),
  }));
  await page.goto('http://127.0.0.1:4300/guturgo/auth?returnUrl=%2Fguturgo%2Fcart', { waitUntil: 'domcontentloaded' });

  await page.locator('#signin-mobile').fill('123');
  await page.locator('#signin-password').fill('wrong-password');
  await page.getByRole('button', { name: 'Sign In', exact: true }).last().click();
  await expect(page.getByRole('alert')).toContainText('mobile number or password is incorrect');
  await expect(page.getByRole('alert')).not.toContainText('account does not exist');

  await page.getByRole('tab', { name: 'OTP' }).click();
  await page.locator('#otp-mobile').fill('9876543210');
  await page.getByRole('button', { name: 'Send OTP' }).click();
  await expect(page.getByRole('button', { name: /Resend OTP in/ })).toBeDisabled();
  await page.locator('#login-otp').fill('000000');
  await page.getByRole('button', { name: 'Verify & Sign In' }).click();
  await expect(page.getByRole('alert')).toContainText('invalid or expired');
  await page.screenshot({ path: testInfo.outputPath('mock-auth-failure.png'), fullPage: true });
});

test('mock sign-in restores the intended cart route and logout clears the tenant session', async ({ page }) => {
  await useLocalMockStorefront(page);
  await page.route('**/api/store/guturgo/customer-auth/password/signin', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ customerSessionToken: 'synthetic-session', customer: { id: 'synthetic-customer', name: 'Synthetic Customer' } }),
  }));
  await page.route('**/api/store/guturgo/session', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ id: 'synthetic-customer', name: 'Synthetic Customer' }),
  }));
  await page.route('**/api/store/guturgo/wishlist**', route => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await page.goto('http://127.0.0.1:4300/guturgo/auth?returnUrl=%2Fguturgo%2Fcart', { waitUntil: 'domcontentloaded' });
  await page.locator('#signin-mobile').fill('9876543210');
  await page.locator('#signin-password').fill('SyntheticPass1');
  await page.getByRole('button', { name: 'Sign In', exact: true }).last().click();
  await expect(page).toHaveURL(/\/guturgo\/cart$/);
  await expect(page.getByRole('heading', { name: 'Shopping cart' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Your cart is empty' })).toBeVisible();

  await page.goto('http://127.0.0.1:4300/guturgo', { waitUntil: 'domcontentloaded' });
  await page.getByRole('button', { name: 'Open store navigation' }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('khatadhari.customer-session.guturgo'))).toBeNull();
  await expect(page.getByRole('link', { name: 'Sign in to your account' })).toBeVisible();
});

test('expired mock customer session cannot enter the protected cart', async ({ page }) => {
  await useLocalMockStorefront(page);
  await page.addInitScript(() => localStorage.setItem('khatadhari.customer-session.guturgo', 'expired-synthetic-token'));
  await page.route('**/api/store/guturgo/session', route => route.fulfill({
    status: 401,
    contentType: 'application/json',
    body: JSON.stringify({ message: 'Session expired.' }),
  }));
  await page.goto('http://127.0.0.1:4300/guturgo/cart', { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveURL(/\/guturgo\/auth\?/);
  await expect(page.getByRole('heading', { name: 'Sign in to your account' })).toBeVisible();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('khatadhari.customer-session.guturgo'))).toBeNull();
});
