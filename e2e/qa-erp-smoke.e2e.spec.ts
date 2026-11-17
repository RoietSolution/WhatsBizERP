import { expect, test } from '@playwright/test';

test('configured QA ERP shell loads at supported viewport widths', async ({ page }, testInfo) => {
  const qaUrl = process.env.QA_ERP_URL;
  test.skip(!qaUrl, 'Set QA_ERP_URL to the authorized QA ERP base URL.');

  const response = await page.goto(qaUrl!, { waitUntil: 'domcontentloaded' });
  expect(response, 'QA ERP navigation should return an HTTP response').not.toBeNull();
  expect(response!.status(), 'QA ERP should not return a server error').toBeLessThan(500);
  await expect(page.locator('app-root')).toBeAttached();
  await expect(page).toHaveTitle(/KhataDhari ERP/i);

  for (const width of [360, 390, 768, 1366, 1920]) {
    await page.setViewportSize({ width, height: width <= 390 ? 844 : 960 });
    await page.evaluate(async () => {
      await document.fonts.ready;
      await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
    });
    const dimensions = await page.evaluate(() => {
      const viewport = document.documentElement.clientWidth;
      const offenders = Array.from(document.querySelectorAll('body *')).map(element => {
        const rect = element.getBoundingClientRect();
        return {
          tag: element.tagName.toLowerCase(),
          id: element.id,
          className: typeof element.className === 'string' ? element.className.slice(0, 100) : '',
          right: Math.round(rect.right * 10) / 10,
          width: Math.round(rect.width * 10) / 10,
        };
      }).filter(element => element.width > 0 && element.right > viewport + 0.5).slice(0, 8);
      return { viewport, content: document.documentElement.scrollWidth, offenders };
    });
    expect(dimensions.content, `No horizontal overflow at ${width}px; overflowing elements: ${JSON.stringify(dimensions.offenders)}`)
      .toBeLessThanOrEqual(dimensions.viewport);
    await page.screenshot({
      path: testInfo.outputPath(`qa-erp-shell-${width}px.png`),
      fullPage: true,
    });
  }
});
