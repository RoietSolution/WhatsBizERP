import { expect, test } from '@playwright/test';

test('configured QA storefront catalog and search shell render responsively', async ({ page }, testInfo) => {
  const storefrontUrl = process.env.QA_STOREFRONT_URL?.trim();
  test.skip(!storefrontUrl, 'Set QA_STOREFRONT_URL to the authorized QA store URL, including its store key.');

  let parsedUrl: URL;
  try {
    parsedUrl = new URL(storefrontUrl!);
  } catch {
    throw new Error('QA_STOREFRONT_URL must be an absolute authorized QA URL.');
  }
  expect(parsedUrl.protocol).toBe('https:');
  expect(parsedUrl.hostname).toMatch(/^(?:qa|qa-[a-z0-9-]+)\.khatadhari\.com$/i);
  expect(parsedUrl.username + parsedUrl.password + parsedUrl.search + parsedUrl.hash).toBe('');

  const response = await page.goto(parsedUrl.href, { waitUntil: 'domcontentloaded' });
  expect(response, 'QA storefront navigation should return an HTTP response').not.toBeNull();
  expect(response!.status(), 'QA storefront should not return a server error').toBeLessThan(500);
  await expect(page.getByRole('banner')).toContainText(/GuturGo/i);
  await expect(page.getByRole('navigation', { name: 'Shop by category' })).toBeAttached();
  await expect(page.getByRole('textbox', { name: 'Search products' })).toBeVisible();
  const loadedProductImages = await page.locator('main img').evaluateAll(images =>
    images.filter(image => image instanceof HTMLImageElement && image.complete && image.naturalWidth > 0).length);
  expect(loadedProductImages, 'At least one product image should load in the catalog').toBeGreaterThan(0);

  for (const width of [360, 390, 768, 1366, 1920]) {
    await page.setViewportSize({ width, height: width <= 390 ? 844 : 960 });
    const dimensions = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      content: document.documentElement.scrollWidth,
    }));
    expect(dimensions.content, `No horizontal overflow at ${width}px`).toBeLessThanOrEqual(dimensions.viewport);
    await page.screenshot({
      path: testInfo.outputPath(`qa-storefront-catalog-${width}px.png`),
      fullPage: true,
    });
  }
});
