import { expect, test } from '@playwright/test';

test('configured QA ERP shell loads at supported viewport widths', async ({ page }, testInfo) => {
  const qaUrl = process.env.QA_ERP_URL;
  test.skip(!qaUrl, 'Set QA_ERP_URL to the authorized QA ERP base URL.');

  const response = await page.goto(qaUrl!, { waitUntil: 'domcontentloaded' });
  expect(response, 'QA ERP navigation should return an HTTP response').not.toBeNull();
  expect(response!.status(), 'QA ERP should not return a server error').toBeLessThan(500);
  await expect(page.locator('app-root')).toBeAttached();
  await expect(page).toHaveTitle(/KhataDhari ERP/i);

  for (const width of [1440, 768, 390, 320]) {
    await page.setViewportSize({ width, height: width <= 390 ? 844 : 960 });
    await page.waitForLoadState('domcontentloaded');
    const dimensions = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      content: document.documentElement.scrollWidth,
    }));
    expect(dimensions.content, `No horizontal overflow at ${width}px`).toBeLessThanOrEqual(dimensions.viewport);
    await page.screenshot({
      path: testInfo.outputPath(`qa-erp-shell-${width}px.png`),
      fullPage: true,
    });
  }
});
