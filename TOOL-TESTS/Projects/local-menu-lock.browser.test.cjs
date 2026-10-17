// Real compiled React UI, synthetic native bridge only; no server/database/provider calls.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const dist = path.resolve(__dirname, '../../TOOL-LOCAL/Web/dist');
const origin = 'https://dashboard.app.local';
let browser;
before(async () => { browser = await chromium.launch({ channel: 'msedge', headless: true }); });
after(async () => { await browser?.close(); });

async function fixture(t, width = 1440) {
  const context = await browser.newContext({ viewport: { width, height: 1000 } });
  t.after(() => context.close());
  const page = await context.newPage(), errors = [], external = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(() => {
    const listeners = new Set(), personal = '00000000-0000-0000-0000-000000000000';
    const license = { hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 1,
      activeDeviceCount: 1, offlineGraceHours: 0, serverTimeUtc: '2026-10-15T00:00:00Z',
      heartbeatIntervalSeconds: 300, leaseExpiresAtUtc: '2030-10-15T00:15:00Z', accessState: 'Active',
      startsAtUtc: '2026-10-15T00:00:00Z', expiresAtUtc: '2030-10-15T00:00:00Z',
      planCode: 'LOCAL', planName: 'Gói Local', featureFlagsJson: '{"productScope":"vietsub","vietsubCloudEnabled":false}' };
    const state = { profile: { userId: 'fixture-user', email: 'fixture@example.test', displayName: 'Khách Local', accountStatus: 'Active', roles: [] },
      organizations: [], selectedOrganizationId: personal, projects: [], selectedProject: null, models: [], assetLibrary: null,
      providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
      mediaTools: { ready: true, message: 'Synthetic media fixture.', checkedAtUtc: '2026-10-15T00:00:00Z' },
      generationRunning: false, sceneFirstFrames: [],
      features: { vietsubEnabled: true, speechSynchronizationEnabled: false, tikTokEnabled: true }, license };
    const emit = message => listeners.forEach(listener => listener({ data: message }));
    window.menuFixture = { requests: [], state, emit, changePlan: flags => {
      state.license = { ...state.license, featureFlagsJson: flags };
      emit({ type: 'dashboard.state', payload: state });
    } };
    window.chrome = { webview: {
      addEventListener: (_, listener) => listeners.add(listener),
      removeEventListener: (_, listener) => listeners.delete(listener),
      postMessage: serialized => {
        const request = JSON.parse(serialized); window.menuFixture.requests.push(request);
        window.setTimeout(() => {
          if (request.type === 'app.ready') emit({ type: 'dashboard.state', requestId: request.requestId, payload: state });
          if (request.type === 'system.setup.get') emit({ type: 'system.setup.status', requestId: request.requestId, payload: {
            organizationId: personal, contextGeneration: 'fixture', revision: 1, startupRequired: true,
            components: ['media', 'ocr', 'qwen', 'piper'].map(id => ({ id, name: id, version: 'fixture', state: 'READY', canInstall: true, canVerify: true }))
          } });
          if (request.type === 'vietsub.state.get') emit({ type: 'vietsub.state', requestId: request.requestId,
            payload: { enabled: true, busy: false, projects: [], selectedProject: null, jobs: [] } });
        }, 0);
      }
    } };
  });
  await page.route('**/*', route => {
    const url = new URL(route.request().url());
    if (url.origin !== origin) { external.push(url.origin); return route.abort(); }
    const file = path.resolve(dist, url.pathname === '/' ? 'index.html' : url.pathname.slice(1));
    if (!file.startsWith(dist + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile())
      return route.fulfill({ status: 404, body: '' });
    const contentType = ({ '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' })[path.extname(file)] || 'application/octet-stream';
    return route.fulfill({ contentType, body: fs.readFileSync(file) });
  });
  await page.goto(origin);
  await page.waitForFunction(() => document.querySelector('.topbar h1')?.textContent === 'Dịch phụ đề'
    && !document.querySelector('.startup-setup-dialog')
    && !document.querySelector('.sidebar')?.hasAttribute('inert'));
  return { page, errors, external };
}

test('Local exposes only translation; locked menu clicks, Enter and Space cannot navigate or send tool commands', async t => {
  const { page, errors, external } = await fixture(t);
  const nav = page.locator('.sidebar-nav');
  assert.deepEqual(await nav.locator('button:not([aria-disabled="true"])').allTextContents(), ['Dịch phụ đề']);
  for (const locked of await page.locator('.sidebar button[aria-disabled="true"]').all()) {
    assert.match(await locked.getAttribute('title'), /Gói Local/);
    assert.equal(await locked.locator('.nav-lock-icon').count(), 1);
    await locked.scrollIntoViewIfNeeded();
    await locked.click({ force: true });
    await locked.focus(); await page.keyboard.press('Enter'); await page.keyboard.press('Space');
    assert.equal(await page.textContent('.topbar h1'), 'Dịch phụ đề');
  }
  const forbidden = await page.evaluate(() => window.menuFixture.requests.filter(x => /^(project\.|short-video\.|generation\.|providers\.|bilibili\.|tiktok\.|render\.|desktop\.settings\.|license\.payment\.)/.test(x.type)));
  assert.deepEqual(forbidden, []);
  await page.getByRole('button', { name: 'Thông tin gói', exact: true }).click();
  await page.locator('.license-information-dialog').waitFor();
  await page.getByRole('button', { name: 'Đóng thông tin gói', exact: true }).click();
  assert.equal(await page.getByRole('button', { name: 'Đăng xuất', exact: true }).isEnabled(), true);
  assert.deepEqual(errors, []); assert.deepEqual(external, []);
});

test('Local can check its translation runtime inside translation while the Settings menu stays locked', async t => {
  const { page, errors } = await fixture(t);
  const setup = page.locator('.local-translation-setup');
  await setup.locator('summary').click();
  await setup.getByRole('button', { name: 'Kiểm tra hệ thống', exact: true }).click();
  const request = await page.evaluate(() => window.menuFixture.requests.find(x => x.type === 'system.setup.check'));
  assert.equal(request.payload.expectedOrganizationId, '00000000-0000-0000-0000-000000000000');
  assert.equal(await page.locator('.sidebar-nav [aria-label="Cài đặt"]').getAttribute('aria-disabled'), 'true');
  assert.equal(await page.textContent('.topbar h1'), 'Dịch phụ đề');
  assert.deepEqual(errors, []);
});

test('effective plan changes update locks and return an open settings page to translation', async t => {
  const { page, errors } = await fixture(t);
  await page.evaluate(() => window.menuFixture.changePlan('{}'));
  const settings = page.locator('.sidebar-nav [aria-label="Cài đặt"]');
  await page.waitForFunction(() => !document.querySelector('.sidebar-nav [aria-label="Cài đặt"]').hasAttribute('aria-disabled'));
  await settings.click();
  await page.waitForFunction(() => document.querySelector('.topbar h1')?.textContent === 'Cài đặt');
  await page.evaluate(() => window.menuFixture.changePlan('{"productScope":"vietsub","vietsubCloudEnabled":false}'));
  await page.waitForFunction(() => document.querySelector('.topbar h1')?.textContent === 'Dịch phụ đề');
  assert.equal(await settings.getAttribute('aria-disabled'), 'true');
  assert.deepEqual(errors, []);
});

for (const width of [390, 1440]) test('Local locked navigation fits expanded and collapsed sidebar at ' + width, async t => {
  const { page, errors } = await fixture(t, width);
  if (width < 800) {
    await page.getByRole('button', { name: 'Mở menu', exact: true }).click();
    await page.waitForFunction(() => {
      const sidebar = document.querySelector('.sidebar.open');
      return sidebar && Math.abs(sidebar.getBoundingClientRect().left) < 1;
    });
  }
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
    fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `local-menu-${width}.png`), fullPage: true });
  }
  if (width > 800) {
    await page.getByRole('button', { name: 'Thu gọn thanh menu', exact: true }).click();
    assert.equal(await page.locator('.sidebar.collapsed').count(), 1);
    const locked = page.locator('.sidebar-nav [aria-label="Tạo Video Dài"]');
    assert.match(await locked.getAttribute('title'), /Gói Local/);
    const bounds = await locked.boundingBox(), icon = await locked.locator('.nav-lock-icon').boundingBox();
    assert.ok(icon.x >= bounds.x && icon.x + icon.width <= bounds.x + bounds.width);
  }
  assert.deepEqual(errors, []);
});
