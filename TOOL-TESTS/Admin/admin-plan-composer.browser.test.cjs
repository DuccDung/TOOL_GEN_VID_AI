// Production markup/CSS/JS, synthetic HTTP only. No server, database or provider is contacted.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { withAdminFonts } = require('./admin-browser-assets.cjs');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const razor = read('TOOL-SERVER/Pages/Admin/Index.cshtml');
const html = razor.slice(razor.indexOf('<!doctype')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
const poolId = '00000000-0000-0000-0000-000000000010';
const sourceId = '00000000-0000-0000-0000-000000000020';
const source = { licensePlanId: sourceId, name: 'Gói nguồn', planCode: 'source', defaultDurationDays: 30,
  salePriceVnd: 70000, maxActivatedDevices: 3, maxConcurrentSessions: 2, offlineGraceHours: 12,
  description: 'Quyền lợi gói nguồn', marketingFeaturesJson: '["Quyền lợi riêng"]', featureFlagsJson: '{"customEntitlement":false}',
  isActive: true, isPublic: true, displayOrder: 2, updatedAtUtc: '2026-10-13T00:00:00Z' };
const readyGroup = { organizationPoolId: poolId, name: 'veo producttion', availableSeats: 10, ready: true };
let browser;
before(async () => { browser = await chromium.launch({ headless: true }); });
after(async () => { await browser?.close(); });

test('Local publishes with zero groups, hides allocation and still links to separate SePay settings', async t => {
  const { page, calls, errors } = await fixture(t, { groups: [] });
  await open(page); await values(page); await page.selectOption('#composerProduct', 'vietsub');
  assert.equal(await page.locator('#composerRouting').isVisible(), false);
  assert.equal(await page.locator('.composer-shared').isVisible(), false);
  assert.equal(await page.locator('#composerPublish').isEnabled(), true);
  assert.match(await page.textContent('#composerPreviewSeats'), /Không cần tổ chức/);
  await page.check('#composerCloud');
  assert.equal(await page.locator('#composerRouting').isVisible(), true);
  assert.equal(await page.locator('#composerPublish').isDisabled(), true);
  await page.uncheck('#composerCloud');
  await page.click('#composerPublish'); await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls)[0].body.organizationPoolId, null);
  assert.deepEqual(errors, []);
});

test('Local with SePay disabled keeps its settings link visible without the allocation card', async t => {
  const { page, calls } = await fixture(t, { groups: [], paymentsReady: false });
  await open(page); await values(page); await page.selectOption('#composerProduct', 'vietsub');
  assert.equal(await page.locator('#composerRouting').isVisible(), false);
  assert.equal(await page.locator('#composerPublish').isDisabled(), true);
  assert.equal(await page.locator('#composerPaymentSetup').isVisible(), true);
  await page.click('#composerPaymentSetup');
  await page.waitForSelector('[data-panel="payments"]:not(.hidden)');
  assert.equal(posts(calls).length, 0);
});

async function mount(page, expectLogin = false) {
  for (const file of ['admin.css', 'admin-modules.css', 'admin-plan-composer.css'])
    await page.addStyleTag({ content: withAdminFonts(read('TOOL-SERVER/wwwroot/admin/' + file)) });
  for (const file of ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-plan-composer.js'])
    await page.addScriptTag({ content: read('TOOL-SERVER/wwwroot/admin/' + file) });
  if (expectLogin) { await page.waitForSelector('#loginScreen:not(.hidden)'); return; }
  await page.waitForSelector('#overviewMetrics .home-stat', { state: 'attached' });
  await page.evaluate(() => window.videoMakerAdminShell.navigate('plans'));
}
async function fixture(t, options = {}) {
  const context = await browser.newContext({ viewport: { width: options.width || 1440, height: options.height || 1000 } });
  t.after(() => context.close());
  const page = await context.newPage(), calls = [], saved = new Map(), errors = [];
  page.on('pageerror', error => errors.push(error.message));
  let attempts = 0, setupAttempts = 0, overviewAttempts = 0;
  const respond = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
  await page.route('**/*', async route => {
    const url = new URL(route.request().url()), method = route.request().method();
    if (url.hostname !== 'admin.example.test') return route.abort();
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    calls.push({ path: url.pathname, method, body: route.request().postDataJSON() });
    if (url.pathname.endsWith('/overview')) {
      if (options.expiredSession && overviewAttempts++ === 0) return respond(route, { message: 'Phiên đăng nhập hết hạn.' }, 401);
      return respond(route, { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 });
    }
    if (url.pathname === '/api/auth/login') return respond(route, { accessToken: 'synthetic-new-session', refreshToken: 'synthetic-refresh',
      user: { userId: 'admin-test', email: 'admin@example.test', displayName: 'Quản trị viên', roles: ['Admin'] } });
    if (url.pathname === '/api/admin/licenses/plans') return respond(route, [source, ...[...saved.values()].map(value => value.plan)]);
    if (url.pathname.endsWith('/page')) return respond(route, { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    if (url.pathname === '/api/auth/logout') return respond(route, {});
    if (url.pathname.endsWith('/plan-composer/setup')) {
      setupAttempts++;
      if (options.setupDelay) await new Promise(resolve => setTimeout(resolve, options.setupDelay));
      if (options.setupFailure && setupAttempts === 1) return respond(route, { message: 'Không tải được thiết lập.' }, 503);
      return respond(route, { paymentsReady: options.paymentsReady ?? true, paymentMessage: 'Thanh toán SePay chưa sẵn sàng.',
        groups: options.groups ?? [readyGroup] });
    }
    if (url.pathname === '/api/admin/licenses/plan-composer/' + sourceId) return respond(route, { plan: options.editorPlan || source, organizationPoolId: poolId, memberMonthlyAiBudget: 12, mappingUpdatedAtUtc: '2026-10-13T00:00:00Z' });
    if (url.pathname === '/api/admin/licenses/plan-composer' && method === 'POST') {
      attempts++;
      const request = route.request().postDataJSON();
      if (options.saveDelay) await new Promise(resolve => setTimeout(resolve, options.saveDelay));
      if (options.rejected) return respond(route, { code: 'organization_capacity_unavailable', message: 'Nhóm đã hết chỗ nhận khách.' }, 409);
      const replay = saved.get(request.creationId);
      if (replay) { assert.deepEqual(request, replay.request); return respond(route, { ...replay, replayed: true }); }
      const plan = { ...source, licensePlanId: request.planId || request.creationId, name: request.name, defaultDurationDays: request.durationDays,
        salePriceVnd: request.priceVnd, isActive: request.isActive ?? request.publish, isPublic: request.publish };
      const result = { plan, published: request.publish, organizationPoolId: request.organizationPoolId, replayed: false };
      saved.set(request.creationId, { ...result, request });
      if (options.loseFirstResponse && attempts === 1) return route.abort('failed');
      return respond(route, result);
    }
    return respond(route, { message: 'Unmocked endpoint ' + url.pathname }, 404);
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-admin-session');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ userId: 'admin-test', email: 'admin@example.test', displayName: 'Quản trị viên', roles: ['Admin'] }));
  });
  await page.goto('https://admin.example.test/' + (options.directComposer ? '?view=create-plan' : ''));
  await mount(page, options.expiredSession);
  return { page, calls, saved, errors };
}
async function open(page) {
  if (!(await page.locator('#licensePlanComposer').isVisible())) await page.click('#addPlanButton');
  await page.waitForFunction(() => !document.getElementById('composerReload').disabled);
}
async function values(page) { await page.fill('#composerName', 'Gói 30 ngày'); await page.fill('#composerPrice', '70000'); }
const posts = calls => calls.filter(call => call.path.endsWith('/plan-composer') && call.method === 'POST');

const translationGroup = {
  ...readyGroup, ready: false, availableSeats: 0, blockingMessage: 'Chưa có OpenAI và Fal.',
  vietsubLocal: { ready: true, availableSeats: 10 },
  vietsubCloud: { ready: false, availableSeats: 0, blockingMessage: 'Chưa có credential OpenAI Active cho dịch Cloud.' }
};

test('Cloud correction opens the exact organization and model and supplies a return action', async t => {
  const target = { organizationId: 'org-fixture', providerCode: 'openai', modelId: 'model-fixture', target: 'pricing' };
  const { page } = await fixture(t, { groups: [{ ...translationGroup, vietsubCloud: {
    ready: false, availableSeats: 0, blockingMessage: 'Thiếu giá model dịch.', setupTarget: target } }] });
  await open(page); await values(page); await page.selectOption('#composerProduct', 'vietsub'); await page.check('#composerCloud');
  await page.evaluate(() => { window.videoMakerOrganizationAdmin = { openSetupTarget: async (target, options) => {
    window.testCorrection = { target, canReturn: typeof options.returnTo.restore === 'function' };
  } }; });
  await page.click('#composerCloudSetup');
  const correction = await page.evaluate(() => window.testCorrection);
  assert.deepEqual(correction.target, target); assert.equal(correction.canReturn, true);
});

test('Cloud with a zero member quota cannot publish even when organization metadata is ready', async t => {
  const { page, calls } = await fixture(t, { groups: [{ ...translationGroup, vietsubCloud: { ready: true, availableSeats: 6 } }] });
  await open(page); await values(page); await page.selectOption('#composerProduct', 'vietsub'); await page.check('#composerCloud');
  await page.fill('#composerMemberBudget', '0');
  assert.equal(await page.isDisabled('#composerPublish'), true);
  assert.match(await page.locator('#composerStatus').textContent(), /bằng 0 sẽ khóa dịch Cloud/);
  assert.equal(posts(calls).length, 0);
});

test('translation Local form publishes from a group without AI readiness and sends real entitlements', async t => {
  const { page, calls, errors } = await fixture(t, { groups: [translationGroup] });
  await open(page); await values(page);
  assert.equal(await page.locator('#composerPublish').isDisabled(), true);
  await page.selectOption('#composerProduct', 'vietsub');
  assert.equal(await page.textContent('#planComposerTitle'), 'Tạo gói tool dịch');
  assert.equal(await page.locator('#composerPublish').isEnabled(), true);
  assert.equal(await page.locator('#composerCloud').isChecked(), false);
  assert.equal(await page.locator('#composerMemberBudgetField').isVisible(), false);
  assert.match(await page.textContent('#composerPreviewFeatures'), /Dịch Local/);
  assert.doesNotMatch(await page.textContent('#composerPreviewFeatures'), /Tạo và dựng video bằng AI/);
  await page.click('#composerPublish');
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  const request = posts(calls)[0].body;
  assert.equal(request.productScope, 'vietsub'); assert.equal(request.vietsubCloudEnabled, false);
  assert.equal(request.organizationPoolId, null); assert.equal(request.memberMonthlyAiBudget, 0); assert.ok(request.marketingFeatures.includes('Dịch Local trên máy'));
  assert.deepEqual(errors, []);
});

test('Cloud switch reveals settings and its own blocker without losing the form or writing', async t => {
  const { page, calls } = await fixture(t, { groups: [translationGroup] });
  await open(page); await values(page); await page.selectOption('#composerProduct', 'vietsub');
  await page.locator('#composerCloud').focus(); await page.keyboard.press('Space');
  assert.equal(await page.locator('#composerCloudSettings').isVisible(), true);
  assert.equal(await page.locator('#composerMemberBudgetField').isVisible(), true);
  assert.equal(await page.locator('#composerPublish').isDisabled(), true);
  assert.match(await page.textContent('#composerGroupStatus'), /credential OpenAI/);
  assert.equal(await page.inputValue('#composerName'), 'Gói 30 ngày');
  await page.uncheck('#composerCloud');
  assert.equal(await page.locator('#composerPublish').isEnabled(), true);
  assert.equal(posts(calls).length, 0);
});

test('Cloud-enabled package sends explicit Cloud permission and configured quota', async t => {
  const { page, calls } = await fixture(t, { groups: [{ ...translationGroup, vietsubCloud: { ready: true, availableSeats: 6 } }] });
  await open(page); await values(page); await page.selectOption('#composerProduct', 'vietsub'); await page.check('#composerCloud');
  await page.locator('.composer-advanced summary').click(); await page.fill('#composerMemberBudget', '5');
  await page.click('#composerPublish'); await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls)[0].body.vietsubCloudEnabled, true); assert.equal(posts(calls)[0].body.memberMonthlyAiBudget, 5);
});

test('editing translation package preserves immutable rights and server snapshots', async t => {
  const editorPlan = { ...source, featureFlagsJson: '{"productScope":"vietsub","vietsubCloudEnabled":false}' };
  const { page, calls } = await fixture(t, { groups: [translationGroup], editorPlan });
  await page.evaluate(plan => window.videoMakerPlanComposer.edit(plan), editorPlan);
  await page.waitForFunction(() => !document.getElementById('composerPublish').disabled);
  assert.equal(await page.locator('#composerProduct').isDisabled(), true);
  assert.equal(await page.locator('#composerCloud').isDisabled(), true);
  await page.fill('#composerName', 'Tên gói dịch đã sửa'); await page.click('#composerPublish');
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls)[0].body.productScope, undefined);
  assert.equal(posts(calls)[0].body.vietsubCloudEnabled, undefined);
});

for (const width of [390, 1440, 1792]) test('translation form matches the mockup without overflow at ' + width, async t => {
  const { page, errors } = await fixture(t, { groups: [translationGroup], width, height: 1100 });
  await open(page); await values(page); await page.selectOption('#composerProduct', 'vietsub');
  await page.fill('#composerName', 'Vietsub Local 30 ngày');
  assert.equal(await page.locator('.composer-section:visible').count(), 2);
    assert.equal(await page.locator('#composerRouting').isVisible(), false);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
    fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'vietsub-plan-' + width + '.png'), fullPage: true });
  }
  assert.deepEqual(errors, []);
});

test('five fields, real group capacity and safe live preview match the new flow', async t => {
  const { page, calls, errors } = await fixture(t);
  await open(page); await values(page);
  assert.equal(await page.inputValue('#composerGroup'), poolId);
  assert.equal(await page.locator('#composerPublish').isEnabled(), true);
  assert.match(await page.textContent('#composerPreviewPrice'), /70\.000/);
  assert.match(await page.textContent('#composerGroupStatus'), /Còn 10 chỗ/);
  assert.match(await page.textContent('.composer-shared'), /dùng chung/);
  await page.fill('#composerName', '<img src=x onerror=alert(1)>');
  assert.equal(await page.locator('#composerPreviewName img').count(), 0);
  assert.equal(posts(calls).length, 0);
  assert.deepEqual(errors, []);
});

test('formatted price supports typing and editing without changing the integer sent to the server', async t => {
  const { page, calls } = await fixture(t);
  await open(page); await page.fill('#composerName', 'Gói 30 ngày');
  await page.locator('#composerPrice').pressSequentially('70000');
  await page.locator('#planComposerTitle').click();
  assert.equal(await page.inputValue('#composerPrice'), '70.000');
  await page.locator('#composerPrice').press('Home');
  await page.locator('#composerPrice').pressSequentially('1');
  await page.locator('#planComposerTitle').click();
  assert.equal(await page.inputValue('#composerPrice'), '170.000');
  assert.match(await page.textContent('#composerPreviewPrice'), /170\.000/);
  await page.click('#composerPublish');
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls).length, 1);
  assert.equal(posts(calls)[0].body.priceVnd, 170000);
});

test('invalid monetary input blocks writes and corrected formatted input can be saved', async t => {
  const { page, calls } = await fixture(t);
  await open(page); await page.fill('#composerName', 'Gói hợp lệ');
  for (const value of ['0', '-70000', '70000.5', '7e4', '70.00', 'abc', '1000000000001']) {
    await page.fill('#composerPrice', value); await page.click('#composerPublish');
    assert.equal(posts(calls).length, 0, value + ' must never reach the API');
    assert.equal(await page.locator('#composerPrice').evaluate(input => input.validity.customError), true);
  }
  await page.fill('#composerPrice', '70.000'); await page.click('#composerPublish');
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls)[0].body.priceVnd, 70000);
});

test('desktop reference viewport shows save controls and aligns the form with the breadcrumb', async t => {
  const { page } = await fixture(t, { width: 1672, height: 941 });
  await open(page); await values(page);
  await page.evaluate(async () => { await document.fonts.ready; document.activeElement?.blur(); scrollTo(0, 0); });
  const layout = await page.evaluate(() => ({
    saveBottom: document.getElementById('composerPublish').getBoundingClientRect().bottom,
    boxes: ['.composer-status','.composer-form','.composer-preview','.composer-actions'].map(selector => ({selector,height:document.querySelector(selector).getBoundingClientRect().height})),
    viewportHeight: innerHeight,
    fontLoaded: document.fonts.check('18px "VideoMaker Roboto"', 'Gói sử dụng'),
    overflow: document.documentElement.scrollWidth > innerWidth,
    aligned: Math.abs(document.querySelector('.admin-breadcrumb').getBoundingClientRect().x - document.querySelector('.composer-form').getBoundingClientRect().x) < 1
  }));
  assert.equal(layout.fontLoaded, true);
  assert.equal(layout.overflow, false);
  assert.equal(layout.aligned, true);
  assert.ok(layout.saveBottom <= layout.viewportHeight, JSON.stringify(layout));
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
    fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'plan-composer-reference-1672.png'), fullPage: true });
  }
  await page.setViewportSize({ width: 1920, height: 1080 });
  assert.ok(await page.evaluate(() => Math.abs(document.querySelector('.admin-breadcrumb').getBoundingClientRect().x - document.querySelector('.composer-form').getBoundingClientRect().x) < 1));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ forcedColors: 'active', reducedMotion: 'reduce' });
  await page.locator('#composerDevices').focus();
  await page.keyboard.press('ArrowDown');
  assert.equal(await page.inputValue('#composerDevices'), '2');
  await page.keyboard.press('ArrowUp');
  assert.equal(await page.locator('#composerDevices').evaluate(input => getComputedStyle(input).appearance), 'auto');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'plan-composer-high-contrast.png'), fullPage: true });
});

for (const width of [320, 390, 768, 1366, 1920]) test('responsive layout and keyboard at ' + width, async t => {
  const { page, errors } = await fixture(t, { width });
  await open(page); await values(page);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
  const visual = await page.evaluate(() => ({
    sidebar: getComputedStyle(document.querySelector('.sidebar')).backgroundColor,
    formRadius: getComputedStyle(document.querySelector('.composer-form')).borderTopLeftRadius,
    inputBorder: getComputedStyle(document.getElementById('composerName')).borderTopWidth,
    offer: getComputedStyle(document.querySelector('.composer-offer')).backgroundColor,
    previewButton: getComputedStyle(document.querySelector('.composer-offer > button')).opacity
  }));
  assert.deepEqual(visual, { sidebar: 'rgb(255, 255, 255)', formRadius: '6px', inputBorder: '1px',
    offer: 'rgb(255, 255, 255)', previewButton: '1' });
  assert.equal(await page.locator('#adminSearchForm').isVisible(), false);
  await page.keyboard.press('Tab');
  assert.notEqual(await page.evaluate(() => document.activeElement.tagName), 'BODY');
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
    fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
    await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'plan-composer-' + width + '.png'), fullPage: true });
  }
  assert.deepEqual(errors, []);
});

test('direct create-plan URL opens the actual form without a manual click or a write', async t => {
  const { page, calls, errors } = await fixture(t, { directComposer: true });
  await open(page);
  assert.equal(await page.locator('#licensePlanComposer').isVisible(), true);
  assert.equal(await page.locator('#licensePlanList').isVisible(), false);
  assert.equal(await page.textContent('#planComposerTitle'), 'Tạo gói & mở bán');
  assert.equal(posts(calls).length, 0);
  assert.deepEqual(errors, []);
});

test('direct URL survives an expired session and opens only after signing in again', async t => {
  const { page, calls, errors } = await fixture(t, { directComposer: true, expiredSession: true });
  assert.equal(await page.locator('#licensePlanComposer').isVisible(), false);
  assert.equal(posts(calls).length, 0);
  await page.fill('#loginEmail', 'admin@example.test');
  await page.fill('#loginPassword', 'synthetic-password');
  await page.click('#loginForm button[type=submit]');
  await page.waitForSelector('.composer-status.is-ready');
  assert.equal(await page.locator('#licensePlanComposer').isVisible(), true);
  assert.equal(posts(calls).length, 0);
  assert.deepEqual(errors, []);
});

test('one light shell remains when leaving, returning and closing the form', async t => {
  const { page } = await fixture(t);
  await open(page); await values(page);
  await page.evaluate(() => window.videoMakerAdminShell.navigate('overview'));
  assert.equal(await page.locator('body').evaluate(node => node.classList.contains('plan-composer-open')), false);
  assert.equal(await page.locator('#adminSearchForm').isVisible(), true);
  assert.equal(await page.locator('.sidebar').evaluate(node => getComputedStyle(node).backgroundColor), 'rgb(255, 255, 255)');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('plans'));
  await open(page);
  assert.equal(await page.inputValue('#composerName'), 'Gói 30 ngày');
  assert.equal(await page.locator('.sidebar').evaluate(node => getComputedStyle(node).backgroundColor), 'rgb(255, 255, 255)');
  await page.click('#composerBack');
  assert.equal(await page.locator('body').evaluate(node => node.classList.contains('plan-composer-open')), false);
  assert.equal(await page.evaluate(() => document.activeElement.id), 'addPlanButton');
  assert.equal(new URL(page.url()).searchParams.has('view'), false);
});

test('edit sends one update from the shared form with both revision snapshots', async t => {
  const { page, calls } = await fixture(t);
  await page.locator('[data-edit-plan]').first().click();
  await page.waitForSelector('#composerName:not(:disabled)');
  assert.equal(await page.textContent('#planComposerTitle'), 'Chỉnh sửa gói sử dụng');
  assert.equal(await page.inputValue('#composerMemberBudget'), '12');
  await page.fill('#composerName', 'Gói sửa mới');
  await page.click('#composerPublish');
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls).length, 1);
  assert.equal(posts(calls)[0].body.planId, sourceId);
  assert.equal(posts(calls)[0].body.sourcePlanId, null);
  assert.equal(posts(calls)[0].body.expectedUpdatedAtUtc, source.updatedAtUtc);
  assert.equal(posts(calls)[0].body.expectedMappingUpdatedAtUtc, '2026-10-13T00:00:00Z');
  assert.equal(await page.locator('#planDialog').count(), 0);
});

test('private state stays editable when SePay is unavailable', async t => {
  const { page, calls } = await fixture(t, { paymentsReady: false });
  await page.locator('[data-edit-plan]').first().click();
  await page.waitForSelector('#composerName:not(:disabled)');
  await page.locator('.composer-advanced summary').click();
  await page.selectOption('#composerPlanStatus', 'private');
  assert.equal(await page.locator('#composerPublish').isEnabled(), true);
  await page.click('#composerPublish');
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls)[0].body.publish, false);
  assert.equal(posts(calls)[0].body.isActive, true);
});

test('public sale requires price and duration before any write', async t => {
  const { page, calls } = await fixture(t);
  await open(page); await page.fill('#composerName', 'Gói hợp lệ');
  await page.click('#composerPublish');
  assert.equal(posts(calls).length, 0);
  assert.equal(await page.locator('#composerPrice').evaluate(node => node.validity.valueMissing), true);
  await page.fill('#composerPrice', '70000'); await page.fill('#composerDuration', '');
  await page.click('#composerPublish');
  assert.equal(posts(calls).length, 0);
  assert.equal(await page.locator('#composerDuration').evaluate(node => node.validity.valueMissing), true);
});

test('empty groups allow draft but do not imply ready or publish', async t => {
  const { page, calls } = await fixture(t, { groups: [], paymentsReady: false });
  await open(page);
  assert.match(await page.textContent('#composerStatus'), /Chưa có nhóm/);
  assert.equal(await page.locator('#composerPublish').isDisabled(), true);
  await page.fill('#composerName', 'Gói nháp'); await page.fill('#composerDuration', '');
  await page.click('#composerDraft'); await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls).length, 1);
  assert.equal(posts(calls)[0].body.publish, false);
  assert.equal(posts(calls)[0].body.organizationPoolId, null);
  assert.equal(posts(calls)[0].body.durationDays, null);
});

test('specific group blocker and payment setup remain visible', async t => {
  const { page } = await fixture(t, { groups: [{ ...readyGroup, ready: false, availableSeats: 0, blockingMessage: 'Nhóm chưa có tổ chức nhận khách.' }], paymentsReady: false });
  await open(page);
  assert.match(await page.textContent('#composerStatus'), /chưa có tổ chức/);
  assert.equal(await page.locator('#composerGroupSetup').isVisible(), true);
  assert.equal(await page.locator('#composerPaymentSetup').isVisible(), true);
  assert.equal(await page.locator('#composerPublish').isDisabled(), true);
});

test('read failure supports retry without losing input or optimistic readiness', async t => {
  const { page } = await fixture(t, { setupFailure: true });
  await open(page); await values(page);
  assert.match(await page.textContent('#composerStatus'), /Không tải được/);
  assert.equal(await page.locator('#composerPublish').isDisabled(), true);
  await page.click('#composerReload'); await page.waitForSelector('.composer-status.is-ready');
  assert.equal(await page.inputValue('#composerName'), 'Gói 30 ngày');
});

test('returning from setup refreshes readiness and preserves the form', async t => {
  const { page, calls } = await fixture(t);
  await open(page); await values(page);
  const before = calls.filter(call => call.path.endsWith('/setup')).length;
  await page.evaluate(() => window.videoMakerAdminShell.navigate('overview'));
  await page.evaluate(() => window.videoMakerAdminShell.navigate('plans'));
  await page.waitForFunction(() => !document.getElementById('composerReload').disabled);
  assert.equal(calls.filter(call => call.path.endsWith('/setup')).length, before + 1);
  assert.equal(await page.inputValue('#composerName'), 'Gói 30 ngày');
  assert.equal((await page.inputValue('#composerPrice')).replace(/\./g, ''), '70000');
  await page.locator('#planComposerTitle').click();
  assert.equal(await page.inputValue('#composerPrice'), '70.000');
  assert.match(await page.textContent('#composerPreviewPrice'), /70\.000/);
});

test('copy fills existing fields and identifies the source without changing it', async t => {
  const { page, calls } = await fixture(t);
  await page.locator('[data-copy-plan]').first().click();
  await page.waitForSelector('.composer-status.is-ready');
  assert.equal(await page.inputValue('#composerDevices'), '3');
  assert.equal(await page.inputValue('#composerName'), 'Gói nguồn (bản sao)');
  await page.click('#composerPublish'); await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls)[0].body.sourcePlanId, sourceId);
  assert.equal(posts(calls)[0].body.maxConcurrentSessions, 2);
  assert.deepEqual(posts(calls)[0].body.marketingFeatures, ['Quyền lợi riêng']);
});

test('double submission sends one atomic request and reports a completed sale', async t => {
  const { page, calls } = await fixture(t, { saveDelay: 150 });
  await open(page); await values(page);
  await page.evaluate(() => {
    const form = document.getElementById('composerForm'), submitter = document.getElementById('composerPublish');
    form.requestSubmit(submitter); form.requestSubmit(submitter);
  });
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(posts(calls).length, 1);
  assert.match(await page.textContent('#toastStack'), /tạo gói và mở bán/);
});

test('server rejection preserves fields and allows correction without a new plan', async t => {
  const { page, calls, saved } = await fixture(t, { rejected: true });
  await open(page); await values(page); await page.click('#composerPublish');
  await page.waitForFunction(() => document.getElementById('composerMessage').textContent.includes('hết chỗ'));
  assert.equal(await page.inputValue('#composerName'), 'Gói 30 ngày');
  assert.equal(await page.locator('#composerName').isEnabled(), true);
  assert.equal(saved.size, 0);
  assert.equal(posts(calls).length, 1);
});

test('lost response survives reload and retries exactly the same operation', async t => {
  const { page, calls, saved } = await fixture(t, { loseFirstResponse: true });
  await open(page); await values(page); await page.click('#composerPublish');
  await page.waitForFunction(() => document.getElementById('composerPublish').textContent.includes('Thử lại'));
  const first = posts(calls)[0].body;
  assert.equal(await page.locator('#composerName').isDisabled(), true);
  await page.reload(); await mount(page);
  await page.waitForSelector('#composerPublish:not(:disabled)'); await page.click('#composerPublish');
  await page.waitForSelector('#licensePlanList:not(.hidden)');
  assert.equal(saved.size, 1);
  assert.deepEqual(posts(calls)[1].body, first);
});

test('late setup response after logout cannot restore form or enable publishing', async t => {
  const { page, calls } = await fixture(t, { setupDelay: 300 });
  await page.click('#addPlanButton'); await page.click('#logoutButton');
  await page.locator('#adminShell').waitFor({ state: 'hidden' });
  await page.waitForTimeout(400);
  assert.equal(await page.locator('#licensePlanComposer').isHidden(), true);
  assert.equal(await page.locator('#composerPublish').count(), 0);
  assert.equal(posts(calls).length, 0);
});

test('write timeout releases busy state and preserves the request for explicit retry', async t => {
  const { page, calls } = await fixture(t, { saveDelay: 250 });
  await open(page); await values(page);
  await page.evaluate(() => {
    const original = window.setTimeout.bind(window);
    window.setTimeout = (callback, milliseconds, ...args) => original(callback, milliseconds === 30000 ? 40 : milliseconds, ...args);
  });
  await page.click('#composerPublish');
  await page.waitForFunction(() => document.getElementById('composerPublish').textContent.includes('Thử lại'));
  assert.equal(await page.locator('#composerPublish').isEnabled(), true);
  assert.equal(await page.locator('#composerName').isDisabled(), true);
  const request = posts(calls)[0].body;
  const stored = await page.evaluate(() => JSON.parse(sessionStorage.getItem('vmAdminPlanPending:admin-test')));
  assert.deepEqual(stored, request);
  await page.waitForTimeout(300);
  assert.equal(posts(calls).length, 1);
});
