// Actual admin source with synthetic API responses; never contacts a running server or database.
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
const endpoint = '/api/admin/payments/sepay';
const account = (id, bankCode, number) => ({ accountId: id, displayName: id === 'account-1' ? 'Tài khoản chính' : 'Dự phòng',
  bankCode, bankName: bankCode === 'VCB' ? 'Vietcombank' : 'Techcombank', accountNumber: number,
  accountName: 'TEST HOLDER', revision: `account-revision-${id}` });
let browser;
before(async () => { browser = await chromium.launch({ headless: true, channel: process.env.VIDEOMAKER_BROWSER_CHANNEL }); });
after(async () => { await browser?.close(); });

async function fixture(t, options = {}) {
  const context = await browser.newContext({ viewport: { width: options.width || 1600, height: 1050 }, locale: 'vi-VN' });
  const page = await context.newPage();
  const errors = [], calls = [], unexpected = [];
  let value = { enabled: !options.empty, selectedAccountId: options.empty ? null : 'account-1', revision: 'settings-1',
    accounts: options.empty ? [] : [account('account-1', 'VCB', '11111111'), account('account-2', 'TCB', '22222222')] };
  let failRead = Boolean(options.failRead), reads = 0, version = 1, writeGate = null;
  page.on('pageerror', error => errors.push(error.message));
  t.after(async () => { await context.close(); assert.deepEqual(errors, []); assert.deepEqual(unexpected, []); });
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url()), method = request.method();
    if (url.origin !== 'https://admin.example.test') { unexpected.push(url.origin); return route.abort(); }
    if (url.pathname === '/') return route.fulfill({ contentType: 'text/html', body: html });
    const body = method === 'GET' ? null : request.postDataJSON();
    calls.push({ path: url.pathname, method, body });
    let response = null, status = 200;
    if (url.pathname === endpoint && method === 'GET') {
      reads++;
      if (failRead) { failRead = false; status = 503; response = { message: 'Chưa tải được cấu hình thanh toán.' }; }
      else response = value;
    } else if (url.pathname.startsWith(endpoint) && method !== 'GET') {
      if (writeGate) await writeGate;
      if (options.conflict) { status = 409; response = { code: 'sepay_settings_changed', message: 'Cấu hình đã được thay đổi. Vui lòng làm mới rồi thử lại.' }; }
      else {
        if (url.pathname.endsWith('/enabled')) {
          if (body.enabled && !value.selectedAccountId) { status = 400; response = { message: 'Vui lòng chọn tài khoản nhận tiền trước.' }; }
          else value.enabled = body.enabled;
        } else if (url.pathname.endsWith('/selected-account')) value.selectedAccountId = body.accountId;
        else {
          const id = method === 'POST' ? `account-${value.accounts.length + 1}` : url.pathname.split('/').at(-1);
          const saved = { accountId: id, ...body, revision: `account-revision-${++version}` };
          value.accounts = [...value.accounts.filter(x => x.accountId !== id), saved];
          if (body.useForNewOrders) value.selectedAccountId = id;
        }
        if (!response) { value.revision = `settings-${++version}`; response = options.malformedWrite ? { unexpected: true } : value; }
      }
    } else if (url.pathname === '/api/admin/licenses/overview') response = { totalUsers: 0, activeLicenses: 0, onlineSessions: 0, expiringWithinSevenDays: 0 };
    else if (url.pathname === '/api/admin/licenses/plans') response = [];
    else if (url.pathname.endsWith('/page')) response = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 };
    else { unexpected.push(url.pathname); status = 404; response = {}; }
    return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(response) });
  });
  await page.addInitScript(() => {
    sessionStorage.setItem('vmAdminAccessToken', 'synthetic-test-session');
    sessionStorage.setItem('vmAdminUser', JSON.stringify({ displayName: 'Test Admin', roles: ['Admin'] }));
  });
  await page.goto('https://admin.example.test/');
  await page.addStyleTag({ content: withAdminFonts(read('TOOL-SERVER/wwwroot/admin/admin.css')) });
  await page.addStyleTag({ content: read('TOOL-SERVER/wwwroot/admin/admin-modules.css') });
  for (const file of ['admin-pages.js', 'admin-dialogs.js', 'admin.js', 'admin-sepay.js']) await page.addScriptTag({ content: read(`TOOL-SERVER/wwwroot/admin/${file}`) });
  await page.waitForSelector('#overviewMetrics .home-stat');
  await page.evaluate(() => window.videoMakerAdminShell.navigate('payments'));
  await page.waitForFunction(() => document.querySelector('[data-panel="payments"]').getAttribute('aria-busy') === 'false');
  return { page, calls, state: () => value, reads: () => reads, holdWrites: () => {
    let release; writeGate = new Promise(resolve => { release = resolve; }); return release;
  } };
}

test('loads current SQL settings lazily and switches the receiver without enabling paused sales', async t => {
  const { page, calls, state, reads } = await fixture(t);
  assert.equal(reads(), 1);
  assert.equal(await page.locator('#sepayAccounts [data-sepay-account]').count(), 2);
  assert.equal(await page.inputValue('#sepayAccountNumber'), '11111111');
  await page.locator('.sepay-status-bar .toggle-row').click();
  await page.waitForFunction(() => !document.getElementById('sepayEnabled').checked && !document.getElementById('sepayEnabled').disabled);
  await page.locator('[data-sepay-select="account-2"]').click();
  await page.waitForFunction(() => document.getElementById('sepayStatus').textContent.includes('22222222'));
  assert.equal(state().selectedAccountId, 'account-2');
  assert.equal(state().enabled, false);
  assert.match(await page.locator('#sepayAccounts .status-pill').textContent(), /Đã chọn/);
  assert.equal(calls.filter(x => x.method === 'PUT').length, 2);
});

test('adds a bank account, blocks duplicate submit while saving, and keeps selection separate from sales', async t => {
  const { page, calls, state, holdWrites } = await fixture(t, { empty: true });
  assert.match(await page.locator('#sepayAccounts').textContent(), /Chưa có tài khoản/);
  await page.fill('#sepayDisplayName', 'Nhận thanh toán');
  await page.fill('#sepayAccountNumber', '33333333');
  await page.fill('#sepayAccountName', 'TEST COMPANY');
  const release = holdWrites();
  await page.locator('#sepaySave').click();
  await page.waitForFunction(() => document.getElementById('sepayFields').disabled);
  await page.locator('#sepayAccountForm').evaluate(form => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await page.waitForTimeout(80);
  assert.equal(calls.filter(x => x.method === 'POST').length, 1);
  release();
  await page.waitForFunction(() => !document.getElementById('sepayFields').disabled);
  assert.equal(state().accounts.length, 1);
  assert.equal(state().enabled, false);
  assert.equal(state().selectedAccountId, 'account-1');
  assert.equal(calls.find(x => x.method === 'POST').body.settingsRevision, 'settings-1');
});

test('edits and saves the receiver revision, cancels drafts and displays conflicts without losing input', async t => {
  const { page, calls } = await fixture(t);
  await page.fill('#sepayDisplayName', 'Draft name');
  await page.locator('#sepayCancel').click();
  assert.equal(await page.inputValue('#sepayDisplayName'), 'Tài khoản chính');
  await page.fill('#sepayAccountName', 'UPDATED HOLDER');
  await page.locator('#sepaySave').click();
  await page.waitForFunction(() => !document.getElementById('sepayFields').disabled);
  const update = calls.find(x => x.path.endsWith('/accounts/account-1'));
  assert.equal(update.method, 'PUT');
  assert.equal(update.body.revision, 'account-revision-account-1');
  assert.equal(update.body.accountName, 'UPDATED HOLDER');
  const failed = await fixture(t, { conflict: true });
  await failed.page.fill('#sepayAccountName', 'UNSAVED HOLDER');
  await failed.page.locator('#sepaySave').click();
  await failed.page.waitForFunction(() => document.getElementById('sepayFormMessage').textContent.includes('đã được thay đổi'));
  assert.equal(await failed.page.inputValue('#sepayAccountName'), 'UNSAVED HOLDER');
  assert.equal(await failed.page.locator('#sepayAccountNumber').isDisabled(), false);
});

test('handles loading failure, retry, invalid input and rejected enable without optimistic success', async t => {
  const { page, calls } = await fixture(t, { empty: true, failRead: true });
  assert.equal(await page.locator('#sepayAccountNumber').isDisabled(), true);
  assert.match(await page.locator('#sepayNotice').textContent(), /Chưa tải được/);
  await page.locator('#sepayRetry').click();
  await page.waitForFunction(() => !document.getElementById('sepayFields').disabled);
  await page.fill('#sepayAccountNumber', '123');
  await page.locator('#sepaySave').click();
  assert.equal(calls.filter(x => x.method === 'POST').length, 0);
  await page.locator('.sepay-status-bar .toggle-row').click();
  await page.waitForFunction(() => document.getElementById('sepayNotice').textContent.includes('chọn tài khoản'));
  assert.equal(await page.locator('#sepayEnabled').isChecked(), false);
});

test('preserves dirty form and original revision when refreshing instead of silently overwriting another admin', async t => {
  const { page, calls, state } = await fixture(t);
  await page.fill('#sepayDisplayName', 'Unsaved draft');
  state().revision = 'settings-from-other-admin';
  await page.locator('#refreshButton').click();
  await page.waitForFunction(() => !document.getElementById('sepayFields').disabled);
  assert.equal(await page.inputValue('#sepayDisplayName'), 'Unsaved draft');
  await page.locator('#sepaySave').click();
  await page.waitForFunction(() => !document.getElementById('sepayFields').disabled);
  assert.equal(calls.find(x => x.path.endsWith('/accounts/account-1')).body.settingsRevision, 'settings-1');
});

test('invalid write responses keep the draft and request reconciliation instead of reporting success', async t => {
  const { page } = await fixture(t, { malformedWrite: true });
  await page.fill('#sepayAccountName', 'RECONCILE HOLDER');
  await page.locator('#sepaySave').click();
  await page.waitForFunction(() => document.getElementById('sepayFormMessage').textContent.includes('làm mới'));
  assert.equal(await page.inputValue('#sepayAccountName'), 'RECONCILE HOLDER');
  assert.equal(await page.locator('#sepayAccountNumber').isDisabled(), false);
  assert.doesNotMatch(await page.locator('#toastStack').textContent(), /Đã lưu tài khoản/);
});

test('supports another bank and copies the current webhook URL with a manual-copy fallback', async t => {
  const { page, calls } = await fixture(t, { empty: true });
  await page.fill('#sepayDisplayName', 'Ngân hàng khác');
  await page.selectOption('#sepayBank', 'other');
  await page.fill('#sepayBankCode', 'testbank');
  await page.fill('#sepayBankName', 'Ngân hàng kiểm thử');
  await page.fill('#sepayAccountNumber', '44444444');
  await page.fill('#sepayAccountName', 'TEST COMPANY');
  await page.locator('#sepaySave').click();
  await page.waitForFunction(() => !document.getElementById('sepayFields').disabled);
  const saved = calls.find(x => x.method === 'POST');
  assert.equal(saved.body.bankCode, 'TESTBANK');
  assert.equal(saved.body.bankName, 'Ngân hàng kiểm thử');
  assert.equal(await page.inputValue('#sepayBank'), 'other');
  await page.evaluate(() => { navigator.clipboard.writeText = async text => { window.testCopiedText = text; }; });
  await page.locator('#sepayCopyWebhook').click();
  assert.equal(await page.evaluate(() => window.testCopiedText), 'https://admin.example.test/api/payments/sepay/webhook');
  await page.evaluate(() => { navigator.clipboard.writeText = async () => { throw new Error('denied'); }; });
  await page.locator('#sepayCopyWebhook').click();
  await page.waitForFunction(() => document.getElementById('sepayNotice').textContent.includes('Ctrl+C'));
  assert.equal(await page.locator('#sepayWebhookUrl').evaluate(input => input === document.activeElement), true);
});

test('clears receiving-account data on session reset and ignores late write responses', async t => {
  const { page, holdWrites } = await fixture(t);
  await page.fill('#sepayAccountName', 'LATE RESPONSE');
  const release = holdWrites();
  await page.locator('#sepaySave').click();
  await page.waitForFunction(() => document.getElementById('sepayFields').disabled);
  await page.evaluate(() => window.videoMakerSepayAdmin.reset());
  release();
  await page.waitForTimeout(180);
  assert.equal(await page.inputValue('#sepayAccountNumber'), '');
  assert.equal(await page.locator('#sepayAccounts').textContent(), '');
  assert.equal(await page.locator('#sepayAccountNumber').isDisabled(), true);
});

test('stays usable at desktop, tablet and mobile widths and escapes bank/account names', async t => {
  for (const width of [1920, 1440, 1024, 768, 390]) {
    const { page, state } = await fixture(t, { width });
    const dimensions = await page.evaluate(() => ({ viewport: innerWidth, content: document.documentElement.scrollWidth }));
    assert.ok(dimensions.content <= dimensions.viewport + 1, `No page overflow at ${width}px`);
    await page.locator('[data-sepay-edit="account-1"]').click();
    assert.equal(await page.locator('#sepayDisplayName').evaluate(node => node === document.activeElement), true);
    await page.locator('#sepayAccountName').scrollIntoViewIfNeeded();
    assert.equal(await page.locator('#sepayAccountName').isVisible(), true);
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
      fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `sepay-admin-${width}.png`), fullPage: true });
    }
    state().accounts[0].displayName = '<img src=x onerror=alert(1)>';
    await page.evaluate(() => window.videoMakerSepayAdmin.refresh());
    assert.equal(await page.locator('#sepayAccounts img').count(), 0);
    assert.match(await page.locator('#sepayAccounts').textContent(), /<img src=x/);
  }
});
