// Run against the built application at localhost:15422. No project dependency is required:
// set NEXTPLUS_PLAYWRIGHT_MODULE to an existing Playwright module when it is not on NODE_PATH.
// Screenshots/report go to NEXTPLUS_BROWSER_OUTPUT (default: ignored artifacts/browser-2b).
// Optional NEXTPLUS_TEST_EXPORT_DIRECTORY exercises adding/removing an empty fixture folder.
// NEXTPLUS_M3=1 adds persistent-cache checks and restarts the backend identified by
// NEXTPLUS_BACKEND_PID_FILE using NEXTPLUS_BACKEND_DLL. Keeps all Milestone 2B checks.
const { chromium } = require(process.env.NEXTPLUS_PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { spawn } = require('node:child_process');
const fsSync = require('node:fs');

(async () => {
  const output = path.resolve(process.env.NEXTPLUS_BROWSER_OUTPUT || 'artifacts/browser-2b');
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch(process.env.NEXTPLUS_CHROME_PATH
    ? { executablePath: process.env.NEXTPLUS_CHROME_PATH, headless: true } : { channel: 'chrome', headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.setDefaultTimeout(30000);
  const report = { consoleErrors: [], pageErrors: [], failedRequests: [], httpErrors: [], checks: [], screenshots: [] };
  const m3 = process.env.NEXTPLUS_M3 === '1';
  report.cacheLoads = []; report.cachePhases = [];
  page.on('console', message => { if (message.type() === 'error') report.consoleErrors.push(message.text()); });
  page.on('pageerror', error => report.pageErrors.push(error.message));
  page.on('requestfailed', request => report.failedRequests.push({ url: request.url(), error: request.failure()?.errorText }));
  page.on('response', response => { if (response.status() >= 400) report.httpErrors.push({ url: response.url(), status: response.status() }); });
  page.on('response', async response => {
    if (m3 && response.url().endsWith('/api/world-library')) {
      try { const data = await response.json(); for (const s of data.snapshots ?? []) if (s.cache && !report.cachePhases.includes(s.cache.state)) report.cachePhases.push(s.cache.state); } catch {}
    }
  });
  const loadResponse = () => page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/api/Bookmark/loadByFullPath'), { timeout: 240000 });
  const cacheLoad = async (response, expected) => {
    assert.equal(response.status(), 200); const headers = await response.allHeaders();
    report.cacheLoads.push({ cache: headers['x-nextplus-cache'], fullLoadMs: headers['x-nextplus-load-ms'] });
    assert.equal(headers['x-nextplus-cache'], expected);
  };
  const restartBackend = async () => {
    const pidFile = process.env.NEXTPLUS_BACKEND_PID_FILE;
    const dll = process.env.NEXTPLUS_BACKEND_DLL;
    assert(pidFile && dll, 'M3 requires an explicitly identified test backend');
    const previousPid = Number((await fs.readFile(pidFile, 'utf8')).trim());
    process.kill(previousPid);
    await new Promise(resolve => setTimeout(resolve, 1500));
    const log = fsSync.openSync(path.join(output, 'backend-restarted.log'), 'a');
    const backend = spawn('dotnet', [dll], { cwd: process.cwd(), windowsHide: true, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }, stdio: ['ignore', log, log] });
    backend.unref(); fsSync.closeSync(log);
    await fs.writeFile(pidFile, String(backend.pid));
    for (let i = 0; i < 60; i++) {
      try { if ((await fetch('http://localhost:15421/api/Bookmark')).ok) { report.backendRestart = { previousPid, newPid: backend.pid }; return; } } catch {}
      await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error('Backend did not restart');
  };
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
    if (m3 && await region.getByRole('button', { name: 'Remove cache', exact: true }).count()) {
      const removed = page.waitForResponse(r => r.url().endsWith('/cache') && r.request().method() === 'DELETE');
      await region.getByRole('button', { name: 'Remove cache', exact: true }).click(); assert.equal((await removed).status(), 200);
    }
    const load = page.waitForResponse(r => r.url().endsWith('/api/Bookmark/loadByFullPath'), { timeout: 180000 });
    await region.getByRole('button', { name: /^(Load|Reload) snapshot$/ }).click();
    if (m3) { await page.waitForTimeout(2000); await shot('m3-01-import-progress'); await cacheLoad(await load, 'MISS'); }
    else assert.equal((await load).status(), 200);
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
    if (m3) {
      await page.waitForLoadState('networkidle'); await restartBackend();
      const refreshed = refreshResponse(); await page.goto('http://localhost:15422'); assert.equal((await refreshed).status(), 200);
      region = page.locator('.v-expansion-panel').filter({ hasText: 'region2' }); await region.locator('.v-expansion-panel-title').click();
      assert.match(await region.innerText(), /Cached and ready/);
      const warm = loadResponse(); await region.getByRole('button', { name: /^(Load|Reload) snapshot$/ }).click();
      await page.waitForTimeout(2500); await shot('m3-02-restore-progress'); await cacheLoad(await warm, 'HIT');
      await region.getByRole('button', { name: 'Reload snapshot', exact: true }).waitFor({ timeout: 60000 });
      check('Complete backend restart restores region2 from persistent cache without XML parsing');
      for (const [route, label] of [['world','Overview'],['map','Map'],['hf','Characters'],['entity','Civilizations'],['artifact','Artifacts'],['war','Wars'],['battle','Battles'],['records','World Records']]) {
        await page.goto(`http://localhost:15422/${route}`); await page.waitForLoadState('networkidle');
        assert((await page.locator('main').innerText()).trim().length > 80, `Expected usable ${label} page`);
        if (route === 'map') await page.locator('#map .leaflet-image-layer').waitFor();
        await shot(`m3-restored-${route}`);
        const detail = page.locator(`main a[href^="/${route}/"]`).first();
        if (['hf','entity','artifact','war','battle'].includes(route) && await detail.count()) {
          await detail.click(); await page.waitForLoadState('networkidle'); await shot(`m3-detail-${route}`);
        }
        check(`${label} works after cache restoration`);
      }
    }
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
    if (m3) {
      const state = await (await fetch('http://localhost:15421/api/world-library')).json();
      const selected = state.snapshots.find(s => s.region === 'region2');
      assert(selected && /^[a-f0-9]{64}$/.test(selected.id));
      const cachePath = path.join(process.env.APPDATA, 'LegendsViewer-NextPlus', 'cache', `${selected.id}.lvwc`);
      await fs.writeFile(cachePath, Buffer.from('incomplete test cache'));
      const refreshed = refreshResponse(); await page.getByRole('button', { name: 'Rescan exports', exact: true }).click(); assert.equal((await refreshed).status(), 200);
      assert.match(await region.innerText(), /Cache invalid or unavailable/); await shot('m3-03-invalid-cache');
      const rebuild = loadResponse(); await region.getByRole('button', { name: 'Rebuild Cache', exact: true }).click();
      await page.waitForTimeout(2000); await shot('m3-03-rebuild-progress'); await cacheLoad(await rebuild, 'MISS');
      await region.getByRole('button', { name: 'Reload snapshot', exact: true }).waitFor({ timeout: 60000 });
      await region.getByText('Cached and ready', { exact: true }).waitFor();
      assert.match(await region.innerText(), /Cached and ready/); await shot('m3-04-cache-ready');
      const removed = page.waitForResponse(r => r.url().endsWith('/cache') && r.request().method() === 'DELETE');
      await region.getByRole('button', { name: 'Remove cache', exact: true }).click(); assert.equal((await removed).status(), 200);
      await region.getByText('Cache unavailable', { exact: true }).waitFor();
      assert.match(await region.innerText(), /Cache unavailable/);
      assert(await region.getByRole('button', { name: 'Reload snapshot', exact: true }).count(), 'Deleting cache must preserve the active bookmark');
      const recreate = loadResponse(); await region.getByRole('button', { name: 'Reload snapshot', exact: true }).click(); await cacheLoad(await recreate, 'MISS');
      await region.getByRole('button', { name: 'Reload snapshot', exact: true }).waitFor({ timeout: 60000 });
      check('Selected cache rebuild and removal work, preserving bookmarks and the active world');
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
