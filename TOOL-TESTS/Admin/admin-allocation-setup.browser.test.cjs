// Actual Razor + CSS + scripts, synthetic APIs only; all other outbound requests are blocked.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { withAdminFonts } = require('./admin-browser-assets.cjs');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const razor = read('TOOL-SERVER/Pages/Admin/Index.cshtml');
const html = razor.slice(razor.indexOf('<!doctype')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
let browser;
before(async () => { browser = await chromium.launch({ headless: true, channel: 'msedge' }); });
after(async () => { await browser?.close(); });

async function fixture(t, options = {}) {
  const context = await browser.newContext({ viewport: { width: options.width || 1440, height: 1050 }, locale: 'vi-VN' });
  const page = await context.newPage();
  const errors = [], unexpected = [], calls = [];
  page.on('pageerror', error => errors.push(error.message));
  t.after(async () => { await context.close(); assert.deepEqual(errors, []); assert.deepEqual(unexpected, []); });
  const plans = [7, 30, 180].map((days, i) => ({ licensePlanId: `plan-${i + 1}`, planCode: `plan-${days}`, name: `Gói ${days} ngày`,
    isActive: true, isPublic: true, salePriceVnd: [50000, 70000, 120000][i], defaultDurationDays: days, maxActivatedDevices: 1, maxConcurrentSessions: 1, offlineGraceHours: 0 }));
  const state = {
    pool: { organizationPoolId: 'pool-1', name: options.unsafe ? '<img src=x onerror=alert(1)>' : 'Veo Production', code: 'veo-production', status: options.active ? 'Active' : 'Inactive' },
    organizations: options.ready || options.linked ? [{ organizationId: 'org-1', organizationPoolId: 'pool-1', organizationName: 'Studio A', organizationCode: 'studio-a', organizationStatus: 'Active',
      seatCapacity: options.full ? 5 : 50, activeSeats: options.full ? 4 : 0, reservedSeats: options.full ? 1 : 0, priority: 100, isAutoAssignmentEnabled: true, isReady: !!options.ready, readinessMessage: null }] : [],
    mappings: [], payment: !options.paymentOff, blocked: !!options.blocked, failPlan: options.failPlan, foreign: !!options.foreign,
    failAux: !!options.failAux, slow: !!options.slow
  };
  if (options.local) {
    plans[0].featureFlagsJson = '{"productScope":"vietsub","vietsubCloudEnabled":false}';
    state.organizations.forEach(org => {
      org.vietsubLocal = { ready: true, message: 'Tool dịch Local không yêu cầu API key hoặc ngân sách AI.' };
      org.vietsubCloud = { ready: false, message: 'Chưa có credential OpenAI.', target: 'credential' };
    });
  }
  const orgDirectory = () => [{ organizationId: 'org-1', name: 'Studio A', code: 'studio-a', role: 'Owner', status: 'Active', memberCount: 1, activeMemberCount: 1,
    monthlyBudgetLimit: state.blocked ? 0 : 100, actualCost: 0, reservedCost: 0, remainingBudget: 100, currencyCode: 'USD',
    aiReadiness: [{ providerCode: 'openai', ready: true, blockingReasons: [] }, { providerCode: 'fal', ready: !state.blocked, blockingReasons: state.blocked ? ['pricing_not_configured'] : [] }] }];
  const summary = () => {
    const ready = state.organizations.filter(org => org.isReady && org.isAutoAssignmentEnabled && org.organizationStatus === 'Active');
    const seats = ready.reduce((sum, org) => sum + Math.max(0, org.seatCapacity - org.activeSeats - org.reservedSeats), 0);
    return { ...state.pool, organizationCount: state.organizations.length, licensePlanCount: state.mappings.length,
      activeLicensePlanCount: state.mappings.filter(mapping => mapping.isActive).length, allocatableOrganizationCount: ready.length,
      seatCapacity: state.organizations.reduce((sum, org) => sum + org.seatCapacity, 0), activeSeats: 0, reservedSeats: 0,
      availableSeats: seats, allocatableAvailableSeats: state.pool.status === 'Active' && state.mappings.length ? seats : 0 };
  };
  const detail = () => ({ pool: summary(), organizations: state.organizations, licensePlans: state.mappings, recentAssignments: [] });
  const pageResult = items => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0, hasPrevious: false, hasNext: false });
  const foreignMapping = { licensePlanId: 'plan-3', planName: 'Gói 180 ngày', organizationPoolId: 'pool-2', organizationPoolName: 'Nhóm khác', isActive: true, isSellable: true };
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url()), method = request.method();
    if (url.origin !== 'https://admin.example.test') { unexpected.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    if (url.pathname === '/admin/admin-allocation-setup.js') return route.fulfill({ contentType: 'application/javascript', body: read('TOOL-SERVER/wwwroot/admin/admin-allocation-setup.js') });
    if (url.pathname === '/admin/admin-allocation-setup.css') return route.fulfill({ contentType: 'text/css', body: read('TOOL-SERVER/wwwroot/admin/admin-allocation-setup.css') });
    const bodyIn = request.postData() ? JSON.parse(request.postData()) : null;
    calls.push({ path: url.pathname, method, body: bodyIn });
    let body;
    const fail = (message, status = 409) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify({ code: 'fixture_error', message }) });
    if (url.pathname === '/api/admin/licenses/overview') body = { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 };
    else if (url.pathname === '/api/admin/licenses/users/page' || url.pathname === '/api/admin/desktop-releases/page') body = pageResult([]);
    else if (url.pathname === '/api/admin/licenses/plans') body = plans;
    else if (url.pathname === '/api/admin/payments/sepay') {
      if (state.failAux) return fail('Không tải được cấu hình SePay.', 503);
      if (state.slow) await new Promise(resolve => setTimeout(resolve, 400));
      body = { enabled: state.payment, selectedAccountId: 'receiver-1', accounts: [{ accountId: 'receiver-1' }] };
    } else if (url.pathname === '/api/organizations') body = orgDirectory();
    else if (url.pathname === '/api/organizations/page') body = pageResult(orgDirectory());
    else if (url.pathname === '/api/admin/ai-pricing') body = [];
    else if (url.pathname === '/api/admin/organization-pools/page') body = pageResult([summary()]);
    else if (url.pathname === '/api/admin/organization-pools') body = state.foreign ? [summary(), { organizationPoolId: 'pool-2', name: 'Nhóm khác', licensePlanCount: 1 }] : [summary()];
    else if (url.pathname === '/api/admin/organization-pools/pool-2') body = { pool: { organizationPoolId: 'pool-2' }, organizations: [], licensePlans: [foreignMapping], recentAssignments: [] };
    else if (url.pathname === '/api/admin/organization-pools/pool-1') {
      if (method === 'GET' && state.holdFinalRead && state.mappings.length) { state.finalReadHeld = true; await new Promise(resolve => { state.resumeFinalRead = resolve; }); }
      if (method === 'PUT') { state.pool = { ...state.pool, ...bodyIn }; body = summary(); } else body = detail();
    } else if (url.pathname === '/api/admin/organization-pools/pool-1/organizations/org-1') {
      if (bodyIn.isReady && state.blocked) return fail('Ngân sách AI đang bằng 0. Tổ chức chưa sẵn sàng.');
      const org = state.organizations[0] || { organizationId: 'org-1', organizationPoolId: 'pool-1', organizationName: 'Studio A', organizationCode: 'studio-a', organizationStatus: 'Active', activeSeats: 0, reservedSeats: 0 };
      Object.assign(org, bodyIn); org.availableSeats = Math.max(0, org.seatCapacity - org.activeSeats - org.reservedSeats);
      org.readinessMessage = bodyIn.isReady ? 'Credential, policy, pricing và budget đã sẵn sàng.' : null;
      state.organizations = [org]; body = org;
    } else if (url.pathname.startsWith('/api/admin/organization-pools/license-plans/')) {
      const id = url.pathname.split('/').at(-1);
      if (id === state.failPlan) return fail('Không lưu được liên kết gói.');
      const plan = plans.find(plan => plan.licensePlanId === id);
      const mapping = { ...bodyIn, licensePlanId: id, planName: plan.name, planCode: plan.planCode, organizationPoolName: 'Veo Production', isSellable: true,
        productScope: options.local && id === 'plan-1' ? 'vietsub' : 'all-tools', vietsubCloudEnabled: false };
      state.mappings = state.mappings.filter(row => row.licensePlanId !== id).concat(mapping); body = mapping;
    } else if (url.pathname.endsWith('/members/page')) body = pageResult([]);
    else if (url.pathname.endsWith('/usage/page')) body = { items: [], groups: [], budgetLimit: 100, actualCost: 0, reservedCost: 0, remainingBudget: 100, currencyCode: 'USD', itemsPage: 1, itemsPageSize: 50, itemsTotalCount: 0, itemsTotalPages: 0 };
    else if (url.pathname.endsWith('/providers') || url.pathname.endsWith('/members')) body = [];
    else if (url.pathname.endsWith('/video-policy')) body = null;
    else if (url.pathname === '/api/auth/logout') body = {};
    else { unexpected.push(url.pathname); return fail('Unexpected fixture endpoint', 404); }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-test-session');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ displayName: 'Admin', email: 'admin@example.test', roles: ['Admin'] }));
  });
  await page.goto('https://admin.example.test/');
  for (const file of ['admin.css', 'admin-modules.css', 'admin-allocation-setup.css']) await page.addStyleTag({ content: withAdminFonts(read(`TOOL-SERVER/wwwroot/admin/${file}`)) });
  for (const file of options.legacy ? ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-organizations.js'] : ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-allocation-setup.js', 'admin-organizations.js']) await page.addScriptTag({ content: read(`TOOL-SERVER/wwwroot/admin/${file}`) });
  await page.waitForSelector('#overviewMetrics .home-stat');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('organizations', { organizationScope: 'management' }));
  await page.click('#organizationPoolsScopeItem');
  await page.waitForSelector(options.legacy ? '#organizationPoolConsole [role="alert"]' : '.allocation-pool-cards');
  const open = async () => {
    await page.locator('.allocation-pool-cards [data-open-pool-setup]').click();
    await page.waitForFunction(() => document.querySelector('#allocationTitle') && document.querySelector('.allocation-guide')?.getAttribute('aria-busy') === 'false');
  };
  return { page, state, calls, open };
}

const stage = async (page, step) => {
  await page.locator(`.allocation-progress [data-allocation-step="${step}"]`).click();
  await page.waitForFunction(step => document.querySelector('[aria-current="step"]')?.dataset.allocationStep === String(step), step);
};
const settled = page => page.waitForFunction(() => document.querySelector('.allocation-guide')?.getAttribute('aria-busy') === 'false');
async function shot(page, name) {
  if (!process.env.VIDEOMAKER_SCREENSHOT_DIR) return;
  fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
  await page.evaluate(() => { window.scrollTo(0, 0); document.activeElement?.blur(); });
  await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, name + '.png'), fullPage: true });
}

test('Local-only plans are excluded from allocation setup', async t => {
  const { page, calls, open } = await fixture(t, { local: true, linked: true, blocked: true }); await open();
  assert.equal(await page.locator('#allocationProductProfile option[value="vietsub-local"]').count(), 0);
  await stage(page, 4);
  assert.equal(await page.locator('#allocationPlansForm input[name="plan"][value="plan-1"]').count(), 0);
  assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
});

test('Cloud allocation fails closed when only the old video readiness is available', async t => {
  const { page, calls, open } = await fixture(t, { ready: true }); await open();
  await page.selectOption('#allocationProductProfile', 'vietsub-cloud'); await stage(page, 3);
  assert.match(await page.locator('.allocation-guide').textContent(), /Server chưa cung cấp kiểm tra gói dịch/);
  assert.equal(await page.locator('button[data-allocation-step="4"]').last().isDisabled(), true);
  assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
});

test('overview explains missing setup without claiming readiness, escapes names and does not write', async t => {
  const { page, calls, open } = await fixture(t, { unsafe: true }); await open();
  assert.match(await page.locator('.allocation-guide').textContent(), /Nhóm chưa có tổ chức/);
  assert.equal(await page.locator('.allocation-guide img').count(), 0);
  assert.match(await page.locator('.allocation-guide').textContent(), /Thanh toán SePay đã bật/);
  assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
  await shot(page, '01-overview-1440');
});

test('missing canonical allocation module shows an error and never falls back to the old interface', async t => {
  const { page } = await fixture(t, { legacy: true });
  assert.equal(await page.locator('.allocation-progress button').count(), 0);
  assert.equal(await page.locator('link[href*="admin-allocation-setup.css"]').count(), 0);
  assert.equal(await page.locator('#organizationPoolConsole [role="alert"]').count(), 1);
  assert.equal(await page.locator('#organizationPoolConsole [data-open-pool]').count(), 0);
});

test('organization save preserves capacity and stays unready until server inspection', async t => {
  const { page, state, calls, open } = await fixture(t); await open(); await stage(page, 2);
  await page.fill('#allocationCapacity', '50'); await page.check('#allocationOrganizationForm [name=auto]');
  await page.locator('#allocationOrganizationForm [type=submit]').click();
  await page.waitForFunction(() => document.querySelector('[aria-current="step"]')?.dataset.allocationStep === '3'); await settled(page);
  assert.equal(state.organizations[0].isReady, false);
  assert.equal(calls.find(call => call.method === 'PUT').body.isReady, false);
  await page.locator('[data-allocation-action=inspect]').click(); await settled(page);
  await page.waitForFunction(() => document.querySelector('.allocation-notice h3')?.textContent.includes('vượt qua'));
  assert.equal(state.organizations[0].isReady, true);
});

test('server rejects readiness and the UI keeps the reason and configuration links', async t => {
  const { page, open } = await fixture(t, { linked: true, blocked: true }); await open(); await stage(page, 3);
  await page.locator('[data-allocation-action=inspect]').click();
  await page.waitForSelector('.allocation-error'); await settled(page);
  assert.match(await page.locator('.allocation-error').textContent(), /Ngân sách AI đang bằng 0/);
  assert.equal(await page.locator('.allocation-footer [data-allocation-step="4"]').isDisabled(), true);
  await page.locator('[data-allocation-action=fix-budget]').click();
  await page.waitForSelector('#organizationBudgetLimit');
  await page.locator('[data-allocation-return]').click(); await settled(page);
  assert.equal(await page.locator('[aria-current="step"]').getAttribute('data-allocation-step'), '3');
  await shot(page, '03-readiness-1440');
});

test('multiple plans save before activation, duplicate submit sends no duplicate writes', async t => {
  const { page, state, calls, open } = await fixture(t, { ready: true }); await open(); await stage(page, 4);
  assert.equal(await page.locator('#allocationPlansForm [type=submit]').isDisabled(), true);
  await page.check('[name=plan][value=plan-1]'); await page.check('[name=plan][value=plan-2]');
  await page.fill('#allocationMemberBudget', '12');
  await page.evaluate(() => { const form = document.querySelector('#allocationPlansForm'); form.requestSubmit(); form.requestSubmit(); });
  await page.waitForFunction(() => document.querySelector('.allocation-guide')?.getAttribute('aria-busy') === 'false' && document.querySelector('.allocation-guide')?.textContent.includes('Gói đã gắn có chỗ'));
  assert.equal(state.pool.status, 'Active'); assert.equal(state.mappings.length, 2);
  const writes = calls.filter(call => call.method === 'PUT');
  assert.equal(writes.length, 3); assert.equal(writes.at(-1).path, '/api/admin/organization-pools/pool-1');
  assert.equal(writes[0].body.defaultMemberMonthlyBudgetLimit, 12);
  await shot(page, '04-plans-1440');
});

test('partial save reports persisted mappings, preserves budget and does not activate on failure', async t => {
  const { page, state, open } = await fixture(t, { ready: true, failPlan: 'plan-2' }); await open(); await stage(page, 4);
  await page.check('[name=plan][value=plan-1]'); await page.check('[name=plan][value=plan-2]'); await page.fill('#allocationMemberBudget', '12');
  await page.locator('#allocationPlansForm [type=submit]').click(); await page.waitForSelector('.allocation-error'); await settled(page);
  assert.match(await page.locator('.allocation-error').textContent(), /Đã lưu 1 liên kết gói/);
  assert.equal(state.pool.status, 'Inactive'); assert.equal(state.mappings.length, 1);
  assert.equal(await page.inputValue('#allocationMemberBudget'), '12');
  state.failPlan = null;
  await page.locator('#allocationPlansForm [type=submit]').click();
  await page.waitForFunction(() => document.querySelector('.allocation-guide')?.textContent.includes('Gói đã gắn có chỗ'));
  assert.equal(state.mappings.length, 2); assert.equal(state.mappings[0].defaultMemberMonthlyBudgetLimit, 12);
});

test('foreign mappings, capacity shortage, and SePay availability disable unsafe setup', async t => {
  const foreign = await fixture(t, { ready: true, foreign: true }); await foreign.open(); await stage(foreign.page, 4);
  assert.equal(await foreign.page.locator('[name=plan][value=plan-3]').isDisabled(), true);
  assert.match(await foreign.page.locator('.allocation-plan-table').textContent(), /Đang ở nhóm Nhóm khác/);
  const full = await fixture(t, { ready: true, full: true }); await full.open(); await stage(full.page, 4);
  assert.equal(await full.page.locator('#allocationPlansForm [type=submit]').isDisabled(), true);
  const disabled = await fixture(t, { ready: true, paymentOff: true }); await disabled.open(); await stage(disabled.page, 4);
  assert.equal(await disabled.page.locator('#allocationPlansForm [type=submit]').isDisabled(), true);
});

test('rechecks changed mapping and payment state before any writes', async t => {
  const { page, state, calls, open } = await fixture(t, { ready: true }); await open(); await stage(page, 4); await page.check('[name=plan][value=plan-3]');
  state.foreign = true;
  await page.locator('#allocationPlansForm [type=submit]').click(); await page.waitForSelector('.allocation-error'); await settled(page);
  assert.match(await page.locator('.allocation-error').textContent(), /đã thuộc nhóm/);
  assert.equal(calls.filter(call => call.method === 'PUT').length, 0);
  const second = await fixture(t, { ready: true }); await second.open(); await stage(second.page, 4); await second.page.check('[name=plan][value=plan-1]');
  second.state.payment = false;
  await second.page.locator('#allocationPlansForm [type=submit]').click(); await second.page.waitForSelector('.allocation-error'); await settled(second.page);
  assert.match(await second.page.locator('.allocation-error').textContent(), /SePay chưa sẵn sàng/);
  assert.equal(second.calls.filter(call => call.method === 'PUT').length, 0);
});

test('auxiliary API failures remain errors and can retry without inventing readiness', async t => {
  const { page, state, open } = await fixture(t, { ready: true, failAux: true }); await open(); await stage(page, 4);
  assert.match(await page.locator('.allocation-error').textContent(), /Không tải được/);
  assert.equal(await page.locator('#allocationPlansForm [type=submit]').isDisabled(), true);
  state.failAux = false;
  await page.locator('[data-allocation-action=reload]').click(); await settled(page);
  assert.equal(await page.locator('.allocation-error').count(), 0);
});

test('late auxiliary responses after closing the pool or logout cannot resurrect the guide', async t => {
  const { page, calls } = await fixture(t, { slow: true });
  await page.locator('.allocation-pool-cards [data-open-pool-setup]').click();
  await page.waitForSelector('#allocationTitle');
  await page.locator('.allocation-intro [data-close-pool]').click();
  await page.waitForSelector('.allocation-pool-cards');
  await page.waitForTimeout(450);
  assert.equal(await page.locator('#allocationTitle').count(), 0);
  await page.click('#logoutButton'); await page.waitForSelector('#loginScreen:not(.hidden)');
  assert.equal(calls.filter(call => call.method === 'PUT').length, 0);
});

test('logout during the final verification prevents activation after earlier mapping save', async t => {
  const { page, state, calls, open } = await fixture(t, { ready: true }); await open(); await stage(page, 4); await page.check('[name=plan][value=plan-1]');
  state.holdFinalRead = true;
  await page.locator('#allocationPlansForm [type=submit]').click();
  for (let i = 0; !state.finalReadHeld && i < 100; i++) await new Promise(resolve => setTimeout(resolve, 20));
  assert.equal(state.finalReadHeld, true);
  await page.click('#logoutButton'); await page.waitForSelector('#loginScreen:not(.hidden)');
  state.holdFinalRead = false; state.resumeFinalRead();
  await page.waitForTimeout(150);
  assert.equal(state.pool.status, 'Inactive');
  assert.equal(calls.some(call => call.method === 'PUT' && call.path === '/api/admin/organization-pools/pool-1'), false);
});

test('four stages support keyboard focus and do not overflow at five widths', async t => {
  const { page, open } = await fixture(t, { ready: true }); await open();
  for (const width of [1440, 1024, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 1050 });
    for (const value of [1, 2, 3, 4]) {
      await stage(page, value);
      assert.equal(await page.evaluate(() => document.activeElement.id), 'allocationTitle');
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `No page overflow ${width} / stage ${value}`);
      if (value === 1 && width === 1440) await shot(page, '01-overview-1440');
      if (value === 2 && width === 1440) await shot(page, '02-organization-1440');
      if (width === 390) await shot(page, `stage-${value}-390`);
    }
  }
});
