// Production React bundle with a fake native bridge. All requests stay in this
// fixture; no server, bank transaction, webhook, database or provider is used.
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

async function fixture(t, mode = 'fulfilled') {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  t.after(() => context.close());
  const page = await context.newPage();
  const errors = [];
  const externalRequests = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.addInitScript(({ fixtureMode }) => {
    const listeners = new Set();
    const state = { mode: fixtureMode, requests: [], emit: message => {
      for (const listener of listeners) listener({ data: message });
    } };
    window.paymentFixture = state;
    const license = { hasActiveLicense: false, currentDeviceActivated: false, maxActivatedDevices: 1,
      activeDeviceCount: 0, offlineGraceHours: 0, serverTimeUtc: '2026-10-15T00:00:00Z',
      heartbeatIntervalSeconds: 300, leaseExpiresAtUtc: null, accessState: 'Missing' };
    const dashboard = { profile: { userId: 'fixture-user', email: 'fixture@example.test', displayName: 'Khách hàng',
      accountStatus: 'Active', roles: [] }, organizations: [], selectedOrganizationId: '', projects: [],
      selectedProject: null, models: [], assetLibrary: null,
      providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
      mediaTools: { ready: false, message: 'Fixture media.', checkedAtUtc: '' }, generationRunning: false,
      sceneFirstFrames: [], features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false }, license };
    const checkout = { orderCode: 'VMO9F2A87C31D60', transferCode: 'VM123456', planCode: 'VIETSUB_MONTH',
      planName: 'Vietsub Local', durationDays: 30, amountVnd: 70000, receiverBankCode: 'VCB',
      receiverAccountNumber: '11111111', receiverAccountName: 'TEST HOLDER', transferContent: 'VM123456',
      qrImageUrl: '/fixture-qr.svg', status: 'Pending', createdAtUtc: '2026-10-15T00:00:00Z',
      expiresAtUtc: '2026-10-15T00:15:00Z', serverTimeUtc: '2026-10-15T00:00:00Z',
      reusedExistingPayment: true, isPaid: false, isFulfilled: false, isExpired: false };
    window.chrome = { webview: {
      addEventListener: (_type, listener) => listeners.add(listener),
      removeEventListener: (_type, listener) => listeners.delete(listener),
      postMessage: serialized => {
        const request = JSON.parse(serialized);
        state.requests.push(request);
        window.setTimeout(() => {
          const reply = (type, payload) => state.emit({ type, requestId: request.requestId, payload });
          if (request.type === 'app.ready') reply('dashboard.state', dashboard);
          if (request.type === 'license.offers.get') reply('license.offers', []);
          if (request.type === 'license.payment.current.get') reply('license.payment.current', { payment: checkout });
          if (request.type === 'license.payment.status') {
            const scheduled = state.mode === 'scheduled';
            const paid = state.mode !== 'pending';
            reply('license.payment.status', { ...checkout, status: paid ? 'Fulfilled' : 'Pending',
              isPaid: paid, isFulfilled: paid, isScheduled: scheduled,
              startsAtUtc: '2026-10-15T00:00:00Z', licenseExpiresAtUtc: '2026-11-14T00:00:00Z' });
            if (!paid || scheduled) return;
            if (state.mode === 'activation-error') {
              state.emit({ type: 'operation.error', requestId: request.requestId,
                error: { code: 'license_activation_pending', message: 'Đã nhận thanh toán. Chưa thể kích hoạt thiết bị, vui lòng kiểm tra lại.' } });
              return;
            }
            const active = { ...license, hasActiveLicense: true, currentDeviceActivated: true, accessState: 'Active',
              planCode: checkout.planCode, planName: checkout.planName,
              startsAtUtc: '2026-10-15T00:00:00Z', expiresAtUtc: '2026-11-14T00:00:00Z' };
            reply('license.activated', active);
            reply('dashboard.state', { ...dashboard, license: active });
          }
        }, 0);
      }
    } };
  }, { fixtureMode: mode });
  await page.route('**/*', route => {
    const url = new URL(route.request().url());
    if (url.origin !== origin) { externalRequests.push(url.origin); return route.abort(); }
    if (url.pathname === '/fixture-qr.svg') return route.fulfill({ contentType: 'image/svg+xml',
      body: '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="white"/></svg>' });
    const file = path.resolve(dist, url.pathname === '/' ? 'index.html' : url.pathname.slice(1));
    if (!file.startsWith(dist + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile())
      return route.fulfill({ status: 404, body: '' });
    return route.fulfill({ body: fs.readFileSync(file), contentType: ({ '.html': 'text/html', '.js': 'text/javascript',
      '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' })[path.extname(file)] || 'application/octet-stream' });
  });
  await page.goto(origin + '/');
  await page.locator('.license-checkout-view').waitFor();
  await page.getByRole('button', { name: 'Tôi đã thanh toán', exact: true }).click();
  return { page, errors, externalRequests };
}

test('success receipt fits desktop, narrow and landscape viewports and waits for explicit entry', async t => {
  const { page, errors, externalRequests } = await fixture(t);
  const dialog = page.getByRole('dialog', { name: 'Thanh toán thành công!' });
  await dialog.waitFor();
  await dialog.getByRole('button', { name: 'Vào ứng dụng', exact: true }).waitFor();
  assert.ok(await dialog.getByText('70.000 đ', { exact: true }).isVisible());
  assert.ok(await dialog.getByText('Vietsub Local', { exact: true }).isVisible());
  for (const viewport of [{ width: 1440, height: 900 }, { width: 390, height: 844 },
    { width: 320, height: 740 }, { width: 900, height: 500 }]) {
    await page.setViewportSize(viewport);
    const bounds = await dialog.boundingBox();
    assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= viewport.width + 1);
    assert.ok(bounds.y >= 0 && bounds.y + bounds.height <= viewport.height + 1);
    assert.ok(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1));
    assert.equal(await page.locator('.app-main').getAttribute('inert'), '');
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
      fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
      await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR,
        `payment-success-${viewport.width}x${viewport.height}.png`) });
    }
  }
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 1440, height: 900 });
  const enter = dialog.getByRole('button', { name: 'Vào ứng dụng', exact: true });
  await enter.focus();
  await page.keyboard.press('Escape');
  assert.ok(await dialog.isVisible());
  for (const key of ['Tab', 'Shift+Tab']) {
    await page.keyboard.press(key);
    assert.equal(await enter.evaluate(element => element === document.activeElement), true);
  }
  await page.mouse.click(8, 8);
  assert.ok(await dialog.isVisible());
  await enter.click();
  await dialog.waitFor({ state: 'detached' });
  assert.equal(await page.locator('.app-main').getAttribute('inert'), null);
  assert.equal(await page.locator('.license-gate-card').count(), 0);
  assert.equal(await page.evaluate(() => window.paymentFixture.requests.filter(r => r.type === 'license.payment.create').length), 0);
  assert.deepEqual(errors, []); assert.deepEqual(externalRequests, []);
});

test('activation failure keeps the receipt and supports retry without a new payment', async t => {
  const { page, errors, externalRequests } = await fixture(t, 'activation-error');
  const dialog = page.getByRole('dialog', { name: 'Thanh toán thành công!' });
  await dialog.getByRole('alert').waitFor();
  assert.equal(await dialog.getByRole('button', { name: 'Vào ứng dụng', exact: true }).count(), 0);
  await page.evaluate(() => { window.paymentFixture.mode = 'fulfilled'; });
  await dialog.getByRole('button', { name: 'Kiểm tra kích hoạt', exact: true }).click();
  await dialog.getByRole('button', { name: 'Vào ứng dụng', exact: true }).waitFor();
  assert.ok(await dialog.isVisible());
  assert.equal(await page.locator('.app-main').getAttribute('inert'), '');
  assert.equal(await page.evaluate(() => window.paymentFixture.requests.filter(r => r.type === 'license.payment.create').length), 0);
  assert.deepEqual(errors, []); assert.deepEqual(externalRequests, []);
});

test('scheduled fulfillment keeps its existing access gate and does not promise immediate entry', async t => {
  const { page, errors, externalRequests } = await fixture(t, 'scheduled');
  await page.getByRole('button', { name: 'Đã xếp lịch sử dụng', exact: true }).waitFor();
  assert.equal(await page.locator('.payment-success-dialog').count(), 0);
  assert.equal(await page.getByRole('button', { name: 'Vào ứng dụng', exact: true }).count(), 0);
  assert.deepEqual(errors, []); assert.deepEqual(externalRequests, []);
});
