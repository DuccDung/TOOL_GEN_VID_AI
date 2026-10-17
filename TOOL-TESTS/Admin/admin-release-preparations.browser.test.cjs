// Actual Razor, JavaScript and CSS with synthetic API responses only; no VPS or database access.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const { withAdminFonts } = require('./admin-browser-assets.cjs');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const razor = read('TOOL-SERVER/Pages/Admin/Index.cshtml');
const html = razor.slice(razor.indexOf('<!doctype')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
const origin = 'https://admin.example.test';
const base = '/api/admin/desktop-release-preparations';
const directBase = '/api/admin/desktop-direct-releases';
const standaloneBase = '/api/admin/desktop-standalone-releases';
const setupBase = '/api/admin/desktop-bootstrappers';
const zipUrl = 'https://103.179.190.182:8443/apps/videomaker/1.2.0/VideoMaker.zip';
const directZipUrl = 'https://103.179.190.182:8443/apps/videomaker/1.2.0/VideoMaker-1.2.0-37-win-x64-' + 'a'.repeat(64) + '.zip';
const id = '17498ca0-7950-4a3b-ab91-f8c3f5b26f02';
const setupId = 'c740e870-0ef3-4433-8c0e-fe175d4102f5';
const paging = items => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0, hasPrevious: false, hasNext: false });
const sharedSetup = () => ({ bootstrapperId: setupId, version: '1.0.0', fileName: 'VideoMaker Setup.exe', downloadUrl: '/api/launcher/bootstrapper/' + setupId,
  sizeBytes: 16384, sha256: 'b'.repeat(64), apiBaseUrl: 'https://api.example.test/', profile: 'Production', channel: 'Stable', platform: 'win-x64', distributionProtocol: 3, modelBundleId: 'models-20261016-03' });
const preparation = overrides => ({ preparationId: id, version: '1.2.0', downloadUrl: zipUrl, releaseNotes: null, isMandatory: false,
  status: 'Queued', revision: crypto.randomUUID(), createdAtUtc: '2026-10-17T00:00:00Z', updatedAtUtc: '2026-10-17T00:00:00Z',
  releaseId: null, fileName: null, sizeBytes: null, sha256: null, buildNumber: null, bytesReceived: 0, totalBytes: null,
  errorCode: null, errorMessage: null, canPublish: false, bootstrapper: null, published: false, generation: 1, ...overrides });
const ready = overrides => preparation({ status: 'Ready', fileName: 'VideoMaker.zip', sizeBytes: 4096, sha256: 'a'.repeat(64), buildNumber: 37,
  bytesReceived: 4096, totalBytes: 4096, canPublish: true, bootstrapper: sharedSetup(), ...overrides });
const directSetup = () => ({ ...sharedSetup(), version: '1.2.0', supportedCatalogProtocols: '3,4' });
const directDraft = overrides => ({ releaseId: id, version: '1.2.0', declaredBuildNumber: 37, downloadUrl: directZipUrl,
  expectedSha256: 'a'.repeat(64), profile: 'Production', channel: 'Stable', platform: 'win-x64', status: 'Draft',
  isActive: false, isMandatory: false, releaseNotes: null, revision: crypto.randomUUID(), createdAtUtc: '2026-10-17T00:00:00Z',
  updatedAtUtc: '2026-10-17T00:00:00Z', firstPublishedAtUtc: null, bootstrapper: directSetup(), canPublish: true, ...overrides });
let browser;
before(async () => { browser = await chromium.launch({ headless: true, channel: process.env.VIDEOMAKER_BROWSER_CHANNEL }); });
after(async () => { await browser?.close(); });

async function fixture(t, options = {}) {
  const direct = options.direct || options.standalone;
  const releaseBase = options.standalone ? standaloneBase : direct ? directBase : base;
  const context = await browser.newContext({ viewport: { width: options.width || 1440, height: 1000 }, locale: 'vi-VN' });
  t.after(() => context.close());
  const page = await context.newPage(), calls = [], errors = [], outbound = [];
  page.setDefaultTimeout(8000);
  const state = { preparation: options.preparation || null, catalog: options.catalog || [], complete: false, failSave: false,
    loseCreateResponse: options.loseCreateResponse || false, conflict: false, forbiddenSave: false, holdCreate: options.holdCreate || false, holdRead: options.holdRead || false, postCount: 0 };
  let unblock, entered;
  const held = new Promise(resolve => { entered = resolve; });
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url()), method = request.method();
    if (url.origin !== origin) { outbound.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    const json = request.headers()['content-type']?.startsWith('application/json') ? request.postDataJSON() : null;
    calls.push({ path: url.pathname, method, body: json, multipart: request.headers()['content-type']?.startsWith('multipart/') });
    let body, status = 200;
    if (url.pathname === '/api/admin/licenses/overview') body = { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 };
    else if (url.pathname === '/api/admin/licenses/plans') body = [];
    else if (url.pathname === '/api/admin/licenses/users/page' || url.pathname === '/api/admin/desktop-releases/page') body = paging([]);
    else if (url.pathname === releaseBase + '/page') body = paging(state.preparation ? [state.preparation] : []);
    else if (url.pathname === releaseBase && method === 'POST') {
      state.postCount++;
      if (!state.preparation) state.preparation = direct ? directDraft({ ...json, ...(options.standalone ? { bootstrapper: null } : {}) }) : preparation({ ...json });
      if (state.holdCreate) { entered(); await new Promise(resolve => { unblock = resolve; }); }
      if (state.loseCreateResponse) { state.loseCreateResponse = false; return route.abort('failed'); }
      body = state.preparation; status = direct ? 201 : 202;
    } else if (url.pathname === releaseBase + '/' + id && method === 'GET') {
      if (state.holdRead) { entered(); await new Promise(resolve => { unblock = resolve; }); state.holdRead = false; }
      if (state.complete) { state.preparation = ready({ ...state.preparation, status: 'Ready', fileName: 'VideoMaker.zip', sha256: 'a'.repeat(64), sizeBytes: 4096, buildNumber: 37, bootstrapper: sharedSetup(), canPublish: true, revision: crypto.randomUUID() }); state.complete = false; }
      body = state.preparation;
    } else if (url.pathname === releaseBase + '/' + id && method === 'PUT' || options.standalone && url.pathname === releaseBase + '/' + id + '/save' && method === 'POST') {
      if (state.forbiddenSave) return route.fulfill({ status: 403, contentType: 'text/html', body: '<!DOCTYPE html><html><body><h1>403 - Forbidden: Access is denied.</h1></body></html>' });
      if (state.conflict) return route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ code: 'desktop_release_revision_conflict', message: 'Bản nháp đã được thay đổi ở phiên khác.' }) });
      if (state.failSave) return route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ code: 'desktop_package_version_mismatch', message: 'Phiên bản nhập 1.2.0 không khớp phiên bản gói 2.0.0.' }) });
      assert.equal(json.revision, state.preparation.revision);
      const changed = json.version !== state.preparation.version || json.downloadUrl !== state.preparation.downloadUrl;
      state.preparation = { ...state.preparation, ...json, revision: crypto.randomUUID(), ...(changed ? direct ? { status: 'Draft' } : { status: 'Queued', canPublish: false, generation: state.preparation.generation + 1 } : {}) };
      body = state.preparation; status = changed && !direct ? 202 : 200;
    } else if (url.pathname.endsWith('/cancel')) {
      assert.equal(json.revision, state.preparation.revision);
      state.preparation = { ...state.preparation, status: 'Cancelled', canPublish: false, revision: crypto.randomUUID() }; body = state.preparation;
    } else if (url.pathname.endsWith('/retry')) {
      assert.equal(json.revision, state.preparation.revision);
      state.preparation = { ...state.preparation, status: 'Queued', generation: state.preparation.generation + 1, revision: crypto.randomUUID(), errorCode: null, errorMessage: null }; body = state.preparation;
    } else if (url.pathname.endsWith('/deactivate')) {
      assert.equal(json.revision, state.preparation.revision);
      state.preparation = { ...state.preparation, isActive: false, ...(direct ? { canPublish: true } : {}), revision: crypto.randomUUID() }; body = state.preparation;
    } else if (url.pathname.endsWith('/publish')) {
      assert.equal(json.revision, state.preparation.revision);
      state.preparation = { ...state.preparation, status: 'Published', published: true, isActive: true, canPublish: !direct, releaseId: direct ? id : 'published-release', revision: crypto.randomUUID() }; body = state.preparation;
    } else if (url.pathname === setupBase + '/status') body = { sharedBootstrapperEnabled: !options.standalone, releasePreparationEnabled: !direct, configured: true, profile: 'Production', ...(direct ? { publicationMode: options.standalone ? 'StandaloneCatalog' : 'DirectCatalog' } : {}) };
    else if (url.pathname === setupBase && method === 'GET') body = state.catalog;
    else if (url.pathname === setupBase && method === 'POST') { const item = { bootstrapper: sharedSetup(), revision: crypto.randomUUID(), isActive: false, createdAtUtc: '2026-10-17T00:00:00Z' }; state.catalog = [item]; body = item; }
    else if (url.pathname === `${setupBase}/${setupId}/activate`) {
      assert.equal(json.revision, state.catalog[0].revision);
      state.catalog[0].isActive = true; body = state.catalog[0];
      if (state.preparation) state.preparation = { ...state.preparation, bootstrapper: options.direct ? directSetup() : sharedSetup(), canPublish: state.preparation.status === (options.direct ? 'Draft' : 'Ready') };
    } else return route.fulfill({ status: 404, body: '{}' });
    return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-admin-test');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ userId: 'synthetic-admin', displayName: 'Admin QA', roles: ['Admin'] }));
  });
  async function load() {
    await page.goto(origin + '/');
    await page.addStyleTag({ content: withAdminFonts(read('TOOL-SERVER/wwwroot/admin/admin.css')) });
    await page.addStyleTag({ content: read('TOOL-SERVER/wwwroot/admin/admin-modules.css') });
    for (const name of ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-release-preparations.js']) await page.addScriptTag({ content: read('TOOL-SERVER/wwwroot/admin/' + name) });
    await page.waitForSelector('#adminShell:not(.hidden)');
    await page.evaluate(() => window.videoMakerAdminShell.navigate('releases'));
    await page.waitForFunction(() => document.getElementById('releasePreparationListMessage').textContent === '');
  }
  await load();
  t.after(() => { assert.deepEqual(errors, []); assert.deepEqual(outbound, []); });
  return { page, calls, state, load, held, unblock: () => unblock?.() };
}
const writes = calls => calls.filter(call => call.method !== 'GET');
async function fillNew(page) {
  await page.click('#addReleaseButton'); await page.fill('#releaseDraftVersion', '1.2.0'); await page.fill('#releaseDraftUrl', zipUrl);
}
async function openExisting(page) { await page.click('[data-open-preparation]'); await page.waitForSelector('#releaseDraftInspection:not([hidden])'); }

test('new form exposes exactly two required fields and no per-release Setup or technical input', async t => {
  const { page, calls } = await fixture(t);
  await page.click('#addReleaseButton');
  assert.deepEqual(await page.locator('#releaseDraftForm [required]').evaluateAll(nodes => nodes.map(node => node.id)), ['releaseDraftVersion', 'releaseDraftUrl']);
  assert.equal(await page.locator('#releaseDraftForm input[type=file], #releaseDraftForm select').count(), 0);
  assert.equal(await page.locator('#releaseDraftMandatory').isChecked(), false);
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseDraftVersion');
  assert.deepEqual(writes(calls), []);
});

test('validation focuses linked summary, preserves notes and sends no write for malformed version or URL', async t => {
  const { page, calls } = await fixture(t); await fillNew(page);
  await page.fill('#releaseDraftNotes', 'Ghi chú giữ nguyên'); await page.fill('#releaseDraftVersion', '1.2'); await page.click('#releaseDraftSave');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseDraftErrors');
  await page.click('#releaseDraftErrors a'); assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseDraftVersion');
  await page.fill('#releaseDraftVersion', '1.2.0'); await page.fill('#releaseDraftUrl', 'http://example.test/app.zip'); await page.click('#releaseDraftSave');
  assert.equal(await page.locator('#releaseDraftErrors a').getAttribute('href'), '#releaseDraftUrl');
  assert.equal(await page.inputValue('#releaseDraftNotes'), 'Ghi chú giữ nguyên'); assert.deepEqual(writes(calls), []);
});

test('save enqueues durable draft, polls to Ready then publishes using the latest revision', async t => {
  const { page, calls, state } = await fixture(t); await fillNew(page); await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftStatus').getByText('Đang chờ', { exact: true }).waitFor();
  const create = writes(calls)[0]; assert.equal(create.path, base); assert.equal(create.body.downloadUrl, zipUrl);
  assert.match(create.body.requestId, /^[a-f\d-]{36}$/); assert.equal(create.body.buildNumber, undefined); assert.equal(create.body.sha256, undefined);
  state.complete = true; await page.waitForFunction(() => !document.getElementById('releaseDraftPublish').disabled);
  const revision = state.preparation.revision; await page.click('#releaseDraftPublish');
  await page.locator('#releaseDraftStatus').getByText('Đang phân phối', { exact: true }).waitFor();
  assert.equal(writes(calls).at(-1).body.revision, revision);
  assert.equal(await page.locator('#releaseDraftVersion').isDisabled(), true);
});

test('changing URL or version locks publish immediately and saving starts a new generation', async t => {
  const { page, state, calls } = await fixture(t, { preparation: ready() }); await openExisting(page);
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), false);
  await page.fill('#releaseDraftUrl', zipUrl.replace('/1.2.0/', '/another/')); assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  await page.fill('#releaseDraftVersion', '1.3.0'); await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftStatus').getByText('Đang chờ', { exact: true }).waitFor();
  assert.equal(state.preparation.generation, 2); assert.equal(writes(calls).at(-1).method, 'PUT');
});

test('notes and mandatory edits require save while retaining inspected identity', async t => {
  const { page, state } = await fixture(t, { preparation: ready() }); await openExisting(page);
  await page.fill('#releaseDraftNotes', 'Cập nhật trải nghiệm'); await page.check('#releaseDraftMandatory');
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true); await page.click('#releaseDraftSave');
  await page.waitForFunction(() => !document.getElementById('releaseDraftPublish').disabled);
  assert.equal(state.preparation.generation, 1); assert.equal(state.preparation.sha256, 'a'.repeat(64));
});

test('published preparation fixes version and URL while allowing CAS edits to notes and mandatory policy', async t => {
  const { page, calls, state } = await fixture(t, { preparation: ready({ status: 'Published', published: true, canPublish: false }) }); await openExisting(page);
  assert.equal(await page.locator('#releaseDraftVersion').isDisabled(), true); assert.equal(await page.locator('#releaseDraftUrl').isDisabled(), true);
  assert.equal(await page.locator('#releaseDraftNotes').isDisabled(), false); assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  await page.fill('#releaseDraftNotes', 'Bổ sung hướng dẫn'); await page.check('#releaseDraftMandatory'); await page.click('#releaseDraftSave');
  await page.waitForFunction(() => document.getElementById('releaseDraftSave').disabled);
  assert.equal(writes(calls).at(-1).method, 'PUT'); assert.equal(state.preparation.published, true); assert.equal(state.preparation.isMandatory, true);
});

test('deactivating a published version confirms the action and preserves the inspected identity', async t => {
  const { page, calls, state } = await fixture(t, { preparation: ready({ status: 'Published', published: true, isActive: true }) }); await openExisting(page);
  await page.click('#releaseDraftDeactivate'); await page.click('#adminActionCancel'); assert.deepEqual(writes(calls), []);
  const revision = state.preparation.revision;
  await page.click('#releaseDraftDeactivate'); await page.click('#adminActionConfirm');
  await page.locator('#releaseDraftStatus').getByText('Đã ngừng phân phối', { exact: true }).waitFor();
  assert.equal(writes(calls).at(-1).path, base + '/' + id + '/deactivate'); assert.equal(writes(calls).at(-1).body.revision, revision);
  assert.equal(state.preparation.sha256, 'a'.repeat(64)); assert.equal(await page.locator('#releaseDraftVersion').isDisabled(), true);
});

test('a deactivated published version can be republished only after metadata is saved with fresh revision', async t => {
  const { page, calls, state } = await fixture(t, { preparation: ready({ status: 'Published', published: true, isActive: false }) }); await openExisting(page);
  assert.equal(await page.locator('#releaseDraftPublish').textContent(), 'Phát hành lại'); assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), false);
  await page.fill('#releaseDraftNotes', 'Phát hành trở lại'); assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  await page.click('#releaseDraftSave'); await page.waitForFunction(() => !document.getElementById('releaseDraftPublish').disabled);
  const revision = state.preparation.revision; await page.click('#releaseDraftPublish');
  await page.locator('#releaseDraftStatus').getByText('Đang phân phối', { exact: true }).waitFor();
  assert.equal(writes(calls).at(-1).body.revision, revision); assert.equal(state.preparation.generation, 1);
});

test('Escape stops modal polling without cancel and reload recovers the server draft', async t => {
  const { page, calls, load, state } = await fixture(t, { preparation: preparation({ status: 'Downloading', bytesReceived: 2048, totalBytes: 4096 }) });
  await openExisting(page); await page.keyboard.press('Escape');
  assert.equal(await page.locator('#releaseDraftDialog').evaluate(node => node.open), false);
  const getCount = calls.filter(call => call.path === base + '/' + id).length;
  await page.waitForTimeout(1500);
  assert.equal(calls.filter(call => call.path === base + '/' + id).length, getCount);
  assert.deepEqual(writes(calls), []);
  state.preparation = ready(); await load(); await openExisting(page);
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), false);
});

test('cancel waits for server acknowledgement and retry reuses the persisted preparation', async t => {
  const { page, calls } = await fixture(t, { preparation: preparation() }); await openExisting(page);
  await page.click('#releaseDraftCancel'); await page.locator('#releaseDraftStatus').getByText('Đã hủy', { exact: true }).waitFor();
  assert.match(await page.locator('#releaseDraftMessage').textContent(), /Server đã xác nhận/);
  await page.click('#releaseDraftRetry'); await page.locator('#releaseDraftStatus').getByText('Đang chờ', { exact: true }).waitFor();
  assert.deepEqual(writes(calls).map(call => call.path), [base + '/' + id + '/cancel', base + '/' + id + '/retry']);
});

test('CAS conflict preserves input and prevents mutation until the user reloads server state', async t => {
  const { page, state, calls } = await fixture(t, { preparation: ready() }); await openExisting(page);
  await page.fill('#releaseDraftNotes', 'Nội dung chưa lưu'); state.conflict = true; await page.click('#releaseDraftSave');
  await page.waitForSelector('#releaseDraftReload:not([hidden])'); assert.equal(await page.inputValue('#releaseDraftNotes'), 'Nội dung chưa lưu');
  assert.equal(await page.locator('#releaseDraftSave').isDisabled(), true); assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  state.conflict = false; state.preparation.releaseNotes = 'Nội dung từ phiên khác'; await page.click('#releaseDraftReload');
  await page.waitForFunction(() => document.getElementById('releaseDraftNotes').value === 'Nội dung từ phiên khác');
  assert.equal(writes(calls).length, 1);
});

test('a lost create response retries with the same requestId and does not create duplicate drafts', async t => {
  const { page, calls, state } = await fixture(t, { loseCreateResponse: true }); await fillNew(page); await page.click('#releaseDraftSave');
  await page.waitForSelector('#releaseDraftErrors:not([hidden])'); await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftStatus').getByText('Đang chờ', { exact: true }).waitFor();
  const creates = writes(calls).filter(call => call.path === base);
  assert.equal(creates.length, 2); assert.equal(creates[0].body.requestId, creates[1].body.requestId); assert.equal(state.preparation.preparationId, id);
});

test('duplicate submit is blocked while closing the window does not cancel the server operation', async t => {
  const current = await fixture(t, { holdCreate: true }); const { page, calls } = current;
  await fillNew(page); await page.click('#releaseDraftSave'); await current.held;
  await page.evaluate(() => document.getElementById('releaseDraftForm').dispatchEvent(new Event('submit', { cancelable: true })));
  await page.keyboard.press('Escape'); assert.equal(await page.locator('#releaseDraftDialog').evaluate(node => node.open), false);
  assert.equal(writes(calls).length, 1); current.unblock();
  await page.waitForSelector('[data-open-preparation]'); await openExisting(page);
  assert.equal(await page.inputValue('#releaseDraftUrl'), zipUrl);
});

test('closing during initial detail loading releases the request lock when the read completes', async t => {
  const current = await fixture(t, { preparation: ready(), holdRead: true }); const { page } = current;
  await page.click('[data-open-preparation]'); await current.held; await page.keyboard.press('Escape');
  current.unblock(); await page.waitForTimeout(150); await page.click('#addReleaseButton');
  await page.waitForSelector('#releaseDraftDialog[open]'); assert.equal(await page.locator('#releaseDraftSave').isDisabled(), false);
  assert.equal(await page.inputValue('#releaseDraftVersion'), '');
});

test('a late save response after logout cannot reopen the dialog or restore protected release data', async t => {
  const current = await fixture(t, { holdCreate: true }); const { page, calls } = current;
  await fillNew(page); await page.click('#releaseDraftSave'); await current.held;
  await page.evaluate(() => window.videoMakerAdminShell.showLogin()); current.unblock(); await page.waitForTimeout(150);
  assert.equal(await page.locator('#adminShell').isVisible(), false);
  assert.equal(await page.locator('#releaseDraftDialog').evaluate(node => node.open), false);
  assert.equal(await page.locator('#releasePreparationList').textContent(), '');
  assert.equal(await page.inputValue('#releaseDraftUrl'), '');
  assert.equal(writes(calls).length, 1);
});

test('missing shared Setup links to separate file-only registration and CAS activation', async t => {
  const { page, calls, state } = await fixture(t, { preparation: ready({ bootstrapper: null, canPublish: false, errorMessage: 'Chưa có Setup dùng chung tương thích.' }) }); await openExisting(page);
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true); await page.click('#releaseDraftSetupLink');
  assert.equal(await page.locator('#bootstrapperDialog').isVisible(), true);
  await page.setInputFiles('#bootstrapperFile', { name: 'VideoMaker Setup.exe', mimeType: 'application/octet-stream', buffer: Buffer.from('MZ synthetic Setup') });
  await page.click('#bootstrapperUpload'); await page.waitForSelector('[data-activate-bootstrapper]');
  const upload = writes(calls).find(call => call.path === setupBase); assert.equal(upload.multipart, true);
  await page.click('[data-activate-bootstrapper]'); await page.click('#adminActionConfirm');
  await page.locator('#bootstrapperStatus').getByText('Đang dùng:', { exact: false }).waitFor(); assert.equal(state.catalog[0].isActive, true);
  await page.keyboard.press('Escape'); await page.waitForFunction(() => !document.getElementById('releaseDraftPublish').disabled);
});

test('revoked system Setup is labelled and cannot be activated or downloaded from the catalog', async t => {
  const { page } = await fixture(t, { catalog: [{ bootstrapper: sharedSetup(), isActive: false, isRevoked: true, revision: crypto.randomUUID() }] });
  await page.click('#manageBootstrapperButton'); await page.locator('#bootstrapperList').getByText('Đã thu hồi', { exact: true }).waitFor();
  assert.equal(await page.locator('#bootstrapperList [data-activate-bootstrapper], #bootstrapperList a[download]').count(), 0);
});

test('long URL and hostile error text remain safe, readable and keyboard accessible on narrow screens', async t => {
  const { page } = await fixture(t, { width: 390, preparation: ready({ errorMessage: '<img src=x onerror=alert(1)>', downloadUrl: zipUrl + 'x'.repeat(1000) }) });
  await openExisting(page);
  assert.equal(await page.locator('#releaseDraftReason img').count(), 0);
  assert.match(await page.locator('#releaseDraftReason').textContent(), /<img/);
  const bounds = await page.locator('#releaseDraftDialog').boundingBox(); assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 391);
  assert.equal(await page.locator('#releaseDraftForm').evaluate(node => node.scrollWidth <= node.clientWidth + 1), true);
  await page.locator('#releaseDraftPublish').focus(); await page.keyboard.press('Shift+Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseDraftSave');
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
    fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'minimal-release-ready-390.png') });
  }
});

test('the initial form fits narrow and desktop viewports with focusable save actions', async t => {
  const { page } = await fixture(t); await fillNew(page);
  for (const width of [320, 390, 768, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    const bounds = await page.locator('#releaseDraftDialog').boundingBox();
    assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1);
    assert.equal(await page.locator('#releaseDraftForm').evaluate(node => node.scrollWidth <= node.clientWidth + 1), true);
    assert.equal(await page.locator('#releaseDraftSave').evaluate(node => getComputedStyle(node).whiteSpace), 'nowrap');
    assert.ok((await page.locator('#releaseDraftSave').boundingBox()).height <= 50);
    assert.ok((await page.locator('#releaseDraftPublish').boundingBox()).height <= 50);
    await page.locator('#releaseDraftSave').focus();
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
      await page.locator('#releaseDraftDialog').evaluate(node => { node.scrollTop = 0; });
      await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `minimal-release-initial-${width}.png`) });
    }
  }
});

test('direct catalog saves two required fields and publishes immediately without inspection or polling', async t => {
  const { page, calls, state } = await fixture(t, { direct: true });
  await fillNew(page); await page.fill('#releaseDraftUrl', directZipUrl);
  assert.deepEqual(await page.locator('#releaseDraftForm [required]').evaluateAll(nodes => nodes.map(node => node.id)), ['releaseDraftVersion', 'releaseDraftUrl']);
  await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftStatus').getByText('Đã lưu nháp', { exact: true }).waitFor();
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), false);
  assert.equal(await page.locator('#releaseDraftProgress').isVisible(), false);
  assert.equal(await page.locator('#releaseDraftRetry').isVisible(), false);
  assert.equal(await page.locator('#releaseDraftCancel').isVisible(), false);
  assert.equal(await page.locator('#releaseDraftTechnical').isVisible(), false);
  assert.equal(await page.locator('#releaseDraftStatusTitle').textContent(), 'Thông tin phát hành');
  assert.match(await page.locator('#releaseWorkspaceNotice').textContent(), /Setup tải và kiểm tra/);
  assert.equal(writes(calls)[0].path, directBase);
  assert.equal(writes(calls)[0].body.sha256, undefined);
  assert.equal(writes(calls)[0].body.buildNumber, undefined);
  await page.waitForTimeout(1400);
  assert.equal(calls.filter(call => call.path === directBase + '/' + id && call.method === 'GET').length, 0);
  const revision = state.preparation.revision;
  await page.click('#releaseDraftPublish');
  await page.locator('#releaseDraftStatus').getByText('Đang phân phối', { exact: true }).waitFor();
  assert.equal(writes(calls).at(-1).body.revision, revision);
  assert.match(await page.locator('#releaseDraftMessage').textContent(), /thông tin phiên bản/);
  assert.equal(calls.some(call => call.path.startsWith(base)), false);
  assert.equal(await page.locator('#releaseDraftVersion').isDisabled(), true);
});

test('direct catalog rejects a noncanonical or mismatched ZIP filename before saving and keeps field errors accessible', async t => {
  const { page, calls } = await fixture(t, { direct: true });
  await fillNew(page); await page.fill('#releaseDraftNotes', 'Giữ ghi chú');
  await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftErrors:not([hidden])').waitFor();
  assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseDraftErrors');
  assert.equal(await page.locator('#releaseDraftErrors a').getAttribute('href'), '#releaseDraftUrl');
  await page.fill('#releaseDraftUrl', directZipUrl.replace('VideoMaker-1.2.0-', 'VideoMaker-1.3.0-'));
  await page.click('#releaseDraftSave');
  assert.equal(await page.inputValue('#releaseDraftNotes'), 'Giữ ghi chú');
  assert.deepEqual(writes(calls), []);
});

test('direct catalog requires the compatible shared Setup and does not present missing inspection as a prerequisite', async t => {
  const { page } = await fixture(t, { direct: true, preparation: directDraft({ canPublish: false, bootstrapper: null,
    errorCode: 'desktop_direct_bootstrapper_required', errorMessage: 'Cần Setup dùng chung từ 1.2.0 hỗ trợ cài trực tiếp.' }) });
  await openExisting(page);
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  assert.equal(await page.locator('#releaseDraftSetupLink').isVisible(), true);
  assert.match(await page.locator('#releaseDraftPublishHint').textContent(), /1\.2\.0/);
  assert.doesNotMatch(await page.locator('#releaseDraftDialog').innerText(), /Kiểm tra đạt|Đang tải gói|Hủy kiểm tra/);
});

test('direct catalog preserves edits on CAS conflict and reloads the declared record without starting a job', async t => {
  const { page, calls, state } = await fixture(t, { direct: true, preparation: directDraft() });
  await openExisting(page); await page.fill('#releaseDraftNotes', 'Chưa lưu'); state.conflict = true;
  await page.click('#releaseDraftSave'); await page.locator('#releaseDraftReload:not([hidden])').waitFor();
  assert.equal(await page.inputValue('#releaseDraftNotes'), 'Chưa lưu');
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  state.conflict = false; state.preparation.releaseNotes = 'Từ phiên khác';
  await page.click('#releaseDraftReload');
  await page.waitForFunction(() => document.getElementById('releaseDraftNotes').value === 'Từ phiên khác');
  assert.equal(writes(calls).length, 1);
  assert.equal(calls.some(call => /\/retry$|\/cancel$/.test(call.path)), false);
});

test('direct catalog retries an uncertain create with one request identity and keeps declaration inactive until publish', async t => {
  const { page, calls, state } = await fixture(t, { direct: true, loseCreateResponse: true });
  await fillNew(page); await page.fill('#releaseDraftUrl', directZipUrl); await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftErrors:not([hidden])').waitFor(); await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftStatus').getByText('Đã lưu nháp', { exact: true }).waitFor();
  const creates = writes(calls).filter(call => call.path === directBase);
  assert.equal(creates.length, 2); assert.equal(creates[0].body.requestId, creates[1].body.requestId);
  assert.equal(state.preparation.isActive, false);
});

test('direct catalog deactivation and republication retain the immutable URL and only change distribution status', async t => {
  const { page, calls, state } = await fixture(t, { direct: true, preparation: directDraft({ status: 'Published', isActive: true, canPublish: false }) });
  await openExisting(page);
  assert.equal(await page.locator('#releaseDraftVersion').isDisabled(), true);
  assert.equal(await page.locator('#releaseDraftUrl').isDisabled(), true);
  await page.click('#releaseDraftDeactivate'); await page.click('#adminActionConfirm');
  await page.locator('#releaseDraftStatus').getByText('Đã ngừng phân phối', { exact: true }).waitFor();
  await page.click('#releaseDraftPublish');
  await page.locator('#releaseDraftStatus').getByText('Đang phân phối', { exact: true }).waitFor();
  assert.equal(state.preparation.downloadUrl, directZipUrl);
  assert.deepEqual(writes(calls).map(call => call.path), [directBase + '/' + id + '/deactivate', directBase + '/' + id + '/publish']);
});

test('standalone saves and publishes without any Setup registration or inspection call', async t => {
  const { page, calls } = await fixture(t, { standalone: true });
  assert.equal(await page.locator('#manageBootstrapperButton').isHidden(), true);
  assert.equal(await page.locator('#releaseTable').isHidden(), true);
  assert.equal(await page.locator('#releaseCount').isHidden(), true);
  await fillNew(page); await page.fill('#releaseDraftUrl', directZipUrl);
  await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftStatus').getByText('Đã lưu nháp', { exact: true }).waitFor();
  assert.equal(await page.locator('#releaseDraftPublish').isEnabled(), true);
  assert.equal(await page.locator('#releaseDraftSetupLink').isHidden(), true);
  if (process.env.VIDEOMAKER_UI_EVIDENCE_ROOT) {
    fs.mkdirSync(process.env.VIDEOMAKER_UI_EVIDENCE_ROOT, { recursive: true });
    for (const width of [390, 1440]) {
      await page.setViewportSize({ width, height: 1000 });
      assert.equal(await page.locator('#releaseDraftForm').evaluate(node => node.scrollWidth <= node.clientWidth + 1), true);
      await page.locator('#releaseDraftDialog').screenshot({ path: path.join(process.env.VIDEOMAKER_UI_EVIDENCE_ROOT, `standalone-release-${width}.png`) });
    }
  }
  await page.click('#releaseDraftPublish');
  await page.locator('#releaseDraftStatus').getByText('Đang phân phối', { exact: true }).waitFor();
  assert.deepEqual(writes(calls).map(call => [call.path, call.method]), [[standaloneBase, 'POST'], [standaloneBase + '/' + id + '/publish', 'POST']]);
  assert.equal(calls.some(call => call.path === setupBase || call.path.startsWith(base) || call.path.startsWith(directBase)), false);
  assert.doesNotMatch(await page.locator('#releaseDraftDialog').innerText(), /Active|1\.2\.0 hỗ trợ|Kiểm tra đạt|Đang tải gói/);
});

test('standalone metadata save uses POST and reports HTML 403 without dumping HTML or losing edits', async t => {
  const { page, calls, state } = await fixture(t, { standalone: true, preparation: directDraft({ bootstrapper: null }) });
  await openExisting(page); await page.fill('#releaseDraftNotes', 'Ghi chú cần giữ'); state.forbiddenSave = true;
  await page.click('#releaseDraftSave');
  await page.locator('#releaseDraftErrors:not([hidden])').waitFor();
  assert.match(await page.textContent('#releaseDraftErrors'), /HTTP 403/);
  assert.doesNotMatch(await page.textContent('#releaseDraftErrors'), /DOCTYPE|<html|<h1/);
  assert.equal(await page.inputValue('#releaseDraftNotes'), 'Ghi chú cần giữ');
  assert.equal(await page.locator('#releaseDraftPublish').isDisabled(), true);
  assert.deepEqual(writes(calls).map(call => [call.path, call.method]), [[standaloneBase + '/' + id + '/save', 'POST']]);
  state.forbiddenSave = false; await page.click('#releaseDraftSave');
  await page.waitForFunction(() => document.getElementById('releaseDraftMessage').textContent.includes('Đã lưu'));
  await page.waitForFunction(() => document.getElementById('releaseDraftForm').getAttribute('aria-busy') === 'false');
  assert.equal(await page.locator('#releaseDraftPublish').isEnabled(), true);
});

test('direct declaration remains readable and keyboard accessible at narrow widths without technical measurements', async t => {
  const { page } = await fixture(t, { direct: true, width: 320, preparation: directDraft() });
  await openExisting(page);
  assert.equal(await page.locator('#releaseDraftForm').evaluate(node => node.scrollWidth <= node.clientWidth + 1), true);
  assert.equal(await page.locator('#releaseDraftTechnical').isVisible(), false);
  await page.locator('#releaseDraftPublish').focus(); await page.keyboard.press('Shift+Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseDraftSave');
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('#releaseDraftDialog').evaluate(node => node.open), false);
});
