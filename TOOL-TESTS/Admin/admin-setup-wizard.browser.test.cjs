// Real Razor, styles and scripts. Only synthetic local APIs; never contact providers or a running server.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { withAdminFonts } = require('./admin-browser-assets.cjs');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const repo = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(repo, file), 'utf8');
const razor = read('TOOL-SERVER/Pages/Admin/Index.cshtml');
const html = razor.slice(razor.indexOf('<!doctype')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
const models = [
  { modelId: 'text', providerCode: 'openai', name: 'GPT-5.6 Luna', modelCode: 'gpt-5.6-luna', modality: 'Text' },
  { modelId: 'image', providerCode: 'openai', name: 'GPT-Image-2', modelCode: 'gpt-image-2', modality: 'Image' },
  { modelId: 'video', providerCode: 'fal', name: 'Veo 3.1 Fast', modelCode: 'fal-ai/veo3.1/fast/image-to-video', modality: 'Video' }
];
const seed = { draftId: 'draft-one', organizationId: 'org-one', poolId: null, state: 'ConnectionsSaved', revision: 4, step: 2,
  form: { organizationName: 'VideoMaker Production', videoModelId: 'video', organizationBudget: 500, memberBudget: 20, capacity: 50,
    planName: 'Gói chuyên nghiệp', priceVnd: 199000, durationDays: 30, maxDevices: 2, description: '', marketingFeatures: [] } };
let browser;
before(async () => { browser = await chromium.launch({ headless: true }); });
after(async () => { await browser?.close(); });
async function fixture(t, { width = 1536, step = 2, paymentsReady = true, losePublish = false, loseConnection = false, unsafe = false } = {}) {
  const context = await browser.newContext({ viewport: { width, height: 1024 }, locale: 'vi-VN' });
  const page = await context.newPage(), errors = [], calls = [], outbound = [];
  page.on('pageerror', error => errors.push(error.message));
  t.after(async () => { await context.close(); assert.deepEqual(errors, []); assert.deepEqual(outbound, []); });
  let d = structuredClone(seed); d.step = step;
  if (unsafe) d.form.organizationName = '<img src=x onerror=alert(1)>';
  let payment = paymentsReady, lost = false;
  const operations = new Map();
  let priceRows = models.flatMap(m => (m.modality === 'Video' ? ['VideoSecond'] : ['InputToken', 'OutputToken']).map(usageType => ({ modelId: m.modelId, usageType, price: .1,
    unit: m.modality === 'Video' ? 'Second' : 'MillionTokens', fingerprint: `safe-${m.modelId}-${usageType}` })));
  const ready = () => ({ ready: payment, aiReady: true, allocationReady: true, paymentsReady: payment, remainingBudget: 500, availableSeats: 50, fingerprint: 'current-readiness',
    models, rates: priceRows, connections: ['openai', 'fal'].map(providerCode => ({ providerCode, active: true, version: 1, hint: '…safe' })),
    checks: [{ code: 'ai', label: 'AI và đơn giá', ready: true, step: 1 }, { code: 'allocation', label: 'Nhóm nhận khách', ready: true, step: 2 },
      { code: 'payments', label: 'Thanh toán SePay', ready: payment, message: payment ? null : 'Cấu hình SePay ở mục riêng.', step: 3 }] });
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url());
    if (url.origin !== 'https://admin.example.test') { outbound.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    const method = request.method(); let body = request.postDataJSON() || null;
    calls.push({ path: url.pathname, method, body }); let response = {};
    if (url.pathname.endsWith('/overview')) response = { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 };
    else if (url.pathname === '/api/admin/licenses/plans') response = [];
    else if (url.pathname.endsWith('/page')) response = { items: [], totalPages: 0, totalCount: 0, page: 1, pageSize: 20 };
    else if (url.pathname === '/api/admin/setup/bootstrap') response = { organizations: [{ organizationId: 'org-one', name: 'VideoMaker Production', role: 'Owner' }], models, paymentsReady: payment, drafts: [d] };
    else if (url.pathname.endsWith('/readiness')) response = ready();
    else if (url.pathname.includes('/operations/')) {
      response = operations.get(url.pathname.split('/').pop());
      if (!response) return route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ code: 'operation_not_found', message: 'Chưa ghi nhận thao tác. Thử lại cùng mã.' }) });
    }
    else if (url.pathname === '/api/admin/setup/drafts/draft-one') {
      if (method === 'PUT') {
        if (body.revision !== d.revision) return route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ code: 'draft_changed', message: 'Bản nháp đã thay đổi ở tab khác.' }) });
        d.form = body.form; d.step = body.step; d.revision++;
      }
      response = d;
    } else if (/\/(pricing|configuration|publish|connections)$/.test(url.pathname)) {
      const kind = url.pathname.split('/').pop();
      if (kind === 'connections' && loseConnection && !lost) { lost = true; return route.abort('failed'); }
      if (kind === 'pricing') priceRows = body.rates;
      if (kind === 'configuration') { d.poolId = 'pool-one'; d.step = 3; d.state = 'ConfigurationSaved'; }
      if (kind === 'publish') { d.state = body.publish ? 'Published' : 'PlanDraftSaved'; d.planId = 'plan-one'; }
      d.revision++;
      const op = { operationId: body.operationId, command: kind, state: 'Succeeded', resultId: kind === 'publish' ? 'plan-one' : null };
      operations.set(body.operationId, op);
      response = { draft: d, operation: op, ...(kind === 'publish' ? { plan: { licensePlanId: 'plan-one', name: d.form.planName, isPublic: body.publish, isActive: body.publish } } : {}) };
      if (kind === 'publish' && losePublish && !lost) { lost = true; return route.abort('failed'); }
    } else if (url.pathname === '/api/auth/logout') response = {};
    else if (url.pathname === '/api/organizations' || url.pathname === '/api/admin/organization-pools' || url.pathname === '/api/admin/ai-pricing') response = [];
    else throw new Error('Unexpected fixture API: ' + url.pathname);
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(response) });
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-test-session');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ userId: 'fixture-admin', displayName: 'Quản trị viên', email: 'admin@example.test', roles: ['Admin'] }));
    sessionStorage.setItem('vmAdminSetup:fixture-admin', JSON.stringify({ draftId: 'draft-one', pending: null }));
  });
  await page.goto('https://admin.example.test/');
  for (const file of ['admin.css', 'admin-modules.css', 'admin-allocation-setup.css', 'admin-plan-composer.css', 'admin-setup-wizard.css'])
    await page.addStyleTag({ content: withAdminFonts(read('TOOL-SERVER/wwwroot/admin/' + file)) });
  for (const file of ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-plan-composer.js', 'admin-allocation-setup.js', 'admin-organizations.js', 'admin-setup-wizard.js'])
    await page.addScriptTag({ content: read('TOOL-SERVER/wwwroot/admin/' + file) });
  await page.waitForSelector('#overviewMetrics .home-stat');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('organizations', { organizationScope: 'setup' }));
  await page.waitForSelector('#adminSetupWizardForm fieldset:not([disabled])');
  return { page, calls, change: fn => { fn(d); }, setPayments: value => { payment = value; } };
}
async function shot(page, name) {
  if (!process.env.VIDEOMAKER_SCREENSHOT_DIR) return;
  fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, name + '.png'), fullPage: true });
  const bounds = await page.evaluate(() => Object.fromEntries(['.sidebar', '.setup-steps', '.setup-columns', '.setup-form-column .setup-card', '.setup-summary', '.setup-footer'].map(selector => {
    const r = document.querySelector(selector)?.getBoundingClientRect(); return [selector, r ? { x: r.x, y: r.y, width: r.width, height: r.height } : null];
  })));
  fs.writeFileSync(path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, name + '.json'), JSON.stringify({ viewport: page.viewportSize(), bounds }, null, 2));
}

test('approved step-two form has three steps, two sidebar items and no writes on opening', async t => {
  const { page, calls } = await fixture(t);
  assert.equal(await page.locator('#organizationSubmenu button').count(), 2);
  assert.equal(await page.locator('.setup-steps li').count(), 3);
  assert.match(await page.locator('.setup-steps [aria-current="step"]').textContent(), /Chi phí & khách hàng/);
  assert.equal(await page.locator('[data-rate]').count(), 5);
  assert.equal(await page.locator('.setup-columns').evaluate(node => getComputedStyle(node).gridTemplateColumns.split(' ').length), 2);
  assert.equal(await page.locator('.setup-wizard input[type="password"]').count(), 0);
  assert.equal(calls.filter(c => c.method !== 'GET').length, 0);
  const footer = await page.locator('#adminSetupWizardForm button[type="submit"]').boundingBox();
  assert.ok(footer.y + footer.height <= 1024, 'step-two submit control fits the reference desktop viewport');
  await shot(page, 'setup-step2-1536');
});

test('changing global price requires confirmation then saves pricing/configuration before step three', async t => {
  const { page, calls } = await fixture(t);
  await page.locator('[data-rate="0"]').fill('2.5');
  await page.locator('#adminSetupWizardForm button[type="submit"]').click();
  await page.waitForSelector('#adminActionDialog[open]');
  assert.match(await page.locator('#adminActionMessage').textContent(), /toàn hệ thống/);
  assert.equal(calls.filter(c => c.method !== 'GET').length, 0);
  await page.locator('#adminActionConfirm').click();
  await page.waitForSelector('[data-check]');
  assert.deepEqual(calls.filter(c => c.method === 'POST').map(c => c.path.split('/').pop()), ['pricing', 'configuration']);
  assert.equal(calls.find(c => c.path.endsWith('/pricing')).body.rates[0].price, 2.5);
  assert.equal(await page.locator('.setup-steps li').count(), 3);
  await shot(page, 'setup-step3-1536');
});

test('SePay remains separate and safe draft resumes with fresh payment status', async t => {
  const f = await fixture(t, { step: 3, paymentsReady: false }); const { page } = f;
  assert.equal(await page.locator('#adminSetupWizardForm button[type="submit"]').isDisabled(), true);
  await page.locator('[data-sepay]').click();
    await page.locator('[data-panel="payments"]').waitFor({ state: 'visible' });
  assert.equal(await page.locator('[data-panel="payments"]').isVisible(), true);
  assert.equal(await page.locator('.setup-wizard [name="bankCode"]').count(), 0);
  f.setPayments(true);
  await page.goBack();
  await page.waitForSelector('#adminSetupWizardForm fieldset:not([disabled])');
  assert.equal(await page.locator('[data-form="planName"]').inputValue(), 'Gói chuyên nghiệp');
  assert.equal(await page.locator('#adminSetupWizardForm button[type="submit"]').isDisabled(), false);
  await shot(page, 'setup-sepay-return-1536');
});

test('lost publication response reconciles receipt without a second create or seat reservation', async t => {
  const { page, calls } = await fixture(t, { step: 3, losePublish: true });
  await page.locator('#adminSetupWizardForm button[type="submit"]').click();
  await page.waitForSelector('#adminActionDialog[open]'); await page.locator('#adminActionConfirm').click();
  await page.waitForSelector('[data-reconcile]');
  await page.locator('[data-reconcile]').click(); await page.waitForSelector('.setup-success');
  assert.equal(calls.filter(c => c.path.endsWith('/publish') && c.method === 'POST').length, 1);
  assert.match(await page.locator('.setup-success').textContent(), /Đã tạo gói & mở bán/);
  assert.equal(calls.some(c => /reserve|seat/.test(c.path) && c.method !== 'GET'), false);
});

test('stale revision is shown and credential never enters session storage', async t => {
  const f = await fixture(t, { step: 1 });
  await shot(f.page, 'setup-step1-1536');
  await f.page.locator('#setupKey-openai').fill('synthetic-transient-secret');
  f.change(d => d.revision++);
  await f.page.locator('[data-connect="openai"]').click();
  await f.page.waitForSelector('#setupWizardMessage:not(:empty)');
  assert.match(await f.page.locator('#setupWizardMessage').textContent(), /tab khác/);
  assert.equal(f.calls.some(c => c.path.endsWith('/connections') && c.method === 'POST'), false);
  assert.doesNotMatch(await f.page.evaluate(() => JSON.stringify(sessionStorage)), /synthetic-transient-secret/);
  assert.equal(await f.page.locator('#setupKey-openai').inputValue(), '');
});

test('cancelling global price confirmation preserves edited prices and makes no write', async t => {
  const { page, calls } = await fixture(t);
  await page.locator('[data-rate="0"]').fill('2.5');
  await page.locator('#adminSetupWizardForm button[type="submit"]').click();
  await page.waitForSelector('#adminActionDialog[open]'); await page.locator('#adminActionCancel').click();
  await page.waitForSelector('#adminSetupWizardForm fieldset:not([disabled])');
  assert.equal(await page.locator('[data-rate="0"]').inputValue(), '2.5');
  assert.equal(calls.filter(c => c.method !== 'GET').length, 0);
});

test('connection timeout keeps only safe metadata and retries with a reentered key and the same operation', async t => {
  const { page, calls } = await fixture(t, { step: 1, loseConnection: true });
  const key = 'synthetic-transient-secret';
  await page.locator('#setupKey-openai').fill(key); await page.locator('[data-connect="openai"]').click();
  await page.waitForSelector('#setupRetryKey');
  assert.doesNotMatch(await page.evaluate(() => JSON.stringify(sessionStorage)), new RegExp(key));
  await page.locator('#setupRetryKey').fill(key); await page.locator('[data-retry]').click();
  await page.waitForSelector('#adminSetupWizardForm fieldset:not([disabled])');
  const writes = calls.filter(c => c.path.endsWith('/connections') && c.method === 'POST');
  assert.equal(writes.length, 2); assert.deepEqual(writes[0].body, writes[1].body);
  assert.equal(await page.locator('#setupKey-openai').inputValue(), '');
});

test('safe text, keyboard labels and responsive form fit supported widths and zoom levels', async t => {
  const { page } = await fixture(t, { unsafe: true });
  assert.equal(await page.locator('.setup-wizard img').count(), 0);
  await page.locator('[data-rate="0"]').focus();
  await page.keyboard.press('Tab');
  assert.equal(await page.evaluate(() => document.activeElement.dataset.rate), '1');
  await page.emulateMedia({ reducedMotion: 'reduce', forcedColors: 'active' });
  assert.notEqual(await page.locator('[data-rate="1"]').evaluate(node => getComputedStyle(node).outlineStyle), 'none');
  await page.emulateMedia({ forcedColors: 'none' });
  for (const width of [320, 390, 768, 1024, 1366, 1440, 1536, 1920]) {
    await page.setViewportSize({ width, height: 1024 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `no overflow at ${width}`);
    assert.equal(await page.locator('#adminSetupWizardForm button[type="submit"]').isVisible(), true);
    await shot(page, `setup-safe-text-step2-${width}`);
  }
  for (const zoom of [1.25, 1.5, 2]) {
    await page.setViewportSize({ width: 1536, height: 1024 });
    await page.evaluate(zoom => { document.body.style.zoom = zoom; }, zoom);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, `no overflow at zoom ${zoom}`);
    await shot(page, `setup-step2-zoom-${zoom}`);
  }
});
