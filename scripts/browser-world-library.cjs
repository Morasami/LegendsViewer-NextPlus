// Run against the built application at localhost:15422. No project dependency is required:
// set NEXTPLUS_PLAYWRIGHT_MODULE to an existing Playwright module when it is not on NODE_PATH.
// Screenshots/report go to NEXTPLUS_BROWSER_OUTPUT (default: ignored artifacts/browser-2b).
// Optional NEXTPLUS_TEST_EXPORT_DIRECTORY exercises adding/removing an empty fixture folder.
const { chromium } = require(process.env.NEXTPLUS_PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');

(async () => {
  const output = path.resolve(process.env.NEXTPLUS_BROWSER_OUTPUT || 'artifacts/browser-2b');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch(process.env.NEXTPLUS_CHROME_PATH
    ? { executablePath: process.env.NEXTPLUS_CHROME_PATH, headless: true } : { channel: 'chrome', headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(30000);
  const report = { consoleErrors: [], pageErrors: [], failedRequests: [], httpErrors: [], checks: [], screenshots: [] };
  page.on('console', message => { if (message.type() === 'error') report.consoleErrors.push(message.text()); });
  page.on('pageerror', error => report.pageErrors.push(error.message));
  page.on('requestfailed', request => report.failedRequests.push({ url: request.url(), error: request.failure()?.errorText }));
  page.on('response', response => { if (response.status() >= 400) report.httpErrors.push({ url: response.url(), status: response.status() }); });
  const check = message => { report.checks.push(message); console.log('PASS', message); };
  const shot = async name => {
    // Let Vuetify transitions and chart animations settle before visual inspection.
    await page.waitForTimeout(1100);
    await page.screenshot({ path: path.join(output, name + '.png'), fullPage: !name.includes('narrow'), animations: 'disabled' });
    report.screenshots.push(name + '.png');
  };
  const noOverflow = async () => assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'Page has horizontal overflow');
  const refreshResponse = () => page.waitForResponse(r => r.url().endsWith('/api/world-library/refresh') && r.request().method() === 'POST');
  const libraryPage = async () => {
    const refreshed = refreshResponse();
    await page.getByRole('link', { name: 'Explore Worlds', exact: true }).click();
    assert.equal((await refreshed).status(), 200);
    await page.getByRole('button', { name: 'Rescan exports', exact: true }).waitFor();
  };
  try {
    const initial = refreshResponse();
    await page.goto('http://localhost:15422');
    assert.equal((await initial).status(), 200);
    await page.locator('.v-expansion-panel').first().waitFor();
    await noOverflow();
    check('Explore Worlds automatically discovers real exports');
    await shot('01-library');
    let region = page.locator('.v-expansion-panel').filter({ hasText: 'region2' });
    assert.equal(await region.count(), 1, 'Expected region2 export in the game installation');
    await region.locator('.v-expansion-panel-title').click();
    await region.getByRole('button', { name: /^(Load|Reload) snapshot$/ }).waitFor();
    await shot('02-expanded-snapshots');
    await region.locator('summary').click();
    assert.match(await region.innerText(), /5\/6 available/);
    await shot('03-companion-status');
    check('Snapshot expands and shows all six companion states without requiring BMP');

    await page.getByRole('button', { name: 'Export directories', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Absolute export directory').fill(process.env.NEXTPLUS_GAME_PATH || 'D:\\Steam\\steamapps\\common\\Dwarf Fortress');
    const browse = page.waitForResponse(r => r.url().includes('/api/game-settings/directories'));
    await dialog.getByRole('button', { name: 'Browse export folders' }).click();
    assert.equal((await browse).status(), 200);
    await dialog.getByRole('button', { name: 'Use this export folder' }).waitFor();
    await shot('04-directory-configuration');
    check('Export directory settings browse actual server folders');
    const fixture = process.env.NEXTPLUS_TEST_EXPORT_DIRECTORY;
    if (fixture) {
      await dialog.getByLabel('Absolute export directory').fill(fixture);
      let saved = page.waitForResponse(r => r.url().endsWith('/api/world-library/directories') && r.request().method() === 'PUT');
      await dialog.getByRole('button', { name: 'Add directory', exact: true }).click();
      const savedResponse = await saved;
      assert.equal(savedResponse.status(), 200);
      const normalized = (await savedResponse.json()).exportDirectories.find(p => p.endsWith(path.basename(fixture)));
      assert(normalized, 'Expected the persisted, normalized fixture path');
      await dialog.getByRole('button', { name: `Remove ${normalized}`, exact: true }).waitFor();
      await dialog.getByRole('button', { name: 'Close', exact: true }).click();
      const reloaded = refreshResponse(); await page.reload(); assert.equal((await reloaded).status(), 200);
      await page.getByRole('button', { name: 'Export directories', exact: true }).click();
      saved = page.waitForResponse(r => r.url().endsWith('/api/world-library/directories') && r.request().method() === 'PUT');
      await dialog.getByRole('button', { name: `Remove ${normalized}`, exact: true }).click();
      assert.equal((await saved).status(), 200);
      check('Additional directory persists across page reload and can be removed');
    }
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    const rescanned = refreshResponse(); await page.getByRole('button', { name: 'Rescan exports', exact: true }).click();
    assert.equal((await rescanned).status(), 200); check('Manual rescan succeeds');

    region = page.locator('.v-expansion-panel').filter({ hasText: 'region2' });
    if (await region.getByRole('button', { name: /^(Load|Reload) snapshot$/ }).count() === 0)
      await region.locator('.v-expansion-panel-title').click();
    const load = page.waitForResponse(r => r.url().endsWith('/api/Bookmark/loadByFullPath'), { timeout: 180000 });
    await region.getByRole('button', { name: /^(Load|Reload) snapshot$/ }).click();
    assert.equal((await load).status(), 200);
    await region.getByRole('button', { name: 'Reload snapshot', exact: true }).waitFor({ timeout: 60000 });
    await shot('05-loaded-library'); check('region2 loads through the existing parser and bookmark path');
    await page.getByRole('link', { name: 'Overview', exact: true }).click();
    await page.waitForURL('**/world');
    await page.locator('canvas').first().waitFor();
    await page.waitForLoadState('networkidle');
    assert.match(await page.locator('main').innerText(), /Anethadef/);
    assert((await page.locator('canvas').count()) > 0, 'Expected world overview charts');
    await shot('06-loaded-overview'); check('World overview, population sections and charts render');
    await page.getByRole('link', { name: 'Map', exact: true }).click();
    await page.waitForURL('**/map');
    await page.locator('#map .leaflet-image-layer').waitFor();
    await page.waitForLoadState('networkidle');
    assert((await page.locator('.leaflet-marker-icon').count()) > 0, 'Expected site icons');
    const layers = page.locator('.leaflet-control-layers');
    await layers.hover();
    const warfare = layers.getByText('Wars & Battles', { exact: true });
    await warfare.click(); await warfare.click();
    await page.mouse.move(500, 400);
    await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
    await shot('07-premium-map'); check('Premium map, site icons, zoom and Wars & Battles layer controls work');
    await libraryPage();
    const second = page.locator('.v-expansion-panel').filter({ hasText: 'region3' });
    if (await second.count() === 1) {
      await second.locator('.v-expansion-panel-title').click();
      const switchLoad = page.waitForResponse(r => r.url().endsWith('/api/Bookmark/loadByFullPath'), { timeout: 180000 });
      await second.getByRole('button', { name: 'Load snapshot', exact: true }).click();
      assert.equal((await switchLoad).status(), 200);
      await second.getByRole('button', { name: 'Reload snapshot', exact: true }).waitFor({ timeout: 60000 });
      check('Explicitly switching to the region3 snapshot succeeds');
      region = page.locator('.v-expansion-panel').filter({ hasText: 'region2' });
      await region.locator('.v-expansion-panel-title').click();
      const back = page.waitForResponse(r => r.url().endsWith('/api/Bookmark/loadByFullPath'), { timeout: 180000 });
      await region.getByRole('button', { name: 'Load snapshot', exact: true }).click();
      assert.equal((await back).status(), 200);
      await region.getByRole('button', { name: 'Reload snapshot', exact: true }).waitFor({ timeout: 60000 });
      check('Switching back to region2 preserves its bookmark and selected snapshot');
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(500);
    await page.evaluate(() => scrollTo(0, 0));
    assert(await page.locator('main').evaluate(e => e.getBoundingClientRect().width > 300), 'Narrow content is squeezed by navigation');
    await noOverflow(); await shot('08-narrow-library');
    await page.getByRole('button', { name: 'Export directories', exact: true }).click();
    await shot('09-narrow-directories'); await noOverflow();
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    await page.getByRole('button', { name: 'Toggle navigation', exact: true }).click();
    await page.getByRole('link', { name: 'Map', exact: true }).click();
    await page.waitForURL('**/map');
    await page.locator('#map .leaflet-image-layer').waitFor();
    await shot('11-narrow-map');
    await page.getByRole('button', { name: 'Toggle navigation', exact: true }).click();
    await libraryPage();
    check('Narrow library and directory dialog have no horizontal page overflow');
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.getByRole('button', { name: 'Select', exact: true }).click();
    assert.match(await dialog.innerText(), /region2-00184-01-01-legends.xml/);
    await shot('10-manual-selector');
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    check('Manual selector still lists exports and existing bookmark cards remain available');
    assert.deepEqual(report.pageErrors, []); assert.deepEqual(report.consoleErrors, []);
    assert.deepEqual(report.httpErrors, []); assert.deepEqual(report.failedRequests, []);
    check('No page errors, console errors, failed requests or HTTP errors');
  } catch (error) {
    report.error = error.stack; await shot('failure').catch(() => {}); throw error;
  } finally {
    await fs.writeFile(path.join(output, 'browser-report.json'), JSON.stringify(report, null, 2));
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
