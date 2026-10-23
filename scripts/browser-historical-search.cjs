// Milestone 4A browser validation against the built application at localhost:15422.
// Set NODE_PATH or NEXTPLUS_PLAYWRIGHT_MODULE to an existing Playwright installation.
// Screenshots and report are written to NEXTPLUS_BROWSER_OUTPUT (default: ignored artifacts/browser-4a).
const { chromium } = require(process.env.NEXTPLUS_PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const output = path.resolve(process.env.NEXTPLUS_BROWSER_OUTPUT || 'artifacts/browser-4a');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch(process.env.NEXTPLUS_CHROME_PATH
    ? { executablePath: process.env.NEXTPLUS_CHROME_PATH, headless: true } : { channel: 'chrome', headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(60000);
  const report = {
    checks: [], screenshots: [], consoleErrors: [], pageErrors: [], failedRequests: [], httpErrors: [],
    abortedSearchRequests: [], searchResponses: [], worldLoads: [],
  };
  page.on('console', message => { if (message.type() === 'error') report.consoleErrors.push(message.text()); });
  page.on('pageerror', error => report.pageErrors.push(error.message));
  page.on('requestfailed', request => {
    const failure = request.failure()?.errorText ?? '';
    if (request.url().includes('/api/Search') && failure === 'net::ERR_ABORTED') {
      report.abortedSearchRequests.push(request.url());
      return;
    }
    report.failedRequests.push({ url: request.url(), error: failure });
  });
  page.on('response', response => {
    if (response.status() >= 400) report.httpErrors.push({ url: response.url(), status: response.status() });
    if (new URL(response.url()).pathname === '/api/Search' && response.status() === 200) {
      response.json().then(data => report.searchResponses.push({
        q: new URL(response.url()).searchParams.get('q'),
        totalCount: data.totalCount,
        generation: data.generation,
        page: data.page,
        pageSize: data.pageSize,
        categories: new URL(response.url()).searchParams.get('categories'),
        truncated: data.truncated,
      })).catch(() => {});
    }
  });
  const check = message => { report.checks.push(message); console.log('PASS', message); };
  const shot = async (name, fullPage = true) => {
    await page.waitForTimeout(700);
    await page.screenshot({ path: path.join(output, name + '.png'), fullPage, animations: 'disabled' });
    report.screenshots.push(name + '.png');
  };
  const noOverflow = async label => {
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),
      `Horizontal overflow at ${label}`);
  };
  const searchField = () => page.getByLabel('Search historical records', { exact: true });
  const waitForSearch = (q, extra = () => true) => page.waitForResponse(response => {
    const url = new URL(response.url());
    return url.pathname === '/api/Search' && response.status() === 200 && url.searchParams.get('q') === q && extra(url);
  }, { timeout: 120000 });
  const runSearch = async (query, { categories, fromYear, toYear } = {}) => {
    const responsePromise = waitForSearch(query);
    await searchField().fill(query);
    const response = await responsePromise;
    assert.equal(response.status(), 200);
    await page.waitForTimeout(450);
    return response;
  };
  const clickCategory = async (label, expectedParam) => {
    const responsePromise = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname === '/api/Search' && response.status() === 200 &&
        (url.searchParams.get('categories') ?? '').includes(expectedParam);
    }, { timeout: 120000 });
    await page.locator('.filter-row .v-chip').filter({ hasText: label }).first().click();
    return responsePromise;
  };
  const allCategories = async () => {
    await page.locator('.filter-row .v-chip').filter({ hasText: 'All categories' }).first().click();
    await page.waitForTimeout(500);
  };
  const cards = () => page.locator('.result-card');
  const firstResultHref = async () => cards().first().locator('a.result-link').first().getAttribute('href');
  const loadWorld = async (regionName) => {
    if (new URL(page.url()).pathname !== '/') {
      const refreshed = page.waitForResponse(response => response.url().endsWith('/api/world-library/refresh'), { timeout: 120000 });
      await page.getByRole('link', { name: 'Explore Worlds', exact: true }).click();
      assert.equal((await refreshed).status(), 200);
    }
    const region = page.locator('.v-expansion-panel').filter({ hasText: regionName }).first();
    await region.waitFor();
    if (await region.getByRole('button', { name: /^(Load|Reload) snapshot$/ }).count() === 0) {
      await region.locator('.v-expansion-panel-title').click();
    }
    const loadPromise = page.waitForResponse(response => response.url().endsWith('/api/Bookmark/loadByFullPath'), { timeout: 300000 });
    await region.getByRole('button', { name: /^(Load|Reload) snapshot$/ }).first().click();
    const loadResponse = await loadPromise;
    assert.equal(loadResponse.status(), 200);
    const headers = await loadResponse.allHeaders();
    report.worldLoads.push({ region: regionName, cache: headers['x-nextplus-cache'], loadMs: headers['x-nextplus-load-ms'] });
    await region.getByRole('button', { name: 'Reload snapshot', exact: true }).waitFor({ timeout: 120000 });
    await page.waitForTimeout(500);
    return headers;
  };
  const openSearch = async () => {
    const metadata = page.waitForResponse(response => response.url().endsWith('/api/Search/metadata'), { timeout: 120000 });
    await page.getByRole('link', { name: 'Historical Search', exact: true }).click();
    await page.waitForURL('**/search');
    assert.equal((await metadata).status(), 200);
    await searchField().waitFor();
  };

  try {
    const initialRefresh = page.waitForResponse(response => response.url().endsWith('/api/world-library/refresh'), { timeout: 120000 });
    await page.goto('http://localhost:15422');
    assert.equal((await initialRefresh).status(), 200);
    await page.locator('.v-expansion-panel').first().waitFor();
    await noOverflow('world library');
    await shot('01-world-library');
    check('World Library loads and discovers exports');

    const region2Headers = await loadWorld('region2');
    check('region2 loads through the World Library' + (region2Headers['x-nextplus-cache'] ? ` (${region2Headers['x-nextplus-cache']})` : ''));
    await shot('02-region2-loaded');

    await openSearch();
    await noOverflow('search initial');
    assert.match(await page.locator('main').innerText(), /Anethadef/i);
    await shot('03-search-initial');
    check('Historical Search opens and reports the loaded region2 world');

    const uristResponse = await runSearch('Urist');
    const uristData = await uristResponse.json();
    assert(uristData.totalCount > 25, 'Expected more than one page of Urist matches');
    assert((await cards().count()) > 0);
    assert.match(await cards().first().innerText(), /Urist/);
    await shot('04-figures-urist');
    check(`Figure search returns ${uristData.totalCount} Urist matches`);

    const page2Promise = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname === '/api/Search' && response.status() === 200 && url.searchParams.get('page') === '2';
    }, { timeout: 120000 });
    await page.locator('.v-pagination button').filter({ hasText: /^2$/ }).first().click();
    assert.equal((await page2Promise).status(), 200);
    await page.waitForTimeout(500);
    assert.match(await page.locator('.results-heading').innerText(), /26–50/);
    await shot('05-pagination-page-2');
    check('Pagination requests and displays the second result page');

    const yearPromise = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname === '/api/Search' && response.status() === 200 &&
        url.searchParams.get('q') === 'Urist' &&
        url.searchParams.get('fromYear') === '80' && url.searchParams.get('toYear') === '90';
    }, { timeout: 120000 });
    const yearFields = page.locator('.year-field input');
    await yearFields.nth(0).fill('80');
    await yearFields.nth(1).fill('90');
    const yearResponse = await yearPromise;
    const yearData = await yearResponse.json();
    assert(yearData.totalCount > 0, 'Expected matches in the year range');
    assert(yearData.items.every(item => item.year >= 80 && item.year <= 90), 'Year filter returned out-of-range records');
    await page.waitForTimeout(400);
    await shot('05b-year-filter');
    check('Year range filters results to records with dates in range');

    const clearYearPromise = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname === '/api/Search' && response.status() === 200 &&
        url.searchParams.get('q') === 'Urist' && !url.searchParams.has('fromYear');
    }, { timeout: 120000 });
    await page.getByRole('button', { name: /Clear filters/ }).click();
    await clearYearPromise;
    await page.waitForTimeout(400);

    await allCategories();
    const figuresResponse = await clickCategory('Figures', 'Figure');
    assert.equal(figuresResponse.status(), 200);
    await page.waitForTimeout(400);
    assert((await page.locator('.result-card').filter({ hasText: 'Historical figures' }).count()) === await cards().count());
    await shot('06-category-figures');
    check('Category filter restricts results to historical figures');

    await allCategories();
    await runSearch('war');
    const warsResponse = await clickCategory('Wars', 'War');
    assert.equal(warsResponse.status(), 200);
    await page.waitForTimeout(400);
    assert((await cards().count()) > 0);
    assert((await page.locator('.result-card').filter({ hasText: 'Wars' }).count()) === await cards().count());
    await shot('07-category-wars');
    check('Wars category is searchable and filterable');

    await allCategories();
    await runSearch('battle');
    const battlesResponse = await clickCategory('Battles', 'Battle');
    assert.equal(battlesResponse.status(), 200);
    await page.waitForTimeout(400);
    assert((await cards().count()) > 0);
    assert((await page.locator('.result-card').filter({ hasText: 'Battles' }).count()) === await cards().count());
    await shot('08-category-battles');
    check('Battles category is searchable and filterable');

    await allCategories();
    await runSearch('artifact created');
    const eventsResponse = await clickCategory('Events', 'Event');
    assert.equal(eventsResponse.status(), 200);
    await page.waitForTimeout(400);
    assert((await cards().count()) > 0);
    assert((await page.locator('.result-card').filter({ hasText: 'Events' }).count()) === await cards().count());
    await shot('09-events');
    check('Individual events are searchable by canonical event metadata');

    await allCategories();
    await runSearch('fortress');
    const sitesResponse = await clickCategory('Sites', 'Site');
    assert.equal(sitesResponse.status(), 200);
    await page.waitForTimeout(400);
    assert((await cards().count()) > 0);
    assert((await page.locator('.result-card').filter({ hasText: 'Sites' }).count()) === await cards().count());
    await shot('10-sites');
    check('Sites are searchable through site records');

    await allCategories();
    await runSearch('civilization');
    const entitiesResponse = await clickCategory('Civilizations', 'Entity');
    assert.equal(entitiesResponse.status(), 200);
    await page.waitForTimeout(400);
    assert((await cards().count()) > 0);
    assert((await page.locator('.result-card').filter({ hasText: 'Civilizations & entities' }).count()) === await cards().count());
    await shot('11-entities');
    check('Civilizations and other entities are searchable');

    await allCategories();
    await runSearch('the');
    const artifactsResponse = await clickCategory('Artifacts', 'Artifact');
    assert.equal(artifactsResponse.status(), 200);
    await page.waitForTimeout(400);
    assert((await cards().count()) > 0);
    assert((await page.locator('.result-card').filter({ hasText: 'Artifacts' }).count()) === await cards().count());
    await shot('12-artifacts');
    check('Artifacts are searchable');

    await allCategories();
    await runSearch('Urist');
    const href = await firstResultHref();
    assert.match(href, /^\/hf\/\d+$/);
    await cards().first().locator('a.result-link').first().click();
    await page.waitForURL('**/hf/**');
    await page.locator('main').getByText('Profile Overview', { exact: true }).waitFor();
    await page.waitForLoadState('networkidle');
    assert.match(await page.locator('main').innerText(), /Urist Graybust/);
    await shot('13-figure-detail');
    check('A search result navigates to the existing historical figure detail page');

    await page.goBack();
    await page.waitForURL('**/search**');
    await searchField().waitFor();
    await page.waitForTimeout(800);
    await shot('14-search-returned');
    check('Browser back returns to the persisted search page');

    const region3Headers = await loadWorld('region3');
    check('region3 loads through the World Library after switching' + (region3Headers['x-nextplus-cache'] ? ` (${region3Headers['x-nextplus-cache']})` : ''));
    await openSearch();
    assert.match(await page.locator('main').innerText(), /Sil Kodor/i);
    const region3Search = await runSearch('Urist');
    const region3Data = await region3Search.json();
    assert(region3Data.totalCount > 0);
    assert(region3Data.generation > 0);
    assert((await page.locator('main').innerText()).includes(String(region3Data.generation)));
    assert.match(await page.locator('main').innerText(), /Sil Kodor/i);
    await shot('15-region3-switch');
    check('Switching worlds replaces the search index and shows region3 results, not stale region2 data');

    const artifactsAfterSwitch = await runSearch('artifact created');
    const afterData = await artifactsAfterSwitch.json();
    assert(afterData.totalCount > 0);
    await shot('16-region3-events');
    check('Search remains functional after a world switch');

    await page.setViewportSize({ width: 390, height: 844 });
    const narrowSearch = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname === '/api/Search' && response.status() === 200 && url.searchParams.get('q') === 'Urist';
    }, { timeout: 120000 });
    await page.goto('http://localhost:15422/search?q=Urist');
    await narrowSearch;
    await searchField().waitFor();
    await page.waitForTimeout(600);
    await noOverflow('narrow search');
    assert((await cards().count()) > 0);
    await shot('17-narrow-search', false);
    await page.locator('.filter-row').scrollIntoViewIfNeeded();
    await shot('18-narrow-filters', false);
    check('Narrow layout has no horizontal overflow and keeps search results readable');

    const metadata = await page.evaluate(async () => (await fetch('http://localhost:15421/api/Search/metadata')).json());
    report.metadata = metadata;
    assert.equal(metadata.worldLoaded, true);
    check('Search metadata identifies the active world generation');

    assert.deepEqual(report.consoleErrors, [], 'Unexpected browser console errors');
    assert.deepEqual(report.pageErrors, [], 'Unexpected browser page errors');
    assert.deepEqual(report.failedRequests, [], 'Unexpected failed network requests');
    assert.deepEqual(report.httpErrors, [], 'Unexpected HTTP error responses');
    check('No browser console errors, page errors, failed requests, or HTTP errors');
  } catch (error) {
    report.failure = error.stack || String(error);
    console.error('FAIL', error);
    try { await shot('99-failure'); } catch {}
  } finally {
    await fs.writeFile(path.join(output, 'report.json'), JSON.stringify(report, null, 2));
    await browser.close();
  }
  if (report.failure) process.exit(1);
})().catch(error => { console.error(error); process.exit(1); });
