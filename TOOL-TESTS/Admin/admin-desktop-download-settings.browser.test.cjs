// Real Razor/JS/CSS with intercepted synthetic responses; never contacts the VPS or database.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const { withAdminFonts } = require('./admin-browser-assets.cjs');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const razor = read('TOOL-SERVER/Pages/Admin/Index.cshtml');
const html = razor.slice(razor.indexOf('<!doctype')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
const origin = 'https://admin.example.test';
const endpoint = '/api/admin/desktop-download-settings';
const setupUrl = 'https://103.179.190.182:8443/models/VideoMaker-Setup-1.2.0.exe';
const saved = (url = setupUrl, revision = 'AAAAAAAAB9E=') => ({ setupDownloadUrl: url, revision, updatedAtUtc: '2026-10-17T08:00:00Z' });
const empty = () => ({ setupDownloadUrl: null, revision: null, updatedAtUtc: null });
const paging = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, hasPrevious: false, hasNext: false };
function gate() {
  let enter, release;
  const entered = new Promise(resolve => { enter = resolve; });
  const waiting = new Promise(resolve => { release = resolve; });
  return { entered, release, wait: () => { enter(); return waiting; } };
}
let browser;
before(async () => { browser = await chromium.launch({ headless: true, channel: process.env.VIDEOMAKER_BROWSER_CHANNEL }); });
after(async () => { await browser?.close(); });

async function fixture(t, options = {}) {
  const context = await browser.newContext({ viewport: { width: options.width || 1440, height: 1000 }, locale: 'vi-VN' });
  const page = await context.newPage(), calls = [], errors = [], outbound = [];
  t.after(async () => { await context.close(); assert.deepEqual(errors, []); assert.deepEqual(outbound, []); });
  page.setDefaultTimeout(8000);
  const state = { settings: options.settings || empty(), failRead: options.failRead || false, failSave: false,
    conflict: false, loseSaveResponse: false, saveStatus: null, readGate: null, writeGate: null, nextRevision: 1 };
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url()), method = request.method();
    if (url.origin !== origin) { outbound.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    const body = request.postData() ? request.postDataJSON() : null;
    calls.push({ path: url.pathname, method, body });
    let response;
    if (url.pathname === '/api/admin/licenses/overview') response = { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 };
    else if (url.pathname === '/api/admin/licenses/plans') response = [];
    else if (url.pathname === '/api/admin/licenses/users/page' || url.pathname === '/api/admin/desktop-releases/page') response = paging;
    else if (url.pathname === '/api/auth/logout') return route.fulfill({ status: 204 });
    else if (url.pathname === '/api/auth/login') response = { accessToken: 'synthetic-new-session', refreshToken: 'synthetic-refresh', user: { userId: 'another-admin', email: 'qa@example.test', displayName: 'Admin QA 2', roles: ['Admin'] } };
    else if (url.pathname === endpoint && method === 'GET') {
      response = { ...state.settings };
      const pending = state.readGate; state.readGate = null;
      if (pending) await pending.wait();
      if (state.failRead) { state.failRead = false; return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Chưa đọc được cấu hình.' }) }); }
    } else if (url.pathname === endpoint && method === 'POST') {
      const pending = state.writeGate; state.writeGate = null;
      if (pending) await pending.wait();
      if (state.saveStatus) return route.fulfill({ status: state.saveStatus, contentType: 'text/html', body: '<!DOCTYPE html><html><body><h1>403 - Forbidden: Access is denied.</h1></body></html>' });
      if (state.failSave) return route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ code: 'desktop_setup_download_url_invalid', message: 'Nhập link HTTPS tới file Setup.exe trong nguồn tải VPS đã cấu hình.' }) });
      if (state.conflict) { state.conflict = false; return route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ code: 'desktop_setup_download_settings_changed', message: 'Link tải đã được thay đổi. Tải lại thông tin rồi lưu lại.' }) }); }
      assert.equal(body.revision, state.settings.revision);
      state.settings = saved(body.setupDownloadUrl, 'synthetic-revision-' + state.nextRevision++);
      if (state.loseSaveResponse) { state.loseSaveResponse = false; return route.abort('failed'); }
      response = state.settings;
    } else return route.fulfill({ status: 404, contentType: 'application/json', body: '{}' });
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(response) });
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-admin-test');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ userId: 'synthetic-admin', displayName: 'Admin QA', roles: ['Admin'] }));
    window.openedSetupLinks = [];
    window.open = (...args) => { window.openedSetupLinks.push(args); return null; };
  });
  await page.goto(origin + '/');
  await page.addStyleTag({ content: withAdminFonts(read('TOOL-SERVER/wwwroot/admin/admin.css')) });
  await page.addStyleTag({ content: read('TOOL-SERVER/wwwroot/admin/admin-modules.css') });
  for (const name of ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-desktop-download-settings.js']) await page.addScriptTag({ content: read('TOOL-SERVER/wwwroot/admin/' + name) });
  await page.waitForSelector('#adminShell:not(.hidden)');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('releases'));
  await idle(page);
  return { page, state, calls };
}
const writes = calls => calls.filter(call => call.method !== 'GET');
async function idle(page) { await page.waitForFunction(() => document.getElementById('desktopDownloadSettingsForm').getAttribute('aria-busy') === 'false'); }
async function submit(page, value) {
  await page.fill('#desktopSetupDownloadUrl', value);
  await page.click('#desktopDownloadSettingsSave');
}
async function loginAgain(page) {
  await page.fill('#loginEmail', 'qa@example.test'); await page.fill('#loginPassword', 'synthetic-password');
  await page.click('#loginForm button[type=submit]'); await page.waitForSelector('#adminShell:not(.hidden)');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('releases')); await idle(page);
}

test('Setup download card exposes one URL field and leaves shared bootstrapper registration separate', async t => {
  const { page, calls } = await fixture(t);
  assert.deepEqual(await page.locator('#desktopDownloadSettingsForm input').evaluateAll(nodes => nodes.map(node => ({ id: node.id, type: node.type, required: node.required }))), [{ id: 'desktopSetupDownloadUrl', type: 'url', required: true }]);
  assert.equal(await page.locator('#bootstrapperForm #bootstrapperFile[type=file]').count(), 1);
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), true);
  assert.deepEqual(writes(calls), []);
  assert.equal(calls.filter(call => call.path === endpoint).length, 1);
});

test('invalid or unsafe links preserve draft, focus the error field and cause no write', async t => {
  const { page, calls } = await fixture(t);
  const invalid = ['', 'http://example.test/Setup.exe', 'javascript:alert(1)', 'https://user:pass@example.test/Setup.exe',
    setupUrl + '?key=value', setupUrl + '#fragment', setupUrl.replace('.exe', '.zip'),
    'https://example.test/models/a%2fSetup.exe', 'https://example.test/models/a%5cSetup.exe',
    'https://example.test/models/Setup%0a.exe', 'https://example.test/models/Setup%2520.exe',
    'https://example.test/models/%2e%2e/Setup.exe', 'https://example.test/models/../Setup.exe', 'https://example.test/Setup%xx.exe'];
  for (const value of invalid) {
    await submit(page, value);
    assert.equal(await page.getAttribute('#desktopSetupDownloadUrl', 'aria-invalid'), 'true', value);
    assert.equal(await page.evaluate(() => document.activeElement.id), 'desktopSetupDownloadUrl');
    assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), value);
  }
  assert.deepEqual(writes(calls), []);
});

test('saving stores only URL and revision; open uses saved URL and reports that file is not inspected', async t => {
  const { page, calls } = await fixture(t);
  await submit(page, setupUrl); await idle(page);
  assert.deepEqual(writes(calls), [{ path: endpoint, method: 'POST', body: { setupDownloadUrl: setupUrl, revision: null } }]);
  assert.match(await page.textContent('#desktopDownloadSettingsStatus'), /Đã lưu.*chưa được kiểm tra/);
  assert.equal(await page.locator('#desktopDownloadSettingsSave').isDisabled(), true);
  await page.click('#desktopDownloadSettingsOpen');
  assert.deepEqual(await page.evaluate(() => window.openedSetupLinks), [[setupUrl, '_blank', 'noopener,noreferrer']]);
  await page.fill('#desktopSetupDownloadUrl', setupUrl.replace('1.2.0', '1.3.0'));
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), true);
});

test('safe encoded spaces and Unicode filenames roundtrip without weakening traversal rejection', async t => {
  const { page, calls } = await fixture(t);
  const encoded = 'https://103.179.190.182:8443/models/VideoMaker%20C%C3%A0i%20%C4%91%E1%BA%B7t.exe';
  await submit(page, encoded); await idle(page);
  assert.equal(writes(calls).at(-1).body.setupDownloadUrl, encoded);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), encoded);
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), false);
  await page.click('#desktopDownloadSettingsOpen');
  assert.equal((await page.evaluate(() => window.openedSetupLinks))[0][0], encoded);
});

test('pending save disables all actions and suppresses duplicate submit', async t => {
  const { page, state, calls } = await fixture(t);
  const pending = gate(); state.writeGate = pending;
  await submit(page, setupUrl); await pending.entered;
  try {
    for (const id of ['desktopSetupDownloadUrl', 'desktopDownloadSettingsSave', 'desktopDownloadSettingsOpen']) assert.equal(await page.locator('#' + id).isDisabled(), true);
    await page.evaluate(() => document.getElementById('desktopDownloadSettingsForm').requestSubmit());
    assert.equal(writes(calls).length, 1);
  } finally { pending.release(); }
  await idle(page); assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), false);
});

test('server validation keeps draft and restores input focus after pending ends', async t => {
  const { page, state } = await fixture(t); state.failSave = true;
  await submit(page, setupUrl); await idle(page);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), setupUrl);
  assert.match(await page.textContent('#desktopSetupDownloadError'), /nguồn tải VPS/);
  assert.equal(await page.evaluate(() => document.activeElement.id), 'desktopSetupDownloadUrl');
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), true);
});

test('revision conflict reloads latest revision while preserving draft for explicit resubmit', async t => {
  const { page, state, calls } = await fixture(t, { settings: saved() });
  const draft = setupUrl.replace('1.2.0', '1.3.0');
  state.settings = saved(setupUrl.replace('1.2.0', '1.2.1'), 'newer-revision'); state.conflict = true;
  await submit(page, draft); await idle(page);
  await page.waitForFunction(() => document.getElementById('desktopDownloadSettingsStatus').textContent.includes('phiên khác'));
  assert.equal(writes(calls)[0].body.revision, 'AAAAAAAAB9E=');
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), draft);
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), true);
  await page.click('#desktopDownloadSettingsSave'); await idle(page);
  assert.equal(writes(calls)[1].body.revision, 'newer-revision');
  assert.equal(state.settings.setupDownloadUrl, draft);
});

test('read failure exposes retry and retry performs GET rather than uncertain write', async t => {
  const { page, calls } = await fixture(t, { settings: saved(), failRead: true });
  assert.equal(await page.locator('#desktopSetupDownloadUrl').isDisabled(), true);
  assert.equal(await page.textContent('#desktopDownloadSettingsSave'), 'Thử lại');
  await page.click('#desktopDownloadSettingsSave'); await idle(page);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), setupUrl);
  assert.deepEqual(writes(calls), []);
  assert.equal(calls.filter(call => call.path === endpoint).length, 2);
});

test('lost save response recovers stored result by GET without submitting a second write', async t => {
  const { page, state, calls } = await fixture(t); state.loseSaveResponse = true;
  await submit(page, setupUrl); await idle(page);
  assert.equal(await page.textContent('#desktopDownloadSettingsSave'), 'Thử lại');
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), true);
  await page.click('#desktopDownloadSettingsSave'); await idle(page);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), setupUrl);
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), false);
  assert.equal(writes(calls).length, 1);
});

test('returning to releases reloads settings instead of opening a stale saved URL', async t => {
  const { page, state, calls } = await fixture(t, { settings: saved() });
  await page.evaluate(() => window.videoMakerAdminShell.navigate('overview'));
  state.settings = saved(setupUrl.replace('1.2.0', '1.2.2'), 'latest-revision');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('releases')); await idle(page);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), state.settings.setupDownloadUrl);
  assert.equal(calls.filter(call => call.path === endpoint).length, 2);
});

test('shell refresh reloads the Setup link and revision without a write', async t => {
  const { page, state, calls } = await fixture(t, { settings: saved() });
  state.settings = saved(setupUrl.replace('1.2.0', '1.2.3'), 'refreshed-revision');
  await page.click('#refreshButton'); await idle(page);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), state.settings.setupDownloadUrl);
  assert.equal(calls.filter(call => call.path === endpoint).length, 2);
  assert.deepEqual(writes(calls), []);
});

test('late GET from logged out session cannot replace settings loaded by the next admin', async t => {
  const { page, state } = await fixture(t, { settings: saved() });
  await page.evaluate(() => window.videoMakerAdminShell.navigate('overview'));
  const pending = gate(); state.readGate = pending;
  await page.evaluate(() => window.videoMakerAdminShell.navigate('releases')); await pending.entered;
  await page.click('#logoutButton'); await page.waitForSelector('#loginScreen:not(.hidden)');
  const newUrl = setupUrl.replace('1.2.0', '2.0.0'); state.settings = saved(newUrl, 'new-session-revision');
  await loginAgain(page);
  pending.release();
  await page.waitForTimeout(80);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), newUrl);
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), false);
  assert.equal(await page.textContent('#loginMessage'), '');
});

test('late save from logged out session cannot overwrite the next admin view', async t => {
  const { page, state } = await fixture(t, { settings: saved() });
  const pending = gate(); state.writeGate = pending;
  await submit(page, setupUrl.replace('1.2.0', '1.3.0')); await pending.entered;
  await page.click('#logoutButton'); await page.waitForSelector('#loginScreen:not(.hidden)');
  const newUrl = setupUrl.replace('1.2.0', '2.1.0'); state.settings = saved(newUrl, 'next-admin-revision');
  await loginAgain(page);
  // The server may finish an accepted write after abort; its late response must not touch this new view.
  state.failSave = true; pending.release();
  await page.waitForTimeout(80);
  assert.equal(await page.inputValue('#desktopSetupDownloadUrl'), newUrl);
  assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), false);
  assert.equal(await page.locator('#desktopSetupDownloadError').isHidden(), true);
});

test('hosting HTML error gives HTTP status and safe recovery without claiming a save succeeded', async t => {
  const { page, state, calls } = await fixture(t);
  for (const code of [403, 404, 405, 500]) {
    state.saveStatus = code;
    await submit(page, setupUrl); await idle(page);
    const message = await page.textContent('#desktopDownloadSettingsStatus');
    assert.match(message, new RegExp(`HTTP ${code}`));
    assert.doesNotMatch(message, /DOCTYPE|<html|<h1|Đã lưu/);
    assert.equal(await page.locator('#desktopDownloadSettingsOpen').isDisabled(), true);
    assert.equal(await page.textContent('#desktopDownloadSettingsSave'), 'Thử lại');
    const count = writes(calls).length;
    state.saveStatus = null;
    await page.click('#desktopDownloadSettingsSave'); await idle(page);
    assert.equal(writes(calls).length, count);
  }
});

test('download card fits mobile and desktop with usable action targets', async t => {
  const { page } = await fixture(t, { settings: saved() });
  for (const width of [320, 390, 768, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.locator('#desktopDownloadSettingsForm').scrollIntoViewIfNeeded();
    const dimensions = await page.evaluate(() => {
      const form = document.getElementById('desktopDownloadSettingsForm'), box = form.getBoundingClientRect();
      return { viewport: innerWidth, documentWidth: document.documentElement.scrollWidth, left: box.left, right: box.right,
        scrollWidth: form.scrollWidth, width: form.clientWidth,
        buttons: [...form.querySelectorAll('button')].map(node => ({ width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height })) };
    });
    assert.ok(dimensions.left >= 0 && dimensions.right <= width + 1, JSON.stringify(dimensions));
    assert.ok(dimensions.scrollWidth <= dimensions.width + 1, JSON.stringify(dimensions));
    assert.ok(dimensions.documentWidth <= width + 1, JSON.stringify(dimensions));
    assert.ok(dimensions.buttons.every(button => button.width >= 44 && button.height >= 44));
    if (process.env.VIDEOMAKER_UI_EVIDENCE_ROOT && [390, 1440].includes(width)) {
      const output = path.resolve(process.env.VIDEOMAKER_UI_EVIDENCE_ROOT); fs.mkdirSync(output, { recursive: true });
      await page.locator('section[aria-labelledby=desktopDownloadSettingsTitle]').screenshot({ path: path.join(output, `setup-download-${width}.png`) });
    }
  }
});
