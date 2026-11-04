import { expect, test, type Browser, type Page, type TestInfo } from '@playwright/test';
import { existsSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';

const qaErpUrl = process.env.QA_ERP_URL;
const authDirectory = process.env.PLAYWRIGHT_AUTH_DIR ?? join(homedir(), '.whatsbiz-playwright-auth');
const ownerStatePath = process.env.QA_OWNER_STORAGE_STATE ?? join(authDirectory, 'owner.json');
const retailerStatePath = process.env.QA_RETAILER_STORAGE_STATE ?? join(authDirectory, 'retailer.json');

async function createSessionContext(browser: Browser, statePath: string) {
  const context = await browser.newContext({ storageState: statePath });
  const sessionPath = statePath.replace(/\.json$/i, '.session.json');
  if (!existsSync(sessionPath)) return { context, sessionState: 'missing sessionStorage companion file' };
  try {
    const session = JSON.parse(await readFile(sessionPath, 'utf8')) as { origin?: string; authSession?: string };
    const expectedOrigin = qaErpUrl ? new URL(qaErpUrl).origin : '';
    if (session.origin !== expectedOrigin || typeof session.authSession !== 'string' || !session.authSession)
      return { context, sessionState: 'sessionStorage companion failed origin or content validation' };
    await context.addInitScript(({ origin, authSession }) => {
      if (window.location.origin === origin && !window.sessionStorage.getItem('whatsbiz.auth'))
        window.sessionStorage.setItem('whatsbiz.auth', authSession);
    }, { origin: session.origin, authSession: session.authSession });
    return { context, sessionState: 'restored for configured QA origin' };
  } catch {
    return { context, sessionState: 'sessionStorage companion could not be read' };
  }
}

function capturePageDiagnostics(page: Page) {
  const requests: Array<{ method: string; path: string; status?: number; failure?: string }> = [];
  const errors: string[] = [];
  const requestPaths = new WeakMap<object, { method: string; path: string }>();
  page.on('request', request => {
    const url = new URL(request.url());
    if (!url.pathname.startsWith('/api/')) return;
    requestPaths.set(request, { method: request.method(), path: url.pathname });
  });
  page.on('response', response => {
    const request = requestPaths.get(response.request());
    if (request) requests.push({ ...request, status: response.status() });
  });
  page.on('requestfailed', request => {
    const requestInfo = requestPaths.get(request);
    if (requestInfo) requests.push({ ...requestInfo, failure: (request.failure()?.errorText ?? 'network failure').slice(0, 120) });
  });
  page.on('pageerror', error => errors.push(`${error.name}: ${error.message}`.replace(/Bearer\s+\S+/gi, 'Bearer [redacted]').slice(0, 240)));
  page.on('console', message => {
    if (message.type() === 'error' && /api|auth|permission|feature|failed|error/i.test(message.text()))
      errors.push(message.text().replace(/Bearer\s+\S+/gi, 'Bearer [redacted]').replace(/https?:\/\/[^\s"']+/gi, '[url]').slice(0, 240));
  });
  return { requests, errors };
}

async function captureResponsive(page: Page, testInfo: TestInfo, label: string): Promise<void> {
  for (const width of [1440, 768, 390, 320]) {
    await page.setViewportSize({ width, height: width <= 390 ? 844 : 960 });
    const dimensions = await page.evaluate(() => ({
      viewport: window.innerWidth,
      content: document.documentElement.scrollWidth,
    }));
    expect(dimensions.content, `${label} has no horizontal overflow at ${width}px`)
      .toBeLessThanOrEqual(dimensions.viewport);
    await page.screenshot({ path: testInfo.outputPath(`${label}-${width}px.png`), fullPage: true });
  }
}

test('V74/V75 owner city and partner review screens render', async ({ browser }, testInfo) => {
  test.skip(!qaErpUrl || !existsSync(ownerStatePath),
    'Create or configure an authorized owner session with e2e/setup-qa-session.mjs owner.');

  const { context } = await createSessionContext(browser, ownerStatePath!);
  try {
    const page = await context.newPage();
    await page.goto(new URL('/application-owner/marketplace-cities', qaErpUrl!).toString());
    await expect(page.getByRole('heading', { name: 'City marketplaces' })).toBeVisible();
    await page.getByRole('button', { name: 'Add city' }).click();
    await expect(page.getByRole('dialog')).toContainText('Public URL slug');
    await captureResponsive(page, testInfo, 'owner-city-management');

    await page.goto(new URL('/application-owner/partner-verification', qaErpUrl!).toString());
    await expect(page.getByText('Pending partner product listings')).toBeVisible();
    await captureResponsive(page, testInfo, 'owner-partner-verification');
  } finally {
    await context.close();
  }
});

test('V74/V75 retailer city, partner seller and product attribution screens render', async ({ browser }, testInfo) => {
  test.skip(!qaErpUrl || !existsSync(retailerStatePath),
    'Create or configure an authorized retailer session with e2e/setup-qa-session.mjs retailer.');

  const { context, sessionState } = await createSessionContext(browser, retailerStatePath!);
  try {
    const page = await context.newPage();
    const diagnostics = capturePageDiagnostics(page);
    const requestedUrl = new URL('/admin/storefront/cities', qaErpUrl!).toString();
    await page.goto(requestedUrl);
    try {
      await expect(page.getByRole('heading', { name: 'City and service areas' })).toBeVisible();
    } catch {
      const pageState = await page.evaluate(() => ({
        title: document.title,
        headings: Array.from(document.querySelectorAll('h1,h2,h3')).map(node => node.textContent?.trim()).filter(Boolean).slice(0, 20),
      }));
      const report = {
        requestedUrl: new URL(requestedUrl).origin + new URL(requestedUrl).pathname,
        finalUrl: new URL(page.url()).origin + new URL(page.url()).pathname,
        title: pageState.title,
        headings: pageState.headings,
        retailerSession: sessionState,
        apiRequests: diagnostics.requests,
        pageAndConsoleErrors: diagnostics.errors,
      };
      await testInfo.attach('retailer-city-settings-diagnostics', { body: JSON.stringify(report, null, 2), contentType: 'application/json' });
      throw new Error(`Retailer City Settings did not render. Safe diagnostics: ${JSON.stringify(report)}`);
    }
    await captureResponsive(page, testInfo, 'retailer-city-settings');

    await page.goto(new URL('/admin/storefront/partner-sellers', qaErpUrl!).toString());
    await expect(page.getByRole('heading', { name: 'Partner sellers' })).toBeVisible();
    await page.getByRole('button', { name: 'Register seller' }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await captureResponsive(page, testInfo, 'retailer-partner-sellers');

    await page.goto(new URL('/products/new', qaErpUrl!).toString());
    await expect(page.getByText('Seller source')).toBeVisible();
    await page.getByRole('combobox', { name: 'Seller source' }).click();
    await page.getByRole('option', { name: 'Partner seller product' }).click();
    await expect(page.getByText('Verified partner seller')).toBeVisible();
    await captureResponsive(page, testInfo, 'retailer-product-attribution');
  } finally {
    await context.close();
  }
});

test('retailer session is denied Application Owner city administration', async ({ browser }) => {
  test.skip(!qaErpUrl || !existsSync(retailerStatePath),
    'Create or configure an authorized retailer session with e2e/setup-qa-session.mjs retailer.');

  const { context } = await createSessionContext(browser, retailerStatePath);
  try {
    const page = await context.newPage();
    await page.goto(new URL('/application-owner/marketplace-cities', qaErpUrl!).toString());
    await page.waitForLoadState('networkidle', { timeout: 10000 }).catch(() => undefined);
    await expect(page.getByRole('heading', { name: 'City marketplaces' })).toHaveCount(0);
  } finally {
    await context.close();
  }
});

test('Customer Storefront legacy retailer route renders', async ({ browser }, testInfo) => {
  const storefrontUrl = process.env.QA_STOREFRONT_URL;
  test.skip(!storefrontUrl, 'Set QA_STOREFRONT_URL to the authorized QA Customer Storefront base URL.');

  const page = await browser.newPage();
  const apiEvents: Array<Record<string, unknown>> = [];
  const pendingApiDetails: Promise<void>[] = [];
  const pageErrors: string[] = [];
  const consoleErrors: string[] = [];
  const requestStarts = new WeakMap<object, { path: string; method: string; startedAt: number; host: string }>();
  const sanitize = (value: string) => value
    .replace(/Bearer\s+\S+/gi, 'Bearer [redacted]')
    .replace(/https?:\/\/[^\s"']+/gi, '[url]')
    .slice(0, 300);
  page.on('request', request => {
    const url = new URL(request.url());
    if (!url.pathname.toLowerCase().includes('/api/')) return;
    requestStarts.set(request, { path: url.pathname, method: request.method(), startedAt: Date.now(), host: url.host });
  });
  page.on('response', response => {
    const started = requestStarts.get(response.request());
    if (!started) return;
    const entry: Record<string, unknown> = {
      host: started.host,
      method: started.method,
      path: started.path,
      status: response.status(),
      elapsedMs: Date.now() - started.startedAt,
      contentType: response.headers()['content-type']?.split(';')[0] ?? null,
    };
    if (response.status() >= 400) {
      pendingApiDetails.push(response.json().then(body => {
        if (body && typeof body === 'object') {
          const safe = body as Record<string, unknown>;
          for (const key of ['title', 'status', 'code', 'errorCode']) {
            const value = safe[key];
            if (typeof value === 'string' || typeof value === 'number') entry[key] = value;
          }
        }
      }).catch(() => undefined));
    }
    apiEvents.push(entry);
  });
  page.on('requestfailed', request => {
    const url = new URL(request.url());
    const started = requestStarts.get(request);
    if (started) apiEvents.push({
      host: started.host,
      method: started.method,
      path: started.path,
      failure: sanitize(request.failure()?.errorText ?? 'unknown network error'),
      elapsedMs: Date.now() - started.startedAt,
    });
  });
  page.on('pageerror', error => pageErrors.push(sanitize(`${error.name}: ${error.message}`)));
  page.on('console', message => {
    if (message.type() === 'error' && /api|catalog|product|categor|network|failed|error/i.test(message.text()))
      consoleErrors.push(sanitize(message.text()));
  });
  const response = await page.goto(new URL('/guturgo', storefrontUrl!).toString());
  expect(response).not.toBeNull();
  expect(response!.status()).toBeLessThan(500);
  await expect(page.getByRole('banner')).toBeVisible();
  await expect(page.getByRole('banner')).toContainText(/GuturGo/i);
  await expect(page.getByRole('main')).toBeVisible();
  await page.waitForLoadState('networkidle', { timeout: 12000 }).catch(() => undefined);
  await Promise.all(pendingApiDetails);
  await captureResponsive(page, testInfo, 'storefront-guturgo');
  await testInfo.attach('guturgo-store-api-errors.json', {
    body: JSON.stringify({ apiEvents, pageErrors, consoleErrors }, null, 2),
    contentType: 'application/json',
  });
  const productError = page.getByRole('alert').filter({ hasText: "We couldn't load the products" });
  if (await productError.count()) {
    throw new Error(`GuturGo catalog error state rendered. Sanitized browser diagnostics: ${JSON.stringify({ apiEvents, pageErrors, consoleErrors })}`);
  }
  const catalogResponses = apiEvents.filter(event =>
    /\/api\/store\/guturgo\/(products|categories)$/i.test(String(event.path ?? '')));
  expect(catalogResponses.filter(event => Number(event.status) === 200),
    'Both category and product catalog requests should return HTTP 200').toHaveLength(2);
  expect(apiEvents.filter(event => Number(event.status ?? 0) >= 400 || event.failure),
    'Storefront catalog-related API requests should not fail').toEqual([]);
  expect(pageErrors, 'Storefront page should not throw browser runtime errors').toEqual([]);
});

test('Customer Storefront underscore retailer route resolves its own store', async ({ page }) => {
  const storefrontUrl = process.env.QA_STOREFRONT_URL;
  test.skip(!storefrontUrl, 'Set QA_STOREFRONT_URL to the authorized QA Customer Storefront base URL.');

  const underscoreResponse = await page.goto(new URL('/arjn_gar', storefrontUrl!).toString());
  expect(underscoreResponse).not.toBeNull();
  expect(underscoreResponse!.status()).toBeLessThan(500);
  await expect(page.getByRole('banner')).toBeVisible();
  await expect(page.getByRole('banner')).not.toContainText(/GuturGo/i);
});

test('published city discovery route renders', async ({ page }, testInfo) => {
  const storefrontUrl = process.env.QA_STOREFRONT_URL;
  const citySlug = process.env.QA_PUBLISHED_CITY_SLUG;
  test.skip(!storefrontUrl || !citySlug,
    'Set QA_STOREFRONT_URL and QA_PUBLISHED_CITY_SLUG for a currently published QA city.');

  const response = await page.goto(new URL(`/${encodeURIComponent(citySlug!)}`, storefrontUrl!).toString());
  expect(response).not.toBeNull();
  expect(response!.status()).toBeLessThan(500);
  await expect(page.getByRole('main')).toBeVisible();
  await captureResponsive(page, testInfo, 'storefront-city-marketplace');
});
