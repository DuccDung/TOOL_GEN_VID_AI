// Actual Admin assets, synthetic HTTP/file fixtures; never contacts a server or VPS.
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
const setupBytes = Buffer.from('MZ synthetic Setup browser fixture');
const setup = { fileName: 'VideoMaker Setup.exe', sizeBytes: setupBytes.length, sha256: crypto.createHash('sha256').update(setupBytes).digest('hex') };
const registration = () => ({ schemaVersion: 1, version: '1.2.0', buildNumber: 37, channel: 'Beta', platform: 'win-x64',
  downloadUrl: 'https://103.179.190.182:8443/apps/videomaker/1.2.0-37/VideoMaker.zip', fileName: 'VideoMaker.zip',
  sizeBytes: 1024, sha256: 'a'.repeat(64), expandedSizeBytes: 2048, entryCount: 3,
  packageManifest: { product: 'VideoMaker', version: '1.2.0', buildNumber: 37, platform: 'win-x64', managedFiles: ['TOOL-LOCAL.exe', 'update-manifest.json', 'package-inventory.json'] },
  setup, installMode: 'StaticArchive', distributionProtocol: 2, modelBundleId: 'models-20261016-03' });
const paged = items => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0 });
let browser;
before(async () => { browser = await chromium.launch({ headless: true, channel: process.env.VIDEOMAKER_BROWSER_CHANNEL }); });
after(async () => { await browser?.close(); });

async function fixture(t, options = {}) {
  const context = await browser.newContext({ viewport: { width: options.width || 1440, height: 1100 }, locale: 'vi-VN' });
  t.after(() => context.close());
  const page = await context.newPage(), calls = [], errors = [], outbound = [];
  page.setDefaultTimeout(10000);
  const state = { failRegistration: options.failRegistration || false, holdRegistration: options.holdRegistration || false, release: options.release || null };
  let unblock, entered;
  const held = new Promise(resolve => { entered = resolve; });
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url()), method = request.method();
    if (url.origin !== 'https://admin.example.test') { outbound.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    const json = request.headers()['content-type']?.startsWith('application/json') ? request.postDataJSON() : null;
    calls.push({ path: url.pathname, method, body: json });
    let body;
    if (url.pathname === '/api/admin/licenses/overview') body = { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 };
    else if (url.pathname === '/api/admin/licenses/plans') body = [];
    else if (url.pathname === '/api/admin/licenses/users/page') body = paged([]);
    else if (url.pathname === '/api/admin/desktop-releases/page') body = paged(state.release ? [state.release] : []);
    else if (url.pathname === '/api/admin/desktop-releases' && method === 'POST') {
      state.release = { ...json, releaseId: 'release-1', artifacts: [], revision: 'revision-create', publishedAtUtc: '2026-10-17T00:00:00Z' }; body = state.release;
    } else if (url.pathname === '/api/admin/desktop-releases/release-1' && method === 'GET') body = state.release;
    else if (url.pathname === '/api/admin/desktop-releases/release-1' && method === 'PUT') {
      assert.equal(json.revision, state.release.revision);
      state.release = { ...state.release, ...json, revision: 'revision-save' }; body = state.release;
    } else if (url.pathname.endsWith('/artifacts/DesktopPackage/external')) {
      if (state.holdRegistration) { entered(); await new Promise(resolve => { unblock = resolve; }); }
      if (state.failRegistration) return route.fulfill({ status: 400, contentType: 'application/json', body: JSON.stringify({ code: 'external_source_invalid', message: 'Nguồn VPS chưa được duyệt.' }) });
      const artifact = { kind: 'DesktopPackage', sourceKind: 'ExternalVps', fileName: json.fileName, sizeBytes: json.sizeBytes, sha256: json.sha256, downloadUrl: json.downloadUrl,
        installMode: 'StaticArchive', distributionProtocol: 2, metadataRegisteredOnly: true };
      state.release.artifacts = [artifact]; state.release.revision = 'revision-register'; body = artifact;
    } else if (/\/artifacts\/(Setup|DesktopPackage)$/.test(url.pathname)) {
      const kind = url.pathname.split('/').at(-1); const artifact = { kind, sourceKind: 'Internal', ...setup };
      state.release.artifacts.push(artifact); state.release.revision = 'revision-upload-' + kind; body = artifact;
    } else if (url.pathname.endsWith('/publish')) {
      assert.equal(json.revision, state.release.revision);
      state.release.isActive = true; state.release.firstPublishedAtUtc = '2026-10-17T01:00:00Z'; body = state.release;
    } else return route.fulfill({ status: 404, body: '{}' });
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-admin-test');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ displayName: 'Admin QA', roles: ['Admin'] }));
  });
  await page.goto('https://admin.example.test/');
  await page.addStyleTag({ content: withAdminFonts(read('TOOL-SERVER/wwwroot/admin/admin.css')) });
  await page.addStyleTag({ content: read('TOOL-SERVER/wwwroot/admin/admin-modules.css') });
  for (const name of ['admin-pages.js', 'admin-dialogs.js', 'admin.js']) await page.addScriptTag({ content: read('TOOL-SERVER/wwwroot/admin/' + name) });
  await page.waitForSelector('#adminShell:not(.hidden)');
  if (await page.locator('#adminMenuToggle').isVisible()) await page.locator('#adminMenuToggle').click();
  await page.locator('[data-view="releases"]').click();
  t.after(() => { assert.deepEqual(errors, []); assert.deepEqual(outbound, []); });
  return { page, calls, state, held, unblock: () => unblock?.() };
}

async function openImported(page, data = registration()) {
  await page.evaluate(() => window.videoMakerAdminShell.openLegacyRelease());
  await page.selectOption('#releaseSource', 'ExternalVps');
  await page.setInputFiles('#releaseRegistrationFile', { name: 'external-registration.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(data)) });
  await page.waitForFunction(() => document.getElementById('releaseVersion').value === '1.2.0');
}
async function selectSetup(page, bytes = setupBytes) {
  await page.setInputFiles('#releaseSetup', { name: setup.fileName, mimeType: 'application/octet-stream', buffer: bytes });
}
const writes = calls => calls.filter(call => call.method !== 'GET');

test('external import displays binding without fetching files or claiming QA verification', async t => {
  const { page, calls } = await fixture(t);
  await openImported(page);
  assert.equal(await page.inputValue('#releaseExternalHash'), 'a'.repeat(64));
  assert.equal(await page.inputValue('#releaseBuild'), '37');
  assert.match(await page.locator('#releaseImportedSetup').textContent(), /Chưa kiểm file trên VPS/);
  assert.equal(await page.locator('#releasePackage').isDisabled(), true);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1100 });
    const bounds = await page.locator('#releaseDialog').boundingBox();
    assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= width + 1);
    assert.equal(await page.locator('#releaseForm').evaluate(form => form.scrollWidth <= form.clientWidth + 1), true);
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
      fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
      await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => setTimeout(resolve, 220)); });
      await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `external-release-${width}.png`), fullPage: true });
    }
    await page.locator('#releasePublish').focus();
    await page.keyboard.press('Shift+Tab');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseSaveDraft');
    const actionBounds = await page.locator('#releaseSaveDraft').boundingBox();
    assert.ok(actionBounds.y >= 0 && actionBounds.y + actionBounds.height <= 1101);
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR) await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `external-release-actions-${width}.png`), fullPage: true });
    await page.locator('#releaseDialog').evaluate(dialog => { dialog.scrollTop = 0; });
  }
  assert.deepEqual(writes(calls), []);
});

test('publish creates draft then registers ZIP, uploads exact Setup and publishes with fresh revision', async t => {
  const { page, calls } = await fixture(t);
  await openImported(page); await selectSetup(page);
  await page.locator('#releasePublish').click();
  await page.waitForFunction(() => !document.getElementById('releaseDialog').open);
  const mutations = writes(calls);
  assert.deepEqual(mutations.map(call => call.path), ['/api/admin/desktop-releases', '/api/admin/desktop-releases/release-1/artifacts/DesktopPackage/external', '/api/admin/desktop-releases/release-1/artifacts/Setup', '/api/admin/desktop-releases/release-1/publish']);
  assert.equal(mutations[0].body.isActive, false);
  assert.deepEqual(mutations[1].body.setup, setup);
  assert.equal(mutations[3].body.revision, 'revision-upload-Setup');
  await page.locator('#releaseTable').getByText('Metadata VPS đã đăng ký.', { exact: false }).waitFor();
  assert.equal(await page.locator('#releaseTable').getByText('Metadata VPS đã đăng ký.', { exact: false }).count(), 1);
});

test('registration failure preserves fields, focuses error and retries the same draft', async t => {
  const { page, calls, state } = await fixture(t, { failRegistration: true });
  await openImported(page); await selectSetup(page);
  await page.locator('#releaseSaveDraft').click();
  await page.waitForSelector('#releaseErrorSummary:not([hidden])');
  assert.equal(await page.inputValue('#releaseId'), 'release-1');
  assert.equal(await page.inputValue('#releaseExternalUrl'), registration().downloadUrl);
  assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseErrorSummary');
  await page.locator('#releaseErrorSummary a').click();
  assert.equal(await page.evaluate(() => document.activeElement.id), 'releaseExternalUrl');
  state.failRegistration = false;
  await page.locator('#releaseSaveDraft').click();
  await page.waitForFunction(() => !document.getElementById('releaseDialog').open);
  assert.equal(writes(calls).filter(call => call.path === '/api/admin/desktop-releases').length, 1);
  assert.equal(writes(calls).filter(call => call.path.endsWith('/publish')).length, 0);
});

test('wrong Setup bytes fail before creating a release and retain the imported draft', async t => {
  const { page, calls } = await fixture(t);
  await openImported(page); await selectSetup(page, Buffer.from('MZ mismatched fixture'));
  await page.locator('#releasePublish').click();
  await page.waitForSelector('#releaseErrorSummary:not([hidden])');
  assert.match(await page.locator('#releaseErrorSummary').textContent(), /không khớp/);
  assert.deepEqual(writes(calls), []);
  assert.equal(await page.inputValue('#releaseVersion'), '1.2.0');
});

test('invalid import does not overwrite current fields and required fields link to error summary', async t => {
  const { page, calls } = await fixture(t);
  await page.evaluate(() => window.videoMakerAdminShell.openLegacyRelease());
  await page.fill('#releaseVersion', '2.0.0'); await page.selectOption('#releaseSource', 'ExternalVps');
  await page.setInputFiles('#releaseRegistrationFile', { name: 'bad.json', mimeType: 'application/json', buffer: Buffer.from('{"verified":true}') });
  await page.waitForSelector('#releaseErrorSummary:not([hidden])');
  assert.equal(await page.inputValue('#releaseVersion'), '2.0.0');
  await page.locator('#releasePublish').click();
  assert.equal(await page.locator('#releaseErrorSummary a').getAttribute('href'), '#releaseExternalUrl');
  assert.deepEqual(writes(calls), []);
});

test('busy submission blocks duplicate submit, close and Escape until operation finishes', async t => {
  const current = await fixture(t, { holdRegistration: true });
  const { page, calls } = current;
  await openImported(page); await selectSetup(page); await page.locator('#releasePublish').click();
  await current.held;
  assert.equal(await page.locator('#releaseSaveDraft').isDisabled(), true);
  assert.equal(await page.locator('#releaseForm [data-close]').first().isDisabled(), true);
  await page.evaluate(() => document.getElementById('releaseForm').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('#releaseDialog').evaluate(node => node.open), true);
  assert.equal(writes(calls).filter(call => call.path.endsWith('/external')).length, 1);
  current.unblock(); await page.waitForFunction(() => !document.getElementById('releaseDialog').open);
});

test('published external identity remains readonly even after it is deactivated', async t => {
  const data = registration();
  const release = { releaseId: 'release-1', ...data, revision: 'published-revision', isActive: false,
    firstPublishedAtUtc: '2026-10-16T00:00:00Z', publishedAtUtc: '2026-10-16T00:00:00Z',
    artifacts: [{ kind: 'DesktopPackage', sourceKind: 'ExternalVps', ...data }, { kind: 'Setup', ...setup }] };
  const { page, calls } = await fixture(t, { release });
  await page.locator('[data-edit-release="release-1"]').click();
  for (const id of ['releaseVersion', 'releaseBuild', 'releaseSource', 'releaseExternalUrl', 'releaseSetup']) assert.equal(await page.locator('#' + id).isDisabled(), true);
  assert.equal(await page.locator('#releaseIdentityNotice').isVisible(), true);
  await page.locator('#releaseSaveDraft').click(); await page.waitForFunction(() => !document.getElementById('releaseDialog').open);
  assert.deepEqual(writes(calls).map(call => call.method), ['PUT']);
});

test('internal source keeps upload route and saving a draft never publishes', async t => {
  const { page, calls } = await fixture(t, { width: 390 });
  await page.evaluate(() => window.videoMakerAdminShell.openLegacyRelease());
  await page.fill('#releaseVersion', '1.2.0');
  await page.setInputFiles('#releasePackage', { name: 'fixture.zip', mimeType: 'application/zip', buffer: Buffer.from('PK synthetic') });
  await page.locator('#releaseSaveDraft').click(); await page.waitForFunction(() => !document.getElementById('releaseDialog').open);
  assert.deepEqual(writes(calls).map(call => call.path), ['/api/admin/desktop-releases', '/api/admin/desktop-releases/release-1/artifacts/DesktopPackage']);
});
