// Uses the actual Razor, CSS and JavaScript with intercepted, synthetic API responses.
// No application server, database, provider or release endpoint is contacted.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const razor = read('TOOL-SERVER/Pages/Admin/Index.cshtml');
const html = razor.slice(razor.indexOf('<!doctype')).replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '').replace(/<link\b[^>]*>/gi, '');
const days = amount => new Date(Date.now() + amount * 86400000).toISOString();
const plans = ['Cơ bản', 'Chuyên nghiệp', 'Doanh nghiệp'].map((name, i) => ({
  licensePlanId: `plan-${i}`, name, planCode: `plan-${i}`, defaultDurationDays: i === 2 ? 90 : 30,
  maxActivatedDevices: [1, 3, 5][i], maxConcurrentSessions: i + 1, offlineGraceHours: 0,
  isActive: true, isPublic: true, description: 'Tạo video và xử lý phụ đề cho công việc hằng ngày.', salePriceVnd: (i + 1) * 199000
}));
const names = ['Minh Anh', 'Hoàng Nam', 'Thu Hà', 'Quốc Bảo', 'Lan Chi', 'Gia Huy'];
const users = names.map((name, i) => ({ userId: `user-${i}`, displayName: name, email: `user${i}@example.test`, accountStatus: 'Active',
  registeredDeviceCount: i % 3 + 1, activeSessionCount: i % 2, lastLoginAtUtc: days(-1),
  currentLicense: { userLicenseId: `license-${i}`, licensePlanId: `plan-${i % 3}`, planName: plans[i % 3].name, planCode: `plan-${i % 3}`,
    status: 'Active', startsAtUtc: days(-20), expiresAtUtc: days(i % 3 === 1 ? 4 : 30), activeDeviceCount: i % 3 + 1 }
}));
const organization = { organizationId: 'org-1', name: 'Studio Việt', code: 'STUDIO-VIET', role: 'Owner', status: 'Active', memberCount: 8, activeMemberCount: 6,
  monthlyBudgetLimit: 500, actualCost: 84, reservedCost: 12, remainingBudget: 404, currencyCode: 'USD', aiReadiness: [] };
const members = [{ userId: 'user-0', displayName: 'Minh Anh', email: 'user0@example.test', role: 'Owner', status: 'Active', monthlyBudgetLimit: null, joinedAtUtc: days(-30) }];
const pricing = [{ providerId: 'provider-1', providerCode: 'openai', displayName: 'OpenAI', isEnabled: true,
  models: [{ providerModelId: 'model-1', displayName: 'Model nội dung', modelCode: 'text-model', modality: 'Text', isEnabled: true, isDefault: true, costRates: [] }] }];
const pageResult = (items, url, totalCount = items.length) => {
  const page = Number(url.searchParams.get('page') || 1), pageSize = Number(url.searchParams.get('pageSize') || 20);
  const totalPages = Math.ceil(totalCount / pageSize);
  return { items, page, pageSize, totalCount, totalPages, hasPrevious: page > 1, hasNext: page < totalPages };
};
let browser;
before(async () => { browser = await chromium.launch({ headless: true }); });
after(async () => { await browser?.close(); });

async function fixture(t, { width = 1440, empty = false, failOverview = false, login = false, unsafeName = false } = {}) {
  const context = await browser.newContext({ viewport: { width, height: 1050 }, locale: 'vi-VN', timezoneId: 'Asia/Ho_Chi_Minh' });
  const page = await context.newPage();
  const errors = [], calls = [], unexpected = [];
  page.on('pageerror', error => errors.push(error.message));
  t.after(async () => { await context.close(); assert.deepEqual(errors, [], 'No unhandled browser errors'); assert.deepEqual(unexpected, [], 'Only expected local fixture APIs are used'); });
  const rows = empty ? [] : structuredClone(users);
  if (unsafeName) rows[0].displayName = '<img src=x onerror=alert(1)>';
  await page.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.origin !== 'https://admin.example.test') { unexpected.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    const method = route.request().method();
    calls.push({ path: url.pathname, search: url.searchParams.get('search'), page: url.searchParams.get('page'), method });
    let body;
    if (url.pathname.endsWith('/overview')) {
      if (failOverview) return route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Không tải được thống kê.' }) });
      body = { totalUsers: empty ? 0 : 1284, activeLicenses: empty ? 0 : 968, onlineSessions: empty ? 0 : 246, expiringWithinSevenDays: empty ? 0 : 24 };
    } else if (url.pathname === '/api/admin/licenses/plans') body = empty ? [] : plans;
    else if (url.pathname === '/api/admin/licenses/users/page') {
      const term = (url.searchParams.get('search') || '').toLowerCase();
      const result = rows.filter(user => `${user.displayName} ${user.email}`.toLowerCase().includes(term));
      body = pageResult(result, url, term ? result.length : empty ? 0 : 48);
    } else if (url.pathname.startsWith('/api/admin/licenses/users/')) {
      const user = rows.find(user => url.pathname.endsWith(user.userId)) || users[0];
      body = { user, licenses: [user.currentLicense], devices: [], sessions: [] };
    } else if (url.pathname === '/api/admin/desktop-releases/page') body = pageResult(empty ? [] : [{ releaseId: 'release-1', version: '1.0.0', buildNumber: 1,
      platform: 'win-x64', channel: 'Stable', isActive: true, isMandatory: false, publishedAtUtc: days(-1), artifacts: [{ kind: 'DesktopPackage', sizeBytes: 1024, sha256: 'a'.repeat(64) }] }], url);
    else if (url.pathname === '/api/organizations') body = [organization];
    else if (url.pathname === '/api/organizations/page') body = pageResult([organization], url);
    else if (url.pathname === '/api/organizations/org-1/members/page') body = pageResult(members, url);
    else if (url.pathname === '/api/organizations/org-1/members') body = members;
    else if (url.pathname === '/api/organizations/org-1/usage/page') body = { items: [], groups: [], budgetLimit: 500, actualCost: 84, reservedCost: 12, remainingBudget: 404,
      currencyCode: 'USD', periodStartsAtUtc: days(-20), periodEndsAtUtc: days(10), inputTokens: 24000, outputTokens: 8000, videoSeconds: 32, itemsPage: 1, itemsPageSize: 50, itemsTotalCount: 0, itemsTotalPages: 0 };
    else if (url.pathname === '/api/organizations/org-1/providers') body = [];
    else if (url.pathname === '/api/organizations/org-1/video-policy') body = null;
    else if (url.pathname === '/api/organizations/org-1/audit/page') body = pageResult([{ occurredAtUtc: days(-1), eventType: 'organization.created', actorDisplayName: 'Minh Anh', data: { name: 'Studio Việt' }, correlationId: 'fixture-event-1' }], url);
    else if (url.pathname === '/api/admin/ai-pricing') body = pricing;
    else if (url.pathname === '/api/admin/organization-pools') body = [];
    else if (url.pathname === '/api/admin/organization-pools/page') body = pageResult([], url);
    else if (url.pathname === '/api/auth/logout') body = {};
    else { unexpected.push(url.pathname); return route.fulfill({ status: 404, body: '{}' }); }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
  });
  if (!login) await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-test-session');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ displayName: 'Quản trị viên', email: 'admin@example.test', roles: ['Admin'] }));
  });
  await page.goto('https://admin.example.test/');
  await page.addStyleTag({ content: read('TOOL-SERVER/wwwroot/admin/admin.css') });
  for (const file of ['admin.js', 'admin-organizations.js']) await page.addScriptTag({ content: read(`TOOL-SERVER/wwwroot/admin/${file}`) });
  if (!login) await page.waitForSelector('#overviewMetrics .metric-card');
  return { page, calls };
}

async function screenshot(page, name) {
  if (!process.env.VIDEOMAKER_SCREENSHOT_DIR) return;
  fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
  await page.evaluate(async () => { await document.fonts.ready; await new Promise(resolve => setTimeout(resolve, 220)); });
  await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `${name}.png`), fullPage: true });
}

test('overview uses live fixture metrics, expiry dates, real detail actions and escapes user names', async t => {
  const { page, calls } = await fixture(t, { unsafeName: true });
  assert.equal(await page.locator('#overviewMetrics .metric-card strong').first().textContent(), '1.284');
  assert.equal(await page.locator('#userOverview tbody tr').count(), 6);
  assert.equal(await page.locator('#userOverview img').count(), 0);
  assert.equal(await page.locator('#userOverview .status-warning').count(), 2);
  assert.match(await page.locator('#expiryTitle').textContent(), /^24 license/);
  await page.locator('#userOverview [data-user-id="user-1"]').click();
  await page.waitForSelector('#userDetail .user-detail-header');
  assert.match(await page.locator('#userDetail').textContent(), /Hoàng Nam/);
  await page.keyboard.press('Escape');
  assert.equal(await page.locator('#userDialog').evaluate(node => node.open), false);
  assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
});

test('header search routes to paginated users and leaves the overview snapshot intact', async t => {
  const { page, calls } = await fixture(t);
  await page.fill('#adminSearch', 'Thu Hà');
  await page.locator('#adminSearch').press('Enter');
  await page.waitForFunction(() => document.querySelector('#userTable tbody')?.children.length === 1);
  assert.equal(await page.inputValue('#userSearch'), 'Thu Hà');
  assert.equal(calls.some(call => call.search === 'Thu Hà'), true);
  await page.click('.nav-item[data-view="overview"]');
  assert.equal(await page.locator('#userOverview tbody tr').count(), 6);
  await page.click('#manageUsersShortcut');
  await page.fill('#userSearch', '');
  await page.locator('#userSearch').press('Enter');
  await page.waitForSelector('[data-pagination="users"] [data-page="2"]');
  await page.locator('[data-pagination="users"] [data-page="2"]').first().click();
  await page.waitForFunction(() => window.videoMakerAdminShell.state.usersPaging.page === 2);
  assert.equal(calls.some(call => call.page === '2'), true);
});

test('empty and unavailable data remain distinguishable without fabricated totals', async t => {
  const { page } = await fixture(t, { empty: true });
  assert.equal(await page.locator('#overviewMetrics strong').first().textContent(), '0');
  assert.match(await page.locator('#userOverview').textContent(), /Không có license/);
  assert.match(await page.locator('#planOverview').textContent(), /Chưa có gói/);
  const failed = await fixture(t, { failOverview: true });
  assert.equal(await failed.page.locator('#overviewMetrics strong').first().textContent(), '—');
  assert.match(await failed.page.locator('#expiryDescription').textContent(), /Chưa tải được/);
});

test('mobile navigation, keyboard focus and logout remain accessible', async t => {
  const { page } = await fixture(t, { width: 390 });
  assert.equal(await page.locator('#adminNavigation').isVisible(), false);
  await page.locator('#adminMenuToggle').focus();
  await page.keyboard.press('Enter');
  assert.equal(await page.locator('#adminNavigation').isVisible(), true);
  await page.keyboard.press('Escape');
  assert.equal(await page.getAttribute('#adminMenuToggle', 'aria-expanded'), 'false');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'adminMenuToggle');
  assert.notEqual(await page.locator('#adminMenuToggle').evaluate(node => getComputedStyle(node).outlineStyle), 'none');
  await page.click('#logoutButton');
  await page.waitForSelector('#loginScreen:not(.hidden)');
  assert.equal(await page.locator('#adminShell').isVisible(), false);
  await screenshot(page, 'login-mobile');
});

test('all shell pages remain square and borderless without page overflow at five widths', async t => {
  const { page } = await fixture(t);
  for (const width of [390, 768, 1024, 1440, 1920]) {
    await page.setViewportSize({ width, height: 1050 });
    for (const view of ['overview', 'users', 'plans', 'releases']) {
      await page.evaluate(view => window.videoMakerAdminShell.navigate(view), view);
      const geometry = await page.evaluate(() => {
        const elements = [...document.querySelectorAll('.panel, .metric-card, .plan-card-admin, .nav-item, .user-avatar, .status-pill, .primary-button, .ghost-button, .pagination-page, input')]
          .filter(node => node.getClientRects().length);
        return { overflow: document.documentElement.scrollWidth > innerWidth + 1,
          invalid: elements.filter(node => { const s = getComputedStyle(node); return parseFloat(s.borderTopLeftRadius) !== 0 || [s.borderTopWidth, s.borderRightWidth, s.borderBottomWidth, s.borderLeftWidth].some(value => parseFloat(value) !== 0); }).map(node => node.className) };
      });
      assert.equal(geometry.overflow, false, `${view} at ${width}px must not overflow the page`);
      assert.deepEqual(geometry.invalid, [], `${view} at ${width}px must match square borderless design`);
      if (width === 1440 || (width === 390 && view === 'overview')) await screenshot(page, `${view}-${width}`);
    }
  }
  await page.evaluate(() => window.videoMakerAdminShell.navigate('overview'));
  await screenshot(page, 'overview-1920');
});

test('plan and release dialogs retain validation, keyboard dismissal and mobile sizing', async t => {
  const { page, calls } = await fixture(t, { width: 390 });
  await page.evaluate(() => window.videoMakerAdminShell.navigate('plans'));
  await page.click('#addPlanButton');
  await page.locator('#planForm button[type="submit"]').click();
  assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
  assert.equal(await page.locator('#planCode').evaluate(node => node.validity.valueMissing), true);
  await screenshot(page, 'plan-dialog-mobile');
  await page.keyboard.press('Escape');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('releases'));
  await page.click('#addReleaseButton');
  const bounds = await page.locator('#releaseDialog').boundingBox();
  assert.ok(bounds.x >= 0 && bounds.x + bounds.width <= 391);
  assert.equal(await page.locator('#releaseDialog').evaluate(node => getComputedStyle(node).borderTopLeftRadius), '0px');
  await page.keyboard.press('Escape');
});

test('organization setup, directory, pricing and cost guide keep readable responsive layouts', async t => {
  const { page } = await fixture(t);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1050 });
    await page.evaluate(() => window.videoMakerAdminShell.navigate('organizations', { organizationScope: 'setup' }));
    await page.waitForSelector('#adminSetupCenter .setup-center-hero');
    for (const scope of ['setup', 'directory', 'pools', 'pricing', 'cost-guide']) {
      await page.evaluate(scope => window.videoMakerOrganizationAdmin.activate(scope), scope);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `${scope} at ${width}px`);
      if (width === 1440) await screenshot(page, `organization-${scope}`);
    }
  }
});

test('organization detail tabs retain readable content and controls on mobile and desktop', async t => {
  const { page, calls } = await fixture(t);
  await page.evaluate(() => window.videoMakerAdminShell.navigate('organizations', { organizationScope: 'directory' }));
  await page.click('[data-open-organization="org-1"]');
  const markers = { overview: '.organization-setup-hero', members: '#organizationMemberSearch', usage: '.usage-ledger', providers: '.provider-admin-grid', audit: '.audit-table' };
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1050 });
    for (const [tab, marker] of Object.entries(markers)) {
      await page.click(`[data-organization-tab="${tab}"]`);
      await page.waitForSelector(`#organizationTabContent ${marker}`);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true, `organization ${tab} at ${width}px`);
      assert.equal(await page.getAttribute(`[data-organization-tab="${tab}"]`, 'aria-selected'), 'true');
      if (tab === 'providers' || (width === 1440 && tab === 'usage')) await screenshot(page, `organization-detail-${tab}-${width}`);
    }
  }
  assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
});

test('login stays usable at narrow width and reduced motion and high contrast preserve focus', async t => {
  const { page } = await fixture(t, { width: 390, login: true });
  await page.emulateMedia({ reducedMotion: 'reduce', forcedColors: 'active' });
  await page.locator('#loginEmail').focus();
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
  await page.keyboard.press('Tab');
  assert.equal(await page.evaluate(() => document.activeElement.id), 'loginPassword');
  assert.notEqual(await page.locator('#loginPassword').evaluate(node => getComputedStyle(node).outlineStyle), 'none');
  await page.emulateMedia({ forcedColors: 'none' });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await screenshot(page, 'login-desktop');
});
