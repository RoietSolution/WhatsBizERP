import { expect, test } from '@playwright/test';

const localPwa = process.env.CUSTOMER_PWA_URL?.trim();
const widths = [320, 360, 390, 430, 768, 1366, 1920];

async function mockTenantResolution(page: import('@playwright/test').Page): Promise<void> {
  await page.route('**/api/public/routes/**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ slug: 'guturgo', type: 'RETAILER', storeKey: 'guturgo', canonicalPath: '/guturgo' }),
  }));
}

async function mockCustomerPasswordSignIn(page: import('@playwright/test').Page, profileImageUrl?: string): Promise<void> {
  await page.route('**/customer-auth/password/signin', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ customerSessionToken: 'synthetic-session-token', customer: { id: 'synthetic-customer', name: 'Synthetic Shopper', ...(profileImageUrl ? { profileImageUrl } : {}) } }),
  }));
  await page.route('**/api/store/guturgo/session', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ id: 'synthetic-customer', name: 'Synthetic Shopper', ...(profileImageUrl ? { profileImageUrl } : {}) }),
  }));
}

async function seedCartWithVerifiedDeliveryLocation(page: import('@playwright/test').Page, revision = 1, options: { empty?: boolean; verified?: boolean } = {}): Promise<void> {
  await page.addInitScript(({ userRevision, empty, verified }) => {
    const cartKey = 'khata-dhari-cart:guturgo:guest';
    if (!localStorage.getItem(cartKey)) {
      localStorage.setItem(cartKey, JSON.stringify(empty ? [] : [{ quantity: 1, product: { id: 'amul-gold-milk', sellingPrice: 68, available: true } }]));
    }
    const revisionKey = `${cartKey}:revision`;
    if (!localStorage.getItem(revisionKey)) localStorage.setItem(revisionKey, String(userRevision));
    const serviceabilityKey = 'khata-dhari-storefront-serviceability:guturgo:guest';
    if (verified && !sessionStorage.getItem(serviceabilityKey)) {
      sessionStorage.setItem(serviceabilityKey, JSON.stringify({ storeKey: 'guturgo', pincode: '560001', verified: true, verifiedAtUtc: Date.now(), quote: { isDeliveryEnabled: true, isPincodeServiceable: true } }));
    }
  }, { userRevision: revision, empty: options.empty ?? false, verified: options.verified ?? true });
}

async function renderConfiguredBannerFixture(page: import('@playwright/test').Page, count: number): Promise<void> {
  await page.evaluate((bannerCount) => {
    document.querySelector('.store-banners')?.remove();
    const rewards = document.querySelector('.home-rewards');
    if (!rewards?.parentElement) throw new Error('Homepage rewards section was not rendered.');
    const scopeAttribute = Array.from(rewards.attributes).find(attribute => attribute.name.startsWith('_ngcontent-'))?.name;
    const section = document.createElement('section');
    section.className = 'store-banners';
    section.setAttribute('aria-label', 'Synthetic configured banner layout');
    if (scopeAttribute) section.setAttribute(scopeAttribute, '');
    for (let index = 0; index < bannerCount; index++) {
      const banner = document.createElement('a');
      banner.className = `store-banner ${index ? 'secondary' : 'primary'}`;
      banner.href = '#';
      if (scopeAttribute) banner.setAttribute(scopeAttribute, '');
      const picture = document.createElement('picture');
      const image = document.createElement('img');
      image.width = 1200;
      image.height = 480;
      image.alt = `Synthetic configured banner ${index + 1}`;
      image.src = `data:image/svg+xml,${encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="480"><rect width="1200" height="480" fill="#e9f4eb"/><text x="60" y="250" font-size="60" fill="#075c3e">Configured promotion</text></svg>')}`;
      if (scopeAttribute) { picture.setAttribute(scopeAttribute, ''); image.setAttribute(scopeAttribute, ''); }
      picture.append(image);
      banner.append(picture);
      if (scopeAttribute) banner.setAttribute(scopeAttribute, '');
      section.append(banner);
    }
    rewards.parentElement.insertBefore(section, rewards);
  }, count);
}

test('customer PWA homepage rewards and configured benefits stay readable responsively', async ({ page }, testInfo) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await mockTenantResolution(page);
  await page.goto(new URL('/guturgo', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('navigation', { name: 'Shop by category' })).toBeVisible();
  await expect(page.getByRole('link', { name: /My Coins Sign in to view/ })).toBeVisible();
  await expect(page.getByRole('link', { name: /Refer & Earn Invite friends/ })).toBeVisible();
  await expect(page.locator('.trust-strip')).toContainText('Free delivery');
  await expect(page.locator('.trust-strip')).toContainText('499');
  await expect(page.locator('.trust-strip')).toContainText('7 days');
  await expect(page.locator('.trust-strip')).toContainText('Shop with confidence');
  await expect(page.locator('.trust-strip')).not.toContainText('Checkout options shown at checkout');
  await expect(page.locator('.store-banners')).toHaveCount(0);

  for (const width of widths) {
    await page.setViewportSize({ width, height: width <= 430 ? 844 : 960 });
    await renderConfiguredBannerFixture(page, 1);
    await expect(page.locator('.store-banners .store-banner')).toHaveCount(1);
    await renderConfiguredBannerFixture(page, 2);
    const bannerLayout = await page.locator('.store-banners').evaluate(section => ({
      bannerCount: section.querySelectorAll('.store-banner').length,
      ratio: section.querySelector('.store-banner picture')!.getBoundingClientRect().width / section.querySelector('.store-banner picture')!.getBoundingClientRect().height,
      viewport: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
    }));
    expect(bannerLayout.bannerCount).toBe(2);
    expect(bannerLayout.ratio).toBeCloseTo(2.5, 1);
    expect(bannerLayout.scrollWidth, `configured banners should not overflow at ${width}px`).toBeLessThanOrEqual(bannerLayout.viewport);
    if (width === 390 || width === 1366) {
      const path = testInfo.outputPath(`customer-home-configured-banners-${width}px.png`);
      await page.locator('.store-banners').screenshot({ path });
      await testInfo.attach(`customer-home-configured-banners-${width}px`, { path, contentType: 'image/png' });
    }
    await page.locator('.store-banners').evaluate(section => section.remove());
    const dimensions = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, content: document.documentElement.scrollWidth }));
    expect(dimensions.content, `homepage should not overflow at ${width}px`).toBeLessThanOrEqual(dimensions.viewport);
    if (width === 390 || width === 1366) {
      const path = testInfo.outputPath(`customer-home-benefits-${width}px.png`);
      await page.screenshot({ path, fullPage: true });
      await testInfo.attach(`customer-home-benefits-${width}px`, { path, contentType: 'image/png' });
    }
  }
});

test('product details uses configured storefront data and fits mobile and desktop layouts', async ({ page }, testInfo) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await mockTenantResolution(page);
  await page.goto(new URL('/guturgo/products/amul-gold-milk', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { level: 1, name: 'Amul Gold Milk' })).toBeVisible();
  await expect(page.locator('.delivery-progress')).toContainText('499');
  await expect(page.locator('.delivery-estimate')).toContainText('Select a delivery location');
  await expect(page.locator('.detail-benefits')).toContainText('Cash on delivery');
  await expect(page.locator('.detail-benefits')).toContainText('7-day returns after delivery');
  await expect(page.locator('.return-policy')).toHaveCount(0);
  await expect(page.locator('.product-information')).toContainText('Keep refrigerated');
  expect(await page.locator('.spec-item').count()).toBeGreaterThan(0);
  await expect(page.locator('.spec-item dt').filter({ hasText: 'Storage' })).toHaveCount(1);
  await expect(page.locator('.reviews')).toContainText(/ratings/i);

  for (const width of widths) {
    await page.setViewportSize({ width, height: width <= 430 ? 844 : 960 });
    const dimensions = await page.evaluate(() => ({ viewport: document.documentElement.clientWidth, content: document.documentElement.scrollWidth }));
    expect(dimensions.content, `product details should not overflow at ${width}px`).toBeLessThanOrEqual(dimensions.viewport);
    const tagStyles = await page.locator('.spec-item').first().evaluate(element => ({
      display: getComputedStyle(element).display,
      width: element.getBoundingClientRect().width,
      parentWidth: element.parentElement!.getBoundingClientRect().width,
      padding: getComputedStyle(element).padding,
    }));
    // Flex items compute to `flex` after CSS blockification even when styled inline-flex.
    expect(tagStyles.display, `specification is an individual tag at ${width}px`).toBe('flex');
    expect(tagStyles.width, `specification tag stays content-sized at ${width}px`).toBeLessThan(tagStyles.parentWidth);
    if (width <= 720) await expect(page.locator('.sticky-purchase')).toBeVisible();
    else await expect(page.locator('.sticky-purchase')).toBeHidden();
    if ([390, 1366].includes(width)) {
      const path = testInfo.outputPath(`customer-product-details-${width}px.png`);
      await page.screenshot({ path, fullPage: true });
      await testInfo.attach(`customer-product-details-${width}px`, { path, contentType: 'image/png' });
    }
  }
});

test('customer logout returns to branded sign-in and keeps only a safe public return URL', async ({ page }, testInfo) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await mockTenantResolution(page);
  await mockCustomerPasswordSignIn(page);
  await page.goto(new URL('/guturgo/auth?returnUrl=%2Fguturgo%2Fproducts%2Famul-gold-milk', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: 'Welcome to GuturGo' })).toBeVisible();
  await expect(page.getByRole('tab', { name: 'Sign In' })).toHaveText('Sign In');
  await page.getByLabel(/Mobile number/).fill('9876543210');
  await page.locator('#signin-password').fill('Synthetic-password-1');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page).toHaveURL(/\/guturgo\/products\/amul-gold-milk/);
  await expect(page.locator('.header-avatar-button .header-avatar.initials')).toHaveText('SS');
  await page.getByRole('button', { name: 'Open store navigation' }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/guturgo\/auth\?/);
  expect(new URL(page.url()).searchParams.get('returnUrl')).toBe('/guturgo/products/amul-gold-milk');
  await expect(page.getByRole('heading', { name: 'Welcome to GuturGo' })).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  const authScreenshot = testInfo.outputPath('customer-auth-after-logout-390px.png');
  await page.screenshot({ path: authScreenshot, fullPage: true });
  await testInfo.attach('customer-auth-after-logout-390px', { path: authScreenshot, contentType: 'image/png' });

  await page.goto(new URL('/guturgo/auth', localPwa).href, { waitUntil: 'domcontentloaded' });
  await page.getByLabel(/Mobile number/).fill('9876543210');
  await page.locator('#signin-password').fill('Synthetic-password-1');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page).toHaveURL(/\/guturgo$/);
  await page.goto(new URL('/guturgo/account/profile', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: /My Profile/ })).toBeVisible();
  await page.getByRole('button', { name: 'Open store navigation' }).click();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/guturgo\/auth$/);
  expect(new URL(page.url()).searchParams.has('returnUrl')).toBe(false);
});

test('account avatar stays circular and footer remains compact across responsive widths', async ({ page }, testInfo) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await mockTenantResolution(page);
  await page.goto(new URL('/guturgo', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('link', { name: 'Sign in to your account' })).toBeVisible();

  for (const width of widths) {
    await page.setViewportSize({ width, height: width <= 430 ? 844 : 960 });
    const avatar = await page.locator('.header-avatar-button').evaluate(element => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return { width: rect.width, height: rect.height, aspectRatio: style.aspectRatio, radius: style.borderRadius, overflow: style.overflow, flexShrink: style.flexShrink, scrollWidth: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth };
    });
    expect(avatar.width, `guest avatar is square at ${width}px`).toBe(avatar.height);
    expect(avatar.aspectRatio).toBe('1 / 1');
    expect(avatar.radius).toBe('50%');
    expect(avatar.overflow).toBe('hidden');
    expect(avatar.flexShrink).toBe('0');
    expect(avatar.scrollWidth, `header does not overflow at ${width}px`).toBeLessThanOrEqual(avatar.viewport);
    if (width === 390 || width === 1366) {
      const path = testInfo.outputPath(`customer-avatar-footer-${width}px.png`);
      await page.screenshot({ path, fullPage: true });
      await testInfo.attach(`customer-avatar-footer-${width}px`, { path, contentType: 'image/png' });
    }
  }

  const footer = page.locator('.store-privacy-actions');
  await expect(footer.getByRole('link', { name: 'Terms & Conditions' })).toBeVisible();
  await expect(footer.getByRole('link', { name: 'Cancellation, returns & refunds' })).toBeVisible();
  await expect(footer.getByRole('link', { name: 'Privacy policy' })).toBeVisible();
  await expect(footer.getByRole('button', { name: 'Privacy choices' })).toBeVisible();
  const footerStyles = await footer.evaluate(element => ({ background: getComputedStyle(element).backgroundColor, display: getComputedStyle(element).display }));
  expect(footerStyles.background).not.toBe('rgba(0, 0, 0, 0)');
  expect(footerStyles.display).toBe('flex');
  await page.setViewportSize({ width: 390, height: 844 });
  const mobileFooter = await footer.evaluate(element => ({ display: getComputedStyle(element).display, columns: getComputedStyle(element).gridTemplateColumns.split(' ').length }));
  expect(mobileFooter.display).toBe('grid');
  expect(mobileFooter.columns).toBe(2);
  const profileImageUrl = new URL('/test-profile.svg', localPwa).href;
  await page.route(profileImageUrl, route => route.fulfill({ status: 200, contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="green"/></svg>' }));
  await mockCustomerPasswordSignIn(page, profileImageUrl);
  await page.goto(new URL('/guturgo/auth', localPwa).href, { waitUntil: 'domcontentloaded' });
  await page.getByLabel(/Mobile number/).fill('9876543210');
  await page.locator('#signin-password').fill('Synthetic-password-1');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.locator('.header-avatar-button img.header-avatar')).toBeVisible();
  const signedAvatar = await page.locator('.header-avatar-button').evaluate(element => ({ width: element.getBoundingClientRect().width, height: element.getBoundingClientRect().height, radius: getComputedStyle(element).borderRadius, overflow: getComputedStyle(element).overflow, flexShrink: getComputedStyle(element).flexShrink }));
  const avatarImage = await page.locator('.header-avatar-button img').evaluate(image => ({ fit: getComputedStyle(image).objectFit, width: image.getBoundingClientRect().width, height: image.getBoundingClientRect().height, radius: getComputedStyle(image).borderRadius }));
  expect(signedAvatar.width).toBe(signedAvatar.height);
  expect(signedAvatar.radius).toBe('50%');
  expect(signedAvatar.overflow).toBe('hidden');
  expect(signedAvatar.flexShrink).toBe('0');
  expect(avatarImage.fit).toBe('cover');
  expect(avatarImage.width).toBe(avatarImage.height);
  expect(avatarImage.radius).toBe('50%');
  await page.setViewportSize({ width: 390, height: 844 });
  const imagePath = testInfo.outputPath('customer-profile-avatar-390px.png');
  await page.screenshot({ path: imagePath, fullPage: false });
  await testInfo.attach('customer-profile-avatar-390px', { path: imagePath, contentType: 'image/png' });
});

test('free-delivery progress is shown only on eligible routes and verified carts', async ({ page }) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await seedCartWithVerifiedDeliveryLocation(page);
  await mockTenantResolution(page);
  await page.goto(new URL('/guturgo', localPwa).href, { waitUntil: 'domcontentloaded' });
  const popup = page.locator('.delivery-progress.toast:not(.unlocked-toast)');
  await expect(popup).toBeVisible();
  await expect(popup.getByRole('button', { name: 'Dismiss free delivery progress' })).toBeVisible();
  await expect(popup).toContainText('FREE DELIVERY');

  for (const route of ['/guturgo/products', '/guturgo/categories', '/guturgo/search']) {
    await page.goto(new URL(route, localPwa).href, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('.delivery-progress.toast:not(.unlocked-toast)')).toBeVisible();
  }
  for (const route of ['/guturgo/products/amul-gold-milk', '/guturgo/terms', '/guturgo/terms-and-conditions', '/guturgo/privacy', '/guturgo/cancellation-return-refund', '/guturgo/auth', '/guturgo/account/profile', '/guturgo/checkout', '/guturgo/orders']) {
    await page.goto(new URL(route, localPwa).href, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('.delivery-progress.toast')).toHaveCount(0);
  }
});

test('free-delivery dismissal persists through navigation and refresh until successful cart mutation', async ({ page }, testInfo) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await seedCartWithVerifiedDeliveryLocation(page, 3);
  await mockTenantResolution(page);
  await page.goto(new URL('/guturgo', localPwa).href, { waitUntil: 'domcontentloaded' });
  const popup = page.locator('.delivery-progress.toast:not(.unlocked-toast)');
  await expect(popup).toBeVisible();
  await popup.getByRole('button', { name: 'Dismiss free delivery progress' }).click();
  await expect(popup).toHaveCount(0);

  await page.goto(new URL('/guturgo/terms', localPwa).href, { waitUntil: 'domcontentloaded' });
  await page.goBack({ waitUntil: 'domcontentloaded' });
  await expect(popup).toHaveCount(0);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(popup).toHaveCount(0);

  await page.getByRole('button', { name: /Add to cart/i }).first().click();
  await expect(popup).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  const path = testInfo.outputPath('customer-free-delivery-progress-390px.png');
  await page.screenshot({ path, fullPage: true });
  await testInfo.attach('customer-free-delivery-progress-390px', { path, contentType: 'image/png' });
});

test('free-delivery popup is available on cart and suppressed for empty or unverified carts', async ({ page }) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await seedCartWithVerifiedDeliveryLocation(page);
  await mockTenantResolution(page);
  await mockCustomerPasswordSignIn(page);
  await page.goto(new URL('/guturgo/auth', localPwa).href, { waitUntil: 'domcontentloaded' });
  await page.getByLabel(/Mobile number/).fill('9876543210');
  await page.locator('#signin-password').fill('Synthetic-password-1');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page).toHaveURL(/\/guturgo$/);
  await page.evaluate(() => {
    sessionStorage.setItem('khata-dhari-storefront-serviceability:guturgo:synthetic-customer', JSON.stringify({ storeKey: 'guturgo', pincode: '560001', verified: true, verifiedAtUtc: Date.now(), quote: { isDeliveryEnabled: true, isPincodeServiceable: true } }));
  });
  await page.goto(new URL('/guturgo/cart', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.delivery-progress.toast:not(.unlocked-toast)')).toBeVisible();

  await page.evaluate(() => {
    localStorage.setItem('khata-dhari-cart:guturgo:synthetic-customer', '[]');
    localStorage.setItem('khata-dhari-cart:guturgo:synthetic-customer:revision', '0');
  });
  await page.goto(new URL('/guturgo', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.delivery-progress.toast')).toHaveCount(0);
});

test('free-delivery progress stays hidden without a fresh verified delivery location', async ({ page }) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await seedCartWithVerifiedDeliveryLocation(page, 1, { verified: false });
  await mockTenantResolution(page);
  await page.goto(new URL('/guturgo', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('navigation', { name: 'Shop by category' })).toBeVisible();
  await expect(page.locator('.delivery-progress.toast')).toHaveCount(0);
});

test('empty cart does not display free-delivery progress', async ({ page }) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await seedCartWithVerifiedDeliveryLocation(page, 0, { empty: true });
  await mockTenantResolution(page);
  await page.goto(new URL('/guturgo', localPwa).href, { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('navigation', { name: 'Shop by category' })).toBeVisible();
  await expect(page.locator('.delivery-progress.toast')).toHaveCount(0);
});

test('sign-in loading state renders plain text without corrupted icon characters', async ({ page }) => {
  test.skip(!localPwa, 'Set CUSTOMER_PWA_URL to a local Customer PWA running with the explicit mock configuration.');
  await mockTenantResolution(page);
  await page.route('**/customer-auth/password/signin', async route => {
    await new Promise(resolve => setTimeout(resolve, 300));
    await route.fulfill({ status: 401, contentType: 'application/json', body: JSON.stringify({ message: 'Sign in failed.' }) });
  });
  await page.goto(new URL('/guturgo/auth', localPwa).href, { waitUntil: 'domcontentloaded' });
  const signInTab = page.getByRole('tab', { name: 'Sign In' });
  await expect(signInTab).toHaveText('Sign In');
  await page.getByLabel(/Mobile number/).fill('9876543210');
  await page.locator('#signin-password').fill('Synthetic-password-1');
  const submit = page.getByRole('button', { name: 'Sign In' });
  await submit.click();
  await expect(page.getByRole('button', { name: 'Signing in...' })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('Sign in failed.');
  await expect(page.locator('body')).not.toContainText('â');
});
