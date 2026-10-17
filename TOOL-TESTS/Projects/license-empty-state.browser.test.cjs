// Uses the current production web bundle with a fake native bridge; no API,
// database, license mutation or real payment is involved.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const repo = path.resolve(__dirname, '../..');
const dist = path.join(repo, 'TOOL-LOCAL/Web/dist');
const origin = 'https://dashboard.app.local';
let browser;

before(async () => { browser = await chromium.launch({ channel: 'msedge', headless: true }); });
after(async () => { await browser?.close(); });

async function openFixture(mode = 'unavailable', viewport = { width: 1440, height: 900 }) {
  const context = await browser.newContext({ viewport });
  const page = await context.newPage();
  const pageErrors = [];
  const externalRequests = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  await page.addInitScript(({ fixtureMode }) => {
    const listeners = new Set();
    const fixture = { mode: fixtureMode, automatic: true, requests: [],
      emit: message => listeners.forEach(listener => listener({ data: message })) };
    window.licenseFixture = fixture;
    const license = { hasActiveLicense: false, currentDeviceActivated: false, maxActivatedDevices: 1,
      activeDeviceCount: 0, offlineGraceHours: 0, serverTimeUtc: '2026-10-13T00:00:00Z',
      heartbeatIntervalSeconds: 300, leaseExpiresAtUtc: null, accessState: 'Missing',
      accessMessage: 'Tài khoản chưa có gói sử dụng.' };
    const dashboard = { profile: { userId: 'fixture-user', email: 'fixture@example.test',
      displayName: 'Khách hàng', accountStatus: 'Active', roles: [] }, organizations: [],
      selectedOrganizationId: '', projects: [], selectedProject: null, models: [], assetLibrary: null,
      providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
      mediaTools: { ready: false, message: 'Fixture media.', checkedAtUtc: '' },
      generationRunning: false, sceneFirstFrames: [],
      features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false }, license };
    window.chrome = { webview: {
      addEventListener: (_type, listener) => listeners.add(listener),
      removeEventListener: (_type, listener) => listeners.delete(listener),
      postMessage: serialized => {
        const request = JSON.parse(serialized);
        fixture.requests.push(request);
        window.setTimeout(() => {
          if (request.type === 'app.ready') fixture.emit({ type: 'dashboard.state', requestId: request.requestId, payload: dashboard });
          if (request.type === 'license.payment.current.get') fixture.emit({ type: 'license.payment.current', requestId: request.requestId, payload: { payment: null } });
          if (request.type === 'license.offers.get' && fixture.automatic) {
            if (fixture.mode === 'empty') fixture.emit({ type: 'license.offers', requestId: request.requestId, payload: [] });
            else fixture.emit({ type: 'operation.error', requestId: request.requestId,
              error: { code: fixture.mode === 'network' ? 'network_error' : 'payments_unavailable',
                message: fixture.mode === 'network' ? 'Không thể kết nối. Vui lòng thử lại.' : 'Thanh toán đang tạm ngừng. Vui lòng thử lại sau.' } });
          }
        }, 0);
      }
    } };
  }, { fixtureMode: mode });
  await page.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.origin !== origin) {
      externalRequests.push(url.origin);
      return route.abort();
    }
    const relative = url.pathname === '/' ? 'index.html' : url.pathname.slice(1);
    const file = path.resolve(dist, relative);
    if (!file.startsWith(dist + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
      return route.fulfill({ status: 404, body: '' });
    }
    const contentType = ({ '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css',
      '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' })[path.extname(file)] || 'application/octet-stream';
    return route.fulfill({ contentType, body: fs.readFileSync(file) });
  });
  await page.goto(origin + '/');
  await page.locator('.license-gate-card').waitFor();
  return { context, page, pageErrors, externalRequests };
}

test('friendly unavailable state fits small and desktop viewports and keeps keyboard focus in the license gate', async () => {
  const { context, page, pageErrors, externalRequests } = await openFixture();
  try {
    const gate = page.locator('.license-gate-card');
    await page.getByRole('heading', { name: 'Hiện chưa có gói sử dụng', exact: true }).waitFor();
    assert.equal(await gate.getByRole('alert').count(), 0);
    assert.equal(await gate.locator('.license-plan-option').count(), 0);
    assert.equal(await gate.getByText('Thanh toán đang tạm ngừng.', { exact: false }).count(), 0);
    for (const size of [{ width: 320, height: 740 }, { width: 768, height: 1024 }, { width: 1440, height: 900 }]) {
      await page.setViewportSize(size);
      const bounds = await gate.boundingBox();
      assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= size.width + 1);
      assert.ok(await gate.evaluate(element => element.scrollWidth <= element.clientWidth + 1), 'modal does not overflow horizontally');
      const check = gate.getByRole('button', { name: 'Kiểm tra lại', exact: true });
      assert.ok(await check.isVisible());
      assert.ok(await gate.getByRole('button', { name: 'Đăng xuất', exact: true }).isEnabled());
      if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
        fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
        await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'license-unavailable-' + size.width + '.png') });
      }
    }
    await gate.getByRole('button', { name: 'Kiểm tra lại', exact: true }).focus();
    await page.keyboard.press('Tab');
    assert.equal(await page.evaluate(() => document.activeElement.textContent.trim()), 'Đăng xuất');
    await page.keyboard.press('Shift+Tab');
    assert.equal(await page.evaluate(() => document.activeElement.textContent.trim()), 'Kiểm tra lại');
    await page.keyboard.press('Escape');
    assert.ok(await gate.isVisible());
    assert.equal(await page.evaluate(() => window.licenseFixture.requests.filter(request => request.type === 'license.payment.create').length), 0);
    assert.deepEqual(pageErrors, []);
    assert.deepEqual(externalRequests, []);
  } finally { await context.close(); }
});

test('empty success is terminal and retry shows real offers without bypassing the license gate', async () => {
  const { context, page, pageErrors, externalRequests } = await openFixture('empty');
  try {
    await page.getByRole('heading', { name: 'Hiện chưa có gói sử dụng', exact: true }).waitFor();
    await page.evaluate(() => { window.licenseFixture.automatic = false; });
    await page.getByRole('button', { name: 'Kiểm tra lại', exact: true }).click();
    await page.getByRole('heading', { name: 'Đang tải các gói sử dụng', exact: true }).waitFor();
    assert.ok(await page.getByRole('button', { name: 'Đang kiểm tra…', exact: true }).isDisabled());
    assert.equal(await page.evaluate(() => window.licenseFixture.requests.filter(request => request.type === 'license.offers.get').length), 2);
    await page.evaluate(() => {
      const request = window.licenseFixture.requests.filter(item => item.type === 'license.offers.get').at(-1);
      window.licenseFixture.emit({ type: 'license.offers', requestId: request.requestId, payload: [{
        licensePlanId: 'fixture-plan', planCode: 'MONTH', name: 'Gói 30 ngày', priceVnd: 199000,
        durationDays: 30, maxActivatedDevices: 1, marketingFeatures: ['Xuất video'], displayOrder: 1,
        organizationSeatAvailable: true, organizationPoolName: 'Fixture pool', availableOrganizationSeats: 1 }] });
    });
    await page.getByRole('button', { name: 'Chọn gói này', exact: true }).waitFor();
    assert.ok(await page.locator('.license-gate-card').isVisible());
    assert.equal(await page.locator('.license-plan-option').count(), 1);
    assert.equal(await page.evaluate(() => window.licenseFixture.requests.filter(request => request.type === 'license.payment.create').length), 0);
    assert.deepEqual(pageErrors, []);
    assert.deepEqual(externalRequests, []);
  } finally { await context.close(); }
});

test('network failure stays an error and becomes a neutral state only after a valid empty response', async () => {
  const { context, page, pageErrors, externalRequests } = await openFixture('network');
  try {
    await page.getByRole('heading', { name: 'Chưa thể tải danh sách gói', exact: true }).waitFor();
    assert.equal(await page.locator('.license-gate-card [role="alert"]').count(), 1);
    assert.equal(await page.getByRole('heading', { name: 'Hiện chưa có gói sử dụng', exact: true }).count(), 0);
    await page.evaluate(() => { window.licenseFixture.mode = 'empty'; });
    await page.getByRole('button', { name: 'Kiểm tra lại', exact: true }).click();
    await page.getByRole('heading', { name: 'Hiện chưa có gói sử dụng', exact: true }).waitFor();
    assert.equal(await page.locator('.license-gate-card [role="alert"]').count(), 0);
    assert.deepEqual(pageErrors, []);
    assert.deepEqual(externalRequests, []);
  } finally { await context.close(); }
});
