// Run with VIDEOMAKER_PLAYWRIGHT_MODULE pointing to an installed Playwright package.
// All API calls are in-memory fakes; no app server, database or TikTok is contacted.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const repo = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(repo, name), 'utf8');
const razor = read('TOOL-SERVER/Pages/Admin/Index.cshtml');
const html = razor.slice(razor.indexOf('<!doctype')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
const base = { adminManagementEnabled: true, integrationEnabled: false, auditedForPublicPosting: false, auditEvidence: null, requiredRedirectUri: 'http://127.0.0.1:*/callback/', scopes: ['video.publish'], connectedUserCount: 0, pendingPublishJobCount: 0, credentials: [] };
const active = { credentialId: 'active-1', version: 1, status: 'Active', clientKeyHint: 'demo****key1', secretHint: '****demo', createdAtUtc: '2026-09-09T00:00:00Z', activatedAtUtc: '2026-09-09T01:00:00Z' };
let browser;
before(async () => { browser = await chromium.launch({ headless: true }); });
after(async () => { await browser?.close(); });

async function fixture(t, state = base, width = 1440) {
  const context = await browser.newContext({ viewport: { width, height: 1100 }, timezoneId: 'Asia/Ho_Chi_Minh' });
  t.after(() => context.close());
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  t.after(() => assert.deepEqual(errors, [], 'browser must not raise unhandled errors'));
  await page.route('**/*', route => route.abort());
  await page.setContent(html);
  await page.addStyleTag({ content: read('TOOL-SERVER/wwwroot/admin/admin.css') });
  await page.clock.install();
  await page.evaluate(seed => {
    document.getElementById('loginScreen').classList.add('hidden');
    document.getElementById('adminShell').classList.remove('hidden');
    document.getElementById('pageTitle').textContent = 'Tích hợp TikTok';
    document.getElementById('breadcrumbCurrent').textContent = 'Tích hợp TikTok';
    document.getElementById('manageUsersShortcut').classList.add('hidden');
    document.getElementById('pageEyebrow').textContent = 'SYSTEM INTEGRATION';
    document.getElementById('pageSubtitle').textContent = 'Quản lý TikTok Developer App, kiểm tra OAuth và giới hạn đăng công khai.';
    document.querySelectorAll('[data-view]').forEach(element => element.classList.toggle('active', element.dataset.view === 'tiktok'));
    document.querySelectorAll('[data-panel]').forEach(element => element.classList.toggle('hidden', element.dataset.panel !== 'tiktok'));
    document.querySelectorAll('[data-close]').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
    window.fixture = { state: seed, calls: [], holdRead: false, holdWrite: false, failRead: false, failWrite: false };
    window.videoMakerAdminShell = {
      state: { accessToken: 'synthetic-test-session', currentView: 'tiktok' },
      escapeHtml: value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character])),
      formatDate: value => value ? new Date(value).toLocaleString('vi-VN') : '—',
      icon: name => `<svg class="ui-icon" aria-hidden="true"><use href="#icon-${name}"></use></svg>`,
      toast: () => {},
      showLogin: () => { window.videoMakerTikTokAdmin.reset(); window.videoMakerAdminShell.state.accessToken = null; },
      api: async (url, options = {}) => {
        const f = window.fixture, method = options.method || 'GET';
        f.calls.push({ url, method, body: options.body ? JSON.parse(options.body) : null });
        if (method === 'GET') {
          const result = structuredClone(f.state);
          if (f.holdRead) await new Promise(resolve => { f.releaseRead = resolve; });
          if (f.failRead) throw new Error('synthetic connection failure');
          return result;
        }
        if (f.holdWrite) await new Promise(resolve => { f.releaseWrite = resolve; });
        if (f.failWrite) throw new Error('synthetic connection failure');
        if (method === 'PUT') {
          const body = JSON.parse(options.body);
          Object.assign(f.state, { integrationEnabled: body.enabled, auditedForPublicPosting: body.auditedForPublicPosting, auditEvidence: body.auditEvidence });
        } else if (url.endsWith('/verification')) {
          Object.assign(f.state.credentials.find(item => item.status === 'Pending'), { verificationRequestedByCurrentAdmin: true, verificationExpiresAtUtc: new Date(Date.now() + 900000).toISOString() });
          f.state.integrationEnabled = false;
        } else if (method === 'POST') {
          f.state.credentials = f.state.credentials.map(item => item.status === 'Pending' ? { ...item, status: 'Revoked' } : item);
          f.state.credentials.push({ credentialId: 'pending-new', version: 2, status: 'Pending', clientKeyHint: 'demo****key2', secretHint: '****demo', createdAtUtc: new Date().toISOString() });
        } else if (method === 'DELETE') f.state.credentials = f.state.credentials.map(item => item.status === 'Pending' ? { ...item, status: 'Revoked' } : item);
        return structuredClone(f.state);
      }
    };
  }, structuredClone(state));
  for (const file of ['admin-tiktok-state.js', 'admin-tiktok.js']) await page.addScriptTag({ content: read(`TOOL-SERVER/wwwroot/admin/${file}`) });
  await page.evaluate(() => window.videoMakerTikTokAdmin.activate());
  await page.waitForSelector('#tiktokNextAction');
  return page;
}

test('setup validates fields, clears secrets before sending and prevents duplicate writes', async t => {
  const page = await fixture(t);
  await page.click('#tiktokNextAction');
  await page.click('#tiktokCredentialForm button[type=submit]');
  assert.equal(await page.evaluate(() => window.fixture.calls.filter(c => c.method !== 'GET').length), 0);
  assert.match(await page.textContent('#tiktokClientKeyError'), /Nhập Client Key/);
  await page.fill('#tiktokClientKey', 'synthetic-client-key');
  await page.fill('#tiktokClientSecret', 'synthetic-client-secret');
  await page.evaluate(() => { window.fixture.holdWrite = true; document.getElementById('tiktokCredentialForm').requestSubmit(); document.getElementById('tiktokCredentialForm').requestSubmit(); });
  assert.equal(await page.inputValue('#tiktokClientSecret'), '');
  assert.equal(await page.inputValue('#tiktokClientKey'), '');
  assert.equal(await page.evaluate(() => window.fixture.calls.filter(c => c.method === 'POST').length), 1);
  await page.evaluate(() => { window.fixture.holdWrite = false; window.fixture.releaseWrite(); });
  await page.waitForFunction(() => !document.getElementById('tiktokCredentialDialog').open);
  assert.match(await page.textContent('#tiktokStatusTitle'), /cần xác minh/);
  await page.click('#tiktokNextAction');
  assert.equal(await page.evaluate(() => window.fixture.calls.filter(c => c.url.endsWith('/verification')).length), 0);
  await page.keyboard.press('Escape');
  assert.equal(await page.evaluate(() => document.getElementById('tiktokConfirmDialog').open), false);
  await page.click('#tiktokNextAction');
  await page.click('#tiktokConfirmAction');
  await page.waitForFunction(() => document.getElementById('tiktokStatusTitle').textContent.includes('chờ bạn'));
  assert.equal(await page.evaluate(() => window.fixture.calls.filter(c => c.url.endsWith('/verification')).length), 1);
});

test('verification retains its full 15-minute UTC deadline after refresh in Vietnam time', async t => {
  const page = await fixture(t, { ...base, credentials: [{ ...active, status: 'Pending' }] });
  await page.clock.setFixedTime(new Date('2026-09-09T05:00:00Z'));
  await page.click('#tiktokNextAction');
  await page.click('#tiktokConfirmAction');
  await page.waitForFunction(() => document.getElementById('tiktokStatusTitle').textContent.includes('chờ bạn'));
  assert.equal(await page.evaluate(() => new Date().getTimezoneOffset()), -420);
  assert.equal(await page.evaluate(() => window.fixture.state.credentials[0].verificationExpiresAtUtc), '2026-09-09T05:15:00.000Z');
  await page.evaluate(() => window.videoMakerTikTokAdmin.refresh());
  assert.equal(await page.evaluate(() => window.videoMakerTikTokState.describe(window.fixture.state).remaining), 900);
  assert.match(await page.textContent('#tiktokStatusTitle'), /chờ bạn/);
  await page.clock.setFixedTime(new Date('2026-09-09T05:14:59Z'));
  await page.evaluate(() => window.videoMakerTikTokAdmin.refresh());
  assert.match(await page.textContent('#tiktokStatusTitle'), /chờ bạn/);
  await page.clock.setFixedTime(new Date('2026-09-09T05:15:00Z'));
  await page.evaluate(() => window.videoMakerTikTokAdmin.refresh());
  assert.match(await page.textContent('#tiktokStatusTitle'), /hết hạn/);
});

test('background refresh preserves edited evidence and focus; concurrent server policy changes block save', async t => {
  const page = await fixture(t, { ...base, integrationEnabled: true, credentials: [active] });
  await page.check('#tiktokAudited');
  assert.equal(await page.isChecked('#tiktokAuditConfirm'), false);
  await page.fill('#tiktokAuditEvidence', 'ticket-draft-123');
  await page.evaluate(() => window.videoMakerTikTokAdmin.refresh());
  assert.equal(await page.inputValue('#tiktokAuditEvidence'), 'ticket-draft-123');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'tiktokAuditEvidence');
  await page.click('#tiktokSaveSettings');
  assert.match(await page.textContent('#tiktokConfirmError'), /xác nhận/);
  assert.equal(await page.evaluate(() => window.fixture.calls.filter(c => c.method === 'PUT').length), 0);
  await page.evaluate(async () => { window.fixture.state.integrationEnabled = false; await window.videoMakerTikTokAdmin.refresh(); });
  assert.equal(await page.inputValue('#tiktokAuditEvidence'), 'ticket-draft-123');
  assert.equal(await page.isDisabled('#tiktokSaveSettings'), true);
  assert.match(await page.textContent('#tiktokDirtyHint'), /server đã đổi/);
  await page.click('#tiktokResetSettings');
  await page.click('#tiktokConfirmAction');
  await page.waitForFunction(() => !document.getElementById('tiktokIntegrationEnabled').checked);
  assert.equal(await page.isChecked('#tiktokIntegrationEnabled'), false);
  assert.equal(await page.inputValue('#tiktokAuditEvidence'), '');
});

test('verification polls read-only, recognizes activation and stops polling after leaving TikTok', async t => {
  const pending = { ...active, status: 'Pending', verificationRequestedByCurrentAdmin: true, verificationExpiresAtUtc: new Date(Date.now() + 900000).toISOString() };
  const page = await fixture(t, { ...base, credentials: [pending] });
  assert.match(await page.textContent('#tiktokStatusTitle'), /chờ bạn/);
  await page.evaluate(() => { window.fixture.state.integrationEnabled = true; window.fixture.state.credentials[0].status = 'Active'; });
  await page.clock.fastForward(15000);
  await page.waitForFunction(() => document.getElementById('tiktokStatusTitle').textContent.includes('đang hoạt động'));
  assert.match(await page.textContent('#tiktokNotice'), /Xác minh thành công/);
  assert.equal(await page.evaluate(() => window.fixture.calls.some(c => c.method !== 'GET')), false);
  await page.evaluate(() => { window.videoMakerTikTokAdmin.deactivate(); document.querySelector('[data-panel=tiktok]').classList.add('hidden'); });
  const count = await page.evaluate(() => window.fixture.calls.length);
  await page.clock.fastForward(60000);
  assert.equal(await page.evaluate(() => window.fixture.calls.length), count);
});

test('old GET response cannot overwrite a completed credential write', async t => {
  const page = await fixture(t);
  await page.click('#tiktokNextAction');
  await page.fill('#tiktokClientKey', 'synthetic-client-key');
  await page.fill('#tiktokClientSecret', 'synthetic-client-secret');
  await page.evaluate(() => { window.fixture.holdRead = true; void window.videoMakerTikTokAdmin.refresh(); });
  await page.click('#tiktokCredentialForm button[type=submit]');
  await page.waitForFunction(() => !document.getElementById('tiktokCredentialDialog').open);
  await page.evaluate(() => window.fixture.releaseRead());
  await page.waitForFunction(() => document.getElementById('tiktokAdminConsole').getAttribute('aria-busy') === 'false');
  assert.match(await page.textContent('#tiktokStatusTitle'), /cần xác minh/);
});

test('reset clears pending secrets and discards a previous Admin response', async t => {
  const page = await fixture(t);
  await page.click('#tiktokNextAction');
  await page.fill('#tiktokClientSecret', 'synthetic-client-secret');
  await page.evaluate(() => { window.fixture.holdRead = true; void window.videoMakerTikTokAdmin.refresh(); });
  await page.evaluate(() => { window.videoMakerTikTokAdmin.reset(); window.fixture.releaseRead(); });
  assert.equal(await page.inputValue('#tiktokClientSecret'), '');
  assert.equal(await page.evaluate(() => document.getElementById('tiktokCredentialDialog').open), false);
  assert.equal(await page.locator('#tiktokStatusTitle').count(), 0);
});

test('responsive layout, keyboard dialogs and reduced motion remain usable', async t => {
  const page = await fixture(t, { ...base, integrationEnabled: true, credentials: [active] });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  for (const width of [375, 768, 1024, 1440]) {
    await page.setViewportSize({ width, height: 1100 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true, `no horizontal overflow at ${width}`);
  }
  await page.setViewportSize({ width: 375, height: 812 });
  await page.click('#addTikTokCredentialButton');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'tiktokClientKey');
  await page.keyboard.press('Escape');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'addTikTokCredentialButton');
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
    fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'tiktok-admin-mobile.png'), fullPage: true });
    await page.setViewportSize({ width: 1440, height: 1100 });
    await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'tiktok-admin-desktop.png'), fullPage: true });
  }
});

test('public posting requires fresh approval and disabling waits for explicit confirmation', async t => {
  const page = await fixture(t, { ...base, integrationEnabled: true, credentials: [active] });
  await page.check('#tiktokAudited');
  await page.fill('#tiktokAuditEvidence', 'synthetic-review-123');
  await page.check('#tiktokAuditConfirm');
  await page.click('#tiktokSaveSettings');
  await page.waitForFunction(() => document.getElementById('tiktokDirtyHint').textContent === 'Đã đồng bộ');
  assert.equal(await page.isChecked('#tiktokAuditConfirm'), false);
  const saved = await page.evaluate(() => window.fixture.calls.find(c => c.method === 'PUT').body);
  assert.deepEqual(saved, { enabled: true, auditedForPublicPosting: true, confirmAuditApproval: true, auditEvidence: 'synthetic-review-123' });
  await page.uncheck('#tiktokIntegrationEnabled');
  await page.click('#tiktokSaveSettings');
  assert.equal(await page.evaluate(() => window.fixture.calls.filter(c => c.method === 'PUT').length), 1);
  await page.click('#tiktokConfirmAction');
  await page.waitForFunction(() => window.fixture.calls.filter(c => c.method === 'PUT').length === 2);
  assert.equal(await page.evaluate(() => window.fixture.state.integrationEnabled), false);
  assert.equal(await page.evaluate(() => window.fixture.state.auditedForPublicPosting), false);
});

test('failed writes release controls and preserve drafts; failed refresh keeps the previous state', async t => {
  const page = await fixture(t, { ...base, integrationEnabled: true, credentials: [active] });
  await page.check('#tiktokAudited');
  await page.fill('#tiktokAuditEvidence', 'synthetic-review-123');
  await page.check('#tiktokAuditConfirm');
  await page.evaluate(() => { window.fixture.failWrite = true; });
  await page.click('#tiktokSaveSettings');
  await page.waitForFunction(() => document.getElementById('tiktokSettingsMessage').textContent.length > 0);
  assert.equal(await page.isDisabled('#tiktokSaveSettings'), false);
  assert.equal(await page.inputValue('#tiktokAuditEvidence'), 'synthetic-review-123');
  await page.evaluate(async () => { window.fixture.failRead = true; await window.videoMakerTikTokAdmin.refresh(); });
  assert.match(await page.textContent('#tiktokNotice'), /Chưa hoàn tất/);
  assert.match(await page.textContent('#tiktokStatusTitle'), /đang hoạt động/);
  assert.equal(await page.inputValue('#tiktokAuditEvidence'), 'synthetic-review-123');
});

test('verification expiry checks the server once more before ending automatic polling', async t => {
  const pending = { ...active, status: 'Pending', verificationRequestedByCurrentAdmin: true, verificationExpiresAtUtc: new Date(Date.now() + 5000).toISOString() };
  const page = await fixture(t, { ...base, credentials: [pending] });
  const count = await page.evaluate(() => window.fixture.calls.length);
  await page.evaluate(() => { window.fixture.state.credentials[0].status = 'Active'; window.fixture.state.integrationEnabled = true; });
  await page.clock.fastForward(6000);
  await page.waitForFunction(() => document.getElementById('tiktokStatusTitle').textContent.includes('đang hoạt động'));
  assert.equal(await page.evaluate(() => window.fixture.calls.length), count + 1);
  await page.clock.fastForward(60000);
  assert.equal(await page.evaluate(() => window.fixture.calls.length), count + 1);
});

test('another Admin verification and environment lock expose no verification or settings writes', async t => {
  const pending = { ...active, credentialId: 'pending-2', version: 2, status: 'Pending', verificationRequestedByCurrentAdmin: false, verificationExpiresAtUtc: new Date(Date.now() + 900000).toISOString() };
  const page = await fixture(t, { ...base, credentials: [active, pending] });
  assert.match(await page.textContent('#tiktokStatusTitle'), /Admin khác/);
  assert.match(await page.textContent('[aria-current=step]'), /Xác minh trên Desktop/);
  await page.click('#tiktokNextAction');
  assert.equal(await page.evaluate(() => window.fixture.calls.some(c => c.method !== 'GET')), false);
  assert.equal(await page.isDisabled('#tiktokSaveSettings'), true);
  await page.evaluate(async () => { window.fixture.state.adminManagementEnabled = false; await window.videoMakerTikTokAdmin.refresh(); });
  assert.equal(await page.isDisabled('#addTikTokCredentialButton'), true);
  assert.equal(await page.isDisabled('[data-tt-revoke]'), true);
  assert.match(await page.textContent('#tiktokStatusTitle'), /bị khóa/);
});
