import { chromium } from '@playwright/test';
import { createInterface } from 'node:readline/promises';
import { stdin, stdout } from 'node:process';
import { existsSync } from 'node:fs';
import { mkdir, writeFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join, resolve } from 'node:path';

const role = process.argv[2]?.toLowerCase();
if (!['owner', 'retailer'].includes(role ?? '')) {
  throw new Error('Usage: node e2e/setup-qa-session.mjs <owner|retailer>');
}

const qaUrl = process.env.QA_ERP_URL?.trim() || 'https://qa.khatadhari.com';
let parsedQaUrl;
try {
  parsedQaUrl = new URL(qaUrl);
} catch {
  throw new Error('QA_ERP_URL must be a valid HTTPS URL on an authorized KhataDhari QA host.');
}
const authorizedQaHost = /^(?:qa|qa-[a-z0-9-]+)\.khatadhari\.com$/i.test(parsedQaUrl.hostname);
if (parsedQaUrl.protocol !== 'https:' || !authorizedQaHost || parsedQaUrl.username || parsedQaUrl.password || parsedQaUrl.search || parsedQaUrl.hash)
  throw new Error('QA_ERP_URL must use HTTPS on an authorized KhataDhari QA host and contain no credentials or query data.');
const qaBaseUrl = new URL('/', parsedQaUrl.origin);
const loginUrlSetting = process.env[role === 'owner' ? 'QA_OWNER_URL' : 'QA_RETAILER_URL'];
let loginUrl;
try {
  loginUrl = new URL(loginUrlSetting ?? (role === 'owner' ? '/application-owner/login' : '/login'), qaBaseUrl);
} catch {
  throw new Error(`The ${role} login URL is malformed.`);
}
if (loginUrl.protocol !== 'https:' || loginUrl.origin !== qaBaseUrl.origin || loginUrl.username || loginUrl.password || loginUrl.search || loginUrl.hash)
  throw new Error(`The ${role} login URL must use the configured QA ERP origin and contain no credentials or query data.`);
const authDirectory = resolve(process.env.PLAYWRIGHT_AUTH_DIR ?? join(homedir(), '.whatsbiz-playwright-auth'));
const statePath = join(authDirectory, `${role}.json`);
const sessionPath = join(authDirectory, `${role}.session.json`);
const route = role === 'owner' ? '/application-owner/marketplace-cities' : '/profile';
const protectedUrl = new URL(route, qaBaseUrl);
const protectedApiPath = role === 'owner' ? '/api/marketplace/owner/cities' : '/api/auth/me';
const heading = 'City marketplaces';
const loginUrlString = loginUrl.href;
const protectedUrlString = protectedUrl.href;

if (process.argv.includes('--check-urls')) {
  console.log(JSON.stringify({ role, loginUrl: loginUrlString, protectedUrl: protectedUrlString }));
  process.exit(0);
}

const prompt = createInterface({ input: stdin, output: stdout });

try {
  if (existsSync(statePath)) {
    const overwrite = (await prompt.question(`Replace the existing ${role} QA browser state file? [y/N] `)).trim().toLowerCase();
    if (overwrite !== 'y' && overwrite !== 'yes') {
      console.log('Existing state was retained.');
      process.exitCode = 0;
    } else {
      await saveSession();
    }
  } else {
    await saveSession();
  }
} finally {
  prompt.close();
}

async function saveSession() {
  const browser = await chromium.launch({ headless: false });
  try {
    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto(loginUrlString, { waitUntil: 'domcontentloaded' });
    console.log(`QA ${role} sign-in page opened: ${loginUrl.origin}${loginUrl.pathname}`);
    console.log(`Sign in with the authorized QA ${role} account in the opened browser.`);
    await prompt.question('After the expected QA workspace appears, press Enter here to validate the role and save its session: ');
    const sessionScope = await page.evaluate(() => {
      try {
        const value = JSON.parse(sessionStorage.getItem('whatsbiz.auth') ?? 'null');
        return {
          accessToken: typeof value?.accessToken === 'string' ? value.accessToken : null,
          roles: Array.isArray(value?.user?.roles) ? value.user.roles : [],
          tenantId: value?.user?.tenantId ?? null,
          expiresOnUtc: typeof value?.expiresOnUtc === 'string' ? value.expiresOnUtc : null,
        };
      } catch {
        return null;
      }
    });
    if (!sessionScope?.accessToken)
      throw new Error('No valid WhatsBiz authentication session was found; no browser state was saved.');
    const expiresOn = sessionScope.expiresOnUtc ? Date.parse(sessionScope.expiresOnUtc) : NaN;
    if (!Number.isFinite(expiresOn) || expiresOn <= Date.now())
      throw new Error(`QA ${role} authentication session is missing or expired; no browser state was saved.`);
    const hasOwnerRole = sessionScope.roles.includes('ApplicationOwner');
    // The server AuthResponse omits AccountType; its authentication service enforces
    // ApplicationOwner role with a null TenantId only for APPLICATION_OWNER accounts.
    const correctScope = role === 'owner'
      ? hasOwnerRole && sessionScope.tenantId === null
      : !hasOwnerRole && typeof sessionScope.tenantId === 'string' &&
        /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(sessionScope.tenantId) &&
        sessionScope.roles.length > 0;
    if (!correctScope)
      throw new Error(`Authenticated account scope is not valid for the requested ${role} session; no browser state was saved.`);
    let apiOrigin;
    try {
      const runtimeResponse = await page.request.get(new URL('/runtime-config.json', qaBaseUrl).href, { timeout: 15000 });
      if (!runtimeResponse.ok())
        throw new Error(`HTTP ${runtimeResponse.status()}`);
      const runtimeConfig = await runtimeResponse.json();
      apiOrigin = new URL(runtimeConfig?.apiBaseUrl);
      if (apiOrigin.protocol !== 'https:' || !/^(?:qa|qa-[a-z0-9-]+)\.khatadhari\.com$/i.test(apiOrigin.hostname) || apiOrigin.username || apiOrigin.password || apiOrigin.pathname !== '/')
        throw new Error('untrusted QA API origin');
    } catch (error) {
      const detail = error instanceof Error && /^HTTP \d+$/.test(error.message) ? ` (${error.message})` : '';
      throw new Error(`Protected API origin validation failed${detail}; no browser state was saved.`);
    }
    const protectedApiResponse = await page.request.get(new URL(protectedApiPath, apiOrigin).href, {
      timeout: 15000,
      headers: { Authorization: `Bearer ${sessionScope.accessToken}` },
    }).catch(() => null);
    if (!protectedApiResponse)
      throw new Error(`Protected API verification failed (request error); no browser state was saved.`);
    if (protectedApiResponse.status() !== 200)
      throw new Error(`Protected API verification failed (HTTP ${protectedApiResponse.status()}); no browser state was saved.`);

    if (role === 'retailer') {
      let serverUser;
      try {
        serverUser = await protectedApiResponse.json();
      } catch {
        throw new Error('Retailer identity verification failed (invalid /api/auth/me response); no browser state was saved.');
      }
      const serverRoles = Array.isArray(serverUser?.roles) ? serverUser.roles : [];
      const serverTenantId = serverUser?.tenantId;
      if (serverRoles.includes('ApplicationOwner') || !serverRoles.length || !serverTenantId || serverTenantId === '00000000-0000-0000-0000-000000000000')
        throw new Error('Retailer identity verification failed (server response did not contain a retailer tenant scope); no browser state was saved.');
      if (serverTenantId.toLowerCase() !== sessionScope.tenantId.toLowerCase() || serverRoles.includes('ApplicationOwner') !== hasOwnerRole || !sessionScope.roles.every(roleName => serverRoles.includes(roleName)))
        throw new Error('Retailer identity verification failed (server identity did not match the active session); no browser state was saved.');
      await page.goto(protectedUrlString, { waitUntil: 'domcontentloaded' });
      const expectedPath = protectedUrl.pathname.replace(/\/+$/, '');
      const actualPath = new URL(page.url()).pathname.replace(/\/+$/, '');
      if (actualPath !== expectedPath)
        throw new Error('Retailer protected-workspace verification failed (redirected away); no browser state was saved.');
    } else {
      await page.goto(protectedUrlString, { waitUntil: 'domcontentloaded' });
      const headingLocator = page.getByRole('heading', { name: heading });
      try {
        await headingLocator.waitFor({ state: 'visible', timeout: 15000 });
      } catch {
        throw new Error('Owner protected-workspace verification failed; no browser state was saved.');
      }
      const expectedPath = protectedUrl.pathname.replace(/\/+$/, '');
      const actualPath = new URL(page.url()).pathname.replace(/\/+$/, '');
      if (actualPath !== expectedPath)
        throw new Error('Owner protected-workspace verification failed (redirected away); no browser state was saved.');
    }
    await mkdir(authDirectory, { recursive: true });
    const authSession = await page.evaluate(() => sessionStorage.getItem('whatsbiz.auth'));
    if (!authSession)
      throw new Error('Authenticated browser session disappeared before it could be saved; no session state was saved.');
    await context.storageState({ path: statePath });
    await writeFile(sessionPath, JSON.stringify({ origin: qaBaseUrl.origin, authSession }), { encoding: 'utf8', mode: 0o600 });
    console.log(`Validated ${role} role. Browser state saved outside the repository at: ${statePath}`);
    await context.close();
  } finally {
    await browser.close();
  }
}
