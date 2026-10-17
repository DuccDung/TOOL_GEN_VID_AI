// Actual Admin assets, isolated API fixtures, no live database or provider requests.
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
let browser;
before(async () => { browser = await chromium.launch({ headless: true }); });
after(async () => { await browser?.close(); });
const pageResult = items => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0 });
const rate = (id, type, video = false) => ({ costRateId: `${id}-${type}`, usageType: type, unit: video ? 'Second' : 'MillionTokens',
  unitPrice: .1, currencyCode: 'USD', isActive: true, effectiveFromUtc: '2020-01-01T00:00:00Z', effectiveToUtc: null,
  metadataJson: video ? JSON.stringify({ resolution: '720p', nativeAudio: true, endpointId: 'fal-ai/veo3.1/fast/image-to-video' }) : null });

async function fixture(t, options = {}) {
  const context = await browser.newContext({ viewport: { width: options.width || 1440, height: 1024 }, locale: 'vi-VN' });
  const page = await context.newPage(), calls = [], errors = [], outbound = [];
  page.setDefaultTimeout(10000);
  page.on('pageerror', error => errors.push(error.message));
  const catalog = [
    { providerId: 'openai-id', providerCode: 'openai', displayName: 'OpenAI', isEnabled: true, models: [
      { providerModelId: 'text-unused', modelCode: 'text-unused', displayName: 'Model khác', modality: 'Text', isEnabled: true, isDefault: false, costRates: [rate('unused', 'InputToken'), rate('unused', 'OutputToken')] },
      { providerModelId: 'text-current', modelCode: 'text-current', displayName: 'Model đang dùng', modality: 'Text', isEnabled: true, isDefault: true, costRates: [rate('current', 'InputToken')] },
      { providerModelId: 'image', modelCode: 'image', displayName: 'Model tạo ảnh', modality: 'Image', isEnabled: true, isDefault: true, costRates: [rate('image', 'InputToken'), rate('image', 'OutputToken')] }
    ] },
    { providerId: 'fal-id', providerCode: 'fal', displayName: 'Fal', isEnabled: true, models: [
      { providerModelId: 'veo', modelCode: 'fal-ai/veo3.1/fast/image-to-video', displayName: 'Veo 3.1 Fast', modality: 'Video', isEnabled: true, isDefault: true, costRates: [rate('veo', 'VideoSecond', true)] }
    ] }
  ];
  const orgs = ['a', 'b', 'outside-page', 'viewer', 'billing'].map(id => ({ organizationId: id, name: `Studio ${id}`, code: id,
    role: id === 'viewer' ? 'Viewer' : id === 'billing' ? 'BillingManager' : 'Owner', status: 'Active', monthlyBudgetLimit: 100,
    actualCost: 0, reservedCost: 0, remainingBudget: 100, currencyCode: 'USD', memberCount: 1, activeMemberCount: 1,
    keys: { openai: id !== 'a', fal: id !== 'a' }, policy: id !== 'a' }));
  const pool = { organizationPoolId: 'pool', name: 'Nhóm thử', code: 'pool', status: 'Active', licensePlanCount: 1,
    activeLicensePlanCount: 1, organizationCount: 2, allocatableOrganizationCount: 0, allocatableAvailableSeats: 0 };
  const state = { catalog, orgs, failProviders: false, delaySummary: false, draftRevision: 1, wizardChecks: options.wizardChecks || [] };
  let releaseSummary, enteredSummary;
  const delayed = new Promise(resolve => { enteredSummary = resolve; });
  const policy = org => org.policy ? { organizationId: org.organizationId, providerId: 'fal-id', providerCode: 'fal', providerName: 'Fal',
    providerModelId: 'veo', modelCode: catalog[1].models[0].modelCode, modelName: 'Veo 3.1 Fast', policyVersion: 1, scope: 'LongForm', resolution: '720p', nativeAudio: true, isActive: true } : null;
  const directory = () => orgs.map(org => ({ ...org, aiReadiness: [
    { providerCode: 'openai', modelCode: 'text-current', ready: org.keys.openai && catalog[0].models[1].costRates.some(r => r.usageType === 'OutputToken'),
      missingUsageTypes: catalog[0].models[1].costRates.some(r => r.usageType === 'OutputToken') ? [] : ['OutputToken'],
      blockingReasons: [...(org.keys.openai ? [] : ['credential_missing']), ...(catalog[0].models[1].costRates.some(r => r.usageType === 'OutputToken') ? [] : ['pricing_not_configured'])] },
    { providerCode: org.policy ? 'fal' : 'video-long-form', modelCode: org.policy ? catalog[1].models[0].modelCode : null,
      ready: org.policy && org.keys.fal, missingUsageTypes: [], blockingReasons: org.policy ? org.keys.fal ? [] : ['credential_missing'] : ['video_policy_missing'] }
  ] }));
  const detail = () => ({ pool, organizations: orgs.slice(0, 2).map(org => ({ organizationId: org.organizationId, organizationPoolId: 'pool',
    organizationName: org.name, organizationCode: org.code, organizationStatus: 'Active', seatCapacity: options.full ? 1 : 10, activeSeats: options.full ? 1 : 0,
    reservedSeats: 0, priority: 100, isReady: false, isAutoAssignmentEnabled: true, readinessMessage: null })),
    licensePlans: [{ licensePlanId: 'plan', planName: 'Gói thử', planCode: 'plan', organizationPoolId: 'pool', organizationPoolName: 'Nhóm thử', isActive: true, isSellable: true, defaultMemberMonthlyBudgetLimit: null }], recentAssignments: [] });
  const models = catalog.flatMap(provider => provider.models.filter(model => model.isDefault).map(model => ({ modelId: model.providerModelId,
    providerCode: provider.providerCode, modelCode: model.modelCode, name: model.displayName, modality: model.modality })));
  const draft = () => ({ draftId: 'draft', organizationId: 'a', poolId: 'pool', step: 3, revision: state.draftRevision, state: 'ConfigurationSaved',
    form: { organizationName: 'Studio a', videoModelId: 'veo', organizationBudget: 100, memberBudget: null, capacity: 10,
      planName: 'Gói chưa lưu', priceVnd: 70000, durationDays: 30, maxDevices: 1, description: '', marketingFeatures: [] } });
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url()), method = request.method();
    if (url.origin !== 'https://admin.example.test') { outbound.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    const bodyIn = request.postData() ? request.postDataJSON() : null;
    calls.push({ path: url.pathname, search: url.search, method, body: bodyIn });
    if (url.pathname === '/api/organizations' && state.delaySummary) {
      state.delaySummary = false; enteredSummary(); await new Promise(resolve => { releaseSummary = resolve; });
    }
    let body;
    if (url.pathname.endsWith('/overview')) body = { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 };
    else if (url.pathname === '/api/organizations') body = directory();
    else if (url.pathname === '/api/organizations/page') body = pageResult(directory().slice(0, 2));
    else if (url.pathname === '/api/admin/licenses/plans') body = [{ licensePlanId: 'plan', name: 'Gói thử', planCode: 'plan', isActive: true, isPublic: true, salePriceVnd: 70000, defaultDurationDays: 30 }];
    else if (url.pathname === '/api/admin/ai-pricing') body = catalog;
    else if (url.pathname === '/api/admin/organization-pools') body = [pool];
    else if (url.pathname === '/api/admin/organization-pools/page') body = pageResult([pool]);
    else if (url.pathname === '/api/admin/organization-pools/pool') body = detail();
    else if (url.pathname === '/api/admin/payments/sepay') body = { enabled: true, selectedAccountId: 'account', accounts: [{ accountId: 'account', label: 'Tài khoản thử', bankName: 'Ngân hàng thử', bankAccountNumber: '000000', accountHolder: 'TEST' }] };
    else if (url.pathname === '/api/admin/setup/bootstrap') body = { organizations: directory(), drafts: [draft()], models, paymentsReady: false };
    else if (url.pathname === '/api/admin/setup/drafts/draft') body = draft();
    else if (url.pathname === '/api/admin/setup/drafts/draft/readiness') body = { ready: false, aiReady: false, allocationReady: true, paymentsReady: false,
      remainingBudget: 100, availableSeats: 10, fingerprint: 'fixture', checks: state.wizardChecks, models,
      rates: models.flatMap(model => (model.modality === 'Video' ? ['VideoSecond'] : ['InputToken', 'OutputToken']).map(usageType => ({ modelId: model.modelId, usageType, price: .1, unit: model.modality === 'Video' ? 'Second' : 'MillionTokens', fingerprint: 'fixture' }))),
      connections: ['openai', 'fal'].map(providerCode => ({ providerCode, active: false, version: 0, hint: null })) };
    else if (url.pathname === '/api/auth/logout') body = {};
    else if (url.pathname.startsWith('/api/organizations/')) {
      const org = orgs.find(item => item.organizationId === url.pathname.split('/')[3]);
      if (!org) return route.fulfill({ status: 404, body: '{}' });
      if (url.pathname.endsWith('/credential') && method === 'PUT') { org.keys[url.pathname.split('/')[5]] = true; body = {}; }
      else if (url.pathname.endsWith('/providers')) {
        if (state.failProviders) return route.fulfill({ status: 503, contentType: 'application/json', body: '{"message":"Fixture unavailable"}' });
        body = catalog.map(provider => ({ providerId: provider.providerId, providerCode: provider.providerCode, configured: org.keys[provider.providerCode],
          credentialStatus: org.keys[provider.providerCode] ? 'Active' : null, secretHint: org.keys[provider.providerCode] ? '…safe' : null, credentialVersion: 1 }));
      } else if (url.pathname.endsWith('/video-policy')) {
        if (method === 'PUT') org.policy = true;
        body = url.searchParams.get('scope') === 'LongForm' || method === 'PUT' ? policy(org) : null;
      } else if (url.pathname.endsWith('/budget') && method === 'PUT') { org.monthlyBudgetLimit = org.remainingBudget = bodyIn.monthlyBudgetLimit; body = directory().find(item => item.organizationId === org.organizationId); }
      else if (url.pathname.endsWith('/usage/page')) body = { items: [], groups: [], budgetLimit: org.monthlyBudgetLimit, actualCost: org.actualCost,
        reservedCost: org.reservedCost, remainingBudget: org.remainingBudget, currencyCode: 'USD', itemsPage: 1, itemsPageSize: 50, itemsTotalCount: 0, itemsTotalPages: 0 };
      else if (url.pathname.endsWith('/members')) body = [];
      else if (url.pathname.endsWith('/members/page')) body = pageResult([]);
      else { outbound.push(url.pathname); return route.fulfill({ status: 404, body: '{}' }); }
    } else if (url.pathname.startsWith('/api/admin/ai-pricing/models/') && method === 'POST') {
      const model = catalog.flatMap(provider => provider.models).find(item => item.providerModelId === url.pathname.split('/')[5]);
      model.costRates.push(rate('new', bodyIn.usageType)); body = model;
    } else if (url.pathname.endsWith('/page')) body = pageResult([]);
    else { outbound.push(url.pathname); return route.fulfill({ status: 404, body: '{}' }); }
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) }).catch(error => { if (!request.failure()) throw error; });
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-session');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ userId: 'fixture-admin', email: 'admin@example.test', displayName: 'Admin', roles: ['Admin'] }));
    sessionStorage.setItem('vmAdminSetup:fixture-admin', JSON.stringify({ draftId: 'draft', pending: null }));
  });
  await page.goto('https://admin.example.test/');
  for (const name of ['admin.css', 'admin-modules.css', 'admin-plan-composer.css', 'admin-allocation-setup.css', 'admin-setup-wizard.css'])
    await page.addStyleTag({ content: withAdminFonts(read('TOOL-SERVER/wwwroot/admin/' + name)) });
  for (const name of ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-plan-composer.js', 'admin-allocation-setup.js', 'admin-organizations.js', 'admin-setup-wizard.js', 'admin-sepay.js'])
    await page.addScriptTag({ content: read('TOOL-SERVER/wwwroot/admin/' + name) });
  await page.waitForSelector('#overviewMetrics .home-stat');
  t.after(async () => {
    releaseSummary?.();
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
      const name = t.name.replace(/[^a-z0-9]+/gi, '-').slice(0, 80);
      fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
      const diagnostic = await page.evaluate(() => ({ dialogs: [...document.querySelectorAll('dialog[open]')].map(node => node.id),
        messages: [...document.querySelectorAll('.toast, .form-message')].map(node => node.textContent),
        active: document.activeElement.id, panels: [...document.querySelectorAll('[data-panel]:not(.hidden)')].map(node => node.dataset.panel) }));
      fs.writeFileSync(path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, name + '.json'), JSON.stringify({ ...diagnostic, errors, calls: calls.map(call => ({ path: call.path, method: call.method })) }, null, 2));
    }
    await context.close(); assert.deepEqual(errors, []); assert.deepEqual(outbound, []);
  });
  const navigate = destination => page.evaluate(destination => window.videoMakerOrganizationAdmin.openSetupTarget(destination), destination);
  const writes = () => calls.filter(call => call.method !== 'GET');
  return { page, state, calls, navigate, writes, delayed, release: () => releaseSummary?.() };
}

async function shot(page, name) {
  if (!process.env.VIDEOMAKER_SCREENSHOT_DIR) return;
  fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
  await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, name + '.png'), fullPage: true });
}

test('provider cards link to exact credential and optional policy without writes or duplicate credential actions', async t => {
  const { page, navigate, writes } = await fixture(t);
  await navigate({ target: 'organization', organizationId: 'a', tab: 'providers' });
  const openai = page.locator('[data-provider-code="openai"]'), fal = page.locator('[data-provider-code="fal"]');
  assert.equal(await openai.locator('[data-configure-provider="openai"]').count(), 1);
  assert.equal(await openai.locator('[data-readiness-action="credential"]').count(), 0);
  assert.match(await fal.locator('.status-pill').textContent(), /Không được chọn/);
  await shot(page, 'cards-1440');
  await fal.locator('[data-readiness-action="policy"]').click();
  await page.waitForFunction(() => document.querySelector('[data-policy-scope="LongForm"] select')?.value === 'veo');
  assert.equal(await page.inputValue('[data-policy-scope="LongForm"] select'), 'veo');
  assert.equal(await page.evaluate(() => document.activeElement.matches('[data-policy-scope="LongForm"] select')), true);
  assert.deepEqual(writes(), []);
  await openai.locator('[data-configure-provider="openai"]').click();
  await page.waitForSelector('#organizationCredentialDialog[open]');
  assert.equal(await page.inputValue('#organizationCredentialProvider'), 'openai');
  assert.equal(await page.getAttribute('#organizationCredentialForm', 'data-organization-id'), 'a');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'organizationCredentialKey');
  assert.deepEqual(writes(), []);
});

test('missing rate opens current model rather than the first model and refreshes server readiness after save', async t => {
  const { page, navigate, writes, state } = await fixture(t);
  await navigate({ target: 'organization', organizationId: 'b', tab: 'providers' });
  await page.locator('[data-provider-code="openai"] [data-readiness-action="pricing"]').click();
  await page.waitForSelector('#aiRateDialog[open]');
  assert.equal(await page.inputValue('#aiRateModelId'), 'text-current');
  assert.equal(await page.inputValue('#aiRateUsageType'), 'OutputToken');
  assert.equal(await page.locator('[data-pricing-model="text-current"]').evaluate(node => node.classList.contains('setup-target-highlight')), true);
  assert.deepEqual(writes(), []);
  await page.fill('#aiRatePrice', '.2'); await page.fill('#aiRateSource', 'Fixture pricing');
  await page.locator('#aiRateForm [type=submit]').click();
  await page.locator('#aiRateDialog').waitFor({ state: 'hidden' });
  assert.equal(writes().length, 1); assert.match(writes()[0].path, /models\/text-current\/rates$/);
  assert.equal(state.catalog[0].models[0].costRates.length, 2);
  await page.click('[data-setup-correction-return]');
  await page.waitForSelector('[data-provider-code="openai"] .status-healthy');
  assert.equal(await page.locator('[data-provider-code="openai"] [data-readiness-action="pricing"]').count(), 0);
});

test('organization outside the directory page binds credential to that organization and clears secret on close', async t => {
  const { page, navigate, writes } = await fixture(t, { width: 390 });
  assert.equal(await navigate({ target: 'credential', organizationId: 'outside-page', providerCode: 'fal' }), true);
  assert.equal(await page.getAttribute('#organizationCredentialForm', 'data-organization-id'), 'outside-page');
  await page.fill('#organizationCredentialKey', 'synthetic-provider-key');
  assert.equal(await page.evaluate(() => JSON.stringify(sessionStorage).includes('synthetic-provider-key')), false);
  await page.locator('#organizationCredentialForm [type=submit]').click();
  await page.locator('#organizationCredentialDialog').waitFor({ state: 'hidden' });
  assert.equal(writes().length, 1); assert.equal(writes()[0].path, '/api/organizations/outside-page/providers/fal/credential');
  assert.equal(await page.inputValue('#organizationCredentialKey'), '');
  await navigate({ target: 'credential', organizationId: 'b', providerCode: 'openai' });
  await shot(page, 'credential-390');
  await page.fill('#organizationCredentialKey', 'unsent-synthetic-key'); await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('#organizationCredentialKey').value);
  assert.equal(writes().length, 1);
});

test('budget links preserve organization and distinguish locked from exhausted budget', async t => {
  const { page, state, navigate, writes } = await fixture(t);
  state.orgs[0].monthlyBudgetLimit = 0;
  await navigate({ target: 'organization', organizationId: 'a', tab: 'providers' });
  await page.locator('[data-provider-code="openai"] [data-readiness-action="budget"]').click();
  await page.waitForSelector('#organizationBudgetLimit');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'organizationBudgetLimit');
  assert.equal(await page.getAttribute('#organizationBudgetForm', 'data-organization-id'), 'a');
  assert.deepEqual(writes(), []);
  await page.fill('#organizationBudgetLimit', '150'); await page.locator('#organizationBudgetForm [type=submit]').click();
  await page.waitForFunction(() => document.querySelector('#organizationBudgetLimit')?.value === '150');
  assert.equal(writes()[0].path, '/api/organizations/a/budget');
  state.orgs[1].remainingBudget = 0;
  await navigate({ target: 'organization', organizationId: 'b', tab: 'providers' });
  assert.match(await page.locator('[data-provider-code="openai"] .provider-blockers').textContent(), /Ngân sách khả dụng/);
  assert.match(await page.locator('[data-provider-code="openai"] [data-readiness-action="budget"]').textContent(), /Xem ngân sách/);
});

test('model and provider disabled routes are read-only; removed model and missing organization do not choose another', async t => {
  const { page, state, navigate, writes } = await fixture(t);
  state.catalog[0].models[1].isEnabled = false;
  await navigate({ target: 'organization', organizationId: 'b', tab: 'providers' });
  await page.locator('[data-provider-code="openai"] [data-readiness-action="model"]').click();
  await page.waitForSelector('[data-pricing-model="text-current"] [data-toggle-ai-model].setup-target-highlight');
  assert.equal(await page.evaluate(() => document.activeElement.dataset.toggleAiModel), 'text-current');
  state.catalog[0].isEnabled = false;
  await navigate({ target: 'provider', organizationId: 'b', providerCode: 'openai' });
  assert.equal(await page.evaluate(() => document.activeElement.dataset.toggleAiProvider), 'openai-id');
  state.catalog[0].models.splice(1, 1);
  assert.equal(await navigate({ target: 'pricing', organizationId: 'b', providerCode: 'openai', modelId: 'text-current' }), false);
  assert.equal(await page.locator('#aiRateDialog').isVisible(), false);
  assert.equal(await navigate({ target: 'credential', organizationId: 'missing', providerCode: 'openai' }), false);
  assert.equal(await page.locator('#organizationCredentialDialog').isVisible(), false);
  assert.deepEqual(writes(), []);
});

test('roles and late responses cannot open a credential for another organization or resurrect it after logout', async t => {
  const f = await fixture(t), { page, state, navigate, writes } = f;
  assert.equal(await navigate({ target: 'credential', organizationId: 'viewer', providerCode: 'openai' }), false);
  assert.equal(await navigate({ target: 'policy', organizationId: 'billing' }), false);
  assert.equal(await navigate({ target: 'budget', organizationId: 'billing' }), true);
  state.delaySummary = true;
  await page.evaluate(() => { window.videoMakerOrganizationAdmin.openSetupTarget({ target: 'credential', organizationId: 'a', providerCode: 'openai' }); });
  await f.delayed;
  await navigate({ target: 'credential', organizationId: 'b', providerCode: 'fal' });
  f.release();
  assert.equal(await page.getAttribute('#organizationCredentialForm', 'data-organization-id'), 'b');
  assert.equal(await page.inputValue('#organizationCredentialProvider'), 'fal');
  await page.evaluate(() => window.videoMakerAdminShell.showLogin());
  assert.equal(await page.locator('#organizationCredentialDialog').isVisible(), false);
  assert.equal(await page.locator('#setupCorrectionReturn').isVisible(), false);
  assert.deepEqual(writes(), []);
});

test('failed target GET and repeated click never test a key or create a stale dialog', async t => {
  const { page, state, navigate, writes, calls } = await fixture(t);
  state.failProviders = true;
  assert.equal(await navigate({ target: 'credential', organizationId: 'a', providerCode: 'openai' }), false);
  assert.equal(await page.locator('#organizationCredentialDialog').isVisible(), false);
  assert.deepEqual(writes(), []);
  state.failProviders = false;
  await page.evaluate(async () => {
    const context = { target: 'credential', organizationId: 'a', providerCode: 'openai' };
    await Promise.all([window.videoMakerOrganizationAdmin.openSetupTarget(context), window.videoMakerOrganizationAdmin.openSetupTarget(context)]);
  });
  assert.equal(await page.locator('#organizationCredentialDialog[open]').count(), 1);
  assert.equal(calls.filter(call => call.path === '/api/organizations' && call.method === 'GET').length, 2);
  assert.deepEqual(writes(), []);
  await page.keyboard.press('Escape');
  state.failProviders = true;
  assert.equal(await navigate({ target: 'credential', organizationId: 'b', providerCode: 'fal' }), false);
  assert.equal(await page.locator('#organizationCredentialDialog').isVisible(), false);
});

test('wizard correction preserves edited plan on return from separate SePay and local steps never save automatically', async t => {
  const { page, writes } = await fixture(t, { wizardChecks: [
    { code: 'credential_openai', label: 'Kết nối OpenAI', ready: false, step: 1 },
    { code: 'pricing_Text_OutputToken', label: 'Đơn giá', ready: false, step: 2 },
    { code: 'payments', label: 'Thanh toán SePay', ready: false, step: 3 }
  ] });
  await page.evaluate(() => window.videoMakerAdminShell.navigate('organizations', { organizationScope: 'setup' }));
  await page.waitForSelector('[data-form="planName"]');
  await page.fill('[data-form="planName"]', 'Gói chỉnh chưa lưu');
  await page.click('[data-setup-fix="payments"]');
  await page.waitForSelector('[data-panel="payments"]:not(.hidden)');
  await page.click('[data-setup-correction-return]');
  await page.waitForSelector('[data-form="planName"]');
  assert.equal(await page.inputValue('[data-form="planName"]'), 'Gói chỉnh chưa lưu');
  await page.click('[data-setup-fix="pricing_Text_OutputToken"]');
  assert.equal(await page.evaluate(() => document.activeElement.getAttribute('data-rate')), '1');
  assert.equal(await page.locator('.setup-steps [aria-current="step"]').textContent().then(text => /Chi phí/.test(text)), true);
  assert.deepEqual(writes(), []);
});

test('allocation correction returns to the selected organization and capacity dialog cannot target a different pool member', async t => {
  const { page, state, navigate, writes } = await fixture(t, { full: true });
  state.orgs[1].monthlyBudgetLimit = 0;
  await navigate({ target: 'allocation', poolId: 'pool', organizationId: 'b' });
  await page.waitForSelector('#allocationInspectOrganization');
  assert.equal(await page.inputValue('#allocationInspectOrganization'), 'b');
  await page.click('[data-allocation-action="fix-budget"]');
  await page.waitForSelector('#organizationBudgetLimit');
  assert.equal(await page.getAttribute('#organizationBudgetForm', 'data-organization-id'), 'b');
  await page.click('[data-allocation-return]');
  await page.waitForSelector('#allocationInspectOrganization');
  assert.equal(await page.inputValue('#allocationInspectOrganization'), 'b');
  await page.click('[data-allocation-action="fix-capacity"]');
  await page.waitForSelector('#organizationPoolOrganizationDialog[open]');
  assert.equal(await page.inputValue('#poolOrganizationId'), 'b');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'poolOrganizationCapacity');
  assert.deepEqual(writes(), []);
});

test('wizard return refuses a newer draft revision and retains unsaved fields for review', async t => {
  const { page, state, writes } = await fixture(t, { wizardChecks: [{ code: 'payments', label: 'Thanh toán', ready: false, step: 3 }] });
  await page.evaluate(() => window.videoMakerAdminShell.navigate('organizations', { organizationScope: 'setup' }));
  await page.waitForSelector('[data-form="planName"]');
  await page.fill('[data-form="planName"]', 'Nội dung cần giữ');
  await page.click('[data-setup-fix="payments"]');
  state.draftRevision = 2;
  await page.click('[data-setup-correction-return]');
  await page.waitForFunction(() => document.querySelector('#setupWizardMessage')?.textContent.includes('Bản nháp đã thay đổi'));
  assert.equal(await page.inputValue('[data-form="planName"]'), 'Nội dung cần giữ');
  assert.deepEqual(writes(), []);
});

test('rendering escapes organization and model names and catalog actions require Global Admin', async t => {
  const { page, state, navigate, writes } = await fixture(t);
  state.orgs[0].name = '<img src=x onerror=alert(1)>';
  state.catalog[0].models[1].displayName = '<img src=x onerror=alert(1)>';
  await navigate({ target: 'pricing', organizationId: 'a', providerCode: 'openai', modelId: 'text-current', usageTypes: ['OutputToken'] });
  assert.equal(await page.locator('#aiRateDialogTitle img').count(), 0);
  assert.equal(await page.locator('#aiPricingCatalog img').count(), 0);
  await page.keyboard.press('Escape');
  await page.evaluate(() => { window.videoMakerAdminShell.state.user.roles = []; });
  assert.equal(await navigate({ target: 'model', organizationId: 'a', providerCode: 'openai', modelId: 'text-current' }), false);
  assert.equal(await navigate({ target: 'payments' }), false);
  assert.deepEqual(writes(), []);
});

test('links and destination focus remain keyboard accessible and fit mobile and desktop', async t => {
  const { page, navigate, writes } = await fixture(t);
  for (const width of [390, 768, 1440, 1920]) {
    await page.setViewportSize({ width, height: 1024 });
    await navigate({ target: 'organization', organizationId: 'b', tab: 'providers' });
    const link = page.locator('[data-provider-code="openai"] [data-readiness-action="pricing"]');
    await link.focus(); await page.keyboard.press('Enter');
    await page.waitForSelector('#aiRateDialog[open]');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'aiRatePrice');
    const geometry = await page.evaluate(() => ({ overflow: document.documentElement.scrollWidth > innerWidth + 1,
      width: innerWidth, nodes: [...document.querySelectorAll('body *')].filter(node => node.getClientRects().length && node.getBoundingClientRect().right > innerWidth + 1).slice(0, 10).map(node => ({ tag: node.tagName, id: node.id, class: node.className, right: node.getBoundingClientRect().right })) }));
    if (geometry.overflow) await shot(page, 'overflow-' + width);
    assert.equal(geometry.overflow, false, JSON.stringify(geometry));
    await page.keyboard.press('Escape'); await page.locator('#aiRateDialog').waitFor({ state: 'hidden' });
  }
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await navigate({ target: 'budget', organizationId: 'b' });
  assert.notEqual(await page.locator('#organizationBudgetLimit').evaluate(node => getComputedStyle(node).outlineStyle), 'none');
  assert.deepEqual(writes(), []);
});

test('manual navigation clears the previous correction source and a new organization gets its own return point', async t => {
  const { page, navigate, writes } = await fixture(t);
  await navigate({ target: 'organization', organizationId: 'a', tab: 'providers' });
  await page.click('[data-provider-code="openai"] [data-readiness-action="pricing"]');
  await page.waitForSelector('#aiRateDialog[open]');
  await page.keyboard.press('Escape');
  assert.match(await page.locator('#setupCorrectionReturn span').textContent(), /Studio a/);
  await page.click('#organizationDirectoryScopeItem');
  assert.equal(await page.locator('#setupCorrectionReturn').isVisible(), false);
  await page.click('#backToOrganizations'); await page.click('[data-open-organization="b"]');
  await page.click('[data-organization-tab="providers"]');
  await page.click('[data-provider-code="openai"] [data-readiness-action="pricing"]');
  await page.waitForSelector('#aiRateDialog[open]');
  assert.match(await page.locator('#setupCorrectionReturn span').textContent(), /Studio b/);
  await page.keyboard.press('Escape'); await page.click('[data-setup-correction-return]');
  await page.waitForSelector('[data-provider-code="openai"]');
  assert.match(await page.locator('#breadcrumbCurrent').textContent(), /Studio b/);
  assert.deepEqual(writes(), []);
});
