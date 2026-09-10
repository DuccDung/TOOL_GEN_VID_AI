// Renders the real React component and CSS with synthetic data; all network is blocked.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { createRequire, Module } = require('node:module');
const repo = path.resolve(__dirname, '../..');
const webRequire = createRequire(path.join(repo, 'TOOL-LOCAL/Web/package.json'));
const { build } = webRequire('rolldown');
const { createElement } = webRequire('react');
const { renderToStaticMarkup } = webRequire('react-dom/server');
const { chromium } = require(process.env.VIDEOMAKER_PLAYWRIGHT_MODULE || 'playwright');
let browser, TikTokPage, interactiveBundle;
before(async () => {
  const filename = path.join(repo, 'TOOL-LOCAL/Web/src/features/tiktok/TikTokPage.tsx');
  const result = await build({ input: filename, external: ['react', 'react/jsx-runtime', 'lucide-react'], write: false, output: { format: 'cjs' } });
  const compiled = new Module(filename);
  compiled.paths = Module._nodeModulePaths(path.dirname(filename));
  compiled._compile(result.output.find(item => item.type === 'chunk').code, filename);
  TikTokPage = compiled.exports.TikTokPage;
  const entry = path.join(repo, 'TOOL-LOCAL/Web/src/tiktok-browser-fixture.ts');
  const interactive = await build({ input: entry, write: false,
    output: { format: 'iife', intro: 'var process = { env: { NODE_ENV: "production" } };' },
    plugins: [{ name: 'tiktok-fixture',
      resolveId: id => id === entry ? entry : null,
      load: id => id === entry ? `
        import { createElement, useState } from 'react';
        import { createRoot } from 'react-dom/client';
        import { TikTokPage } from './features/tiktok/TikTokPage';
        import { useTikTokModule } from './features/tiktok/useTikTokModule';
        function Fixture() {
          const module = useTikTokModule(true);
          const [visible, setVisible] = useState(false);
          window.tiktokModule = module;
          window.setTikTokVisible = setVisible;
          const content = visible ? createElement(TikTokPage, { module }) : null;
          return window.tiktokUseShell ? createElement('div', { className: 'app-shell' },
            createElement('aside', { className: 'sidebar' }, 'VideoMaker'),
            createElement('main', { className: 'app-main' },
              createElement('header', { className: 'topbar' }, createElement('div', { className: 'topbar-heading' },
                createElement('h1', null, 'Đăng TikTok'), createElement('p', null, 'Chia sẻ video lên tài khoản của bạn.'))), content)) : content;
        }
        createRoot(document.getElementById('root')).render(createElement(Fixture));
      ` : null
    }]
  });
  interactiveBundle = interactive.output.find(item => item.type === 'chunk').code;
  browser = await chromium.launch({ headless: true });
});
after(async () => { await browser?.close(); });

test('selected video loads and seeks under the real CSP, with recovery after a preview failure', async t => {
  const videoBytes = execFileSync(path.join(repo, 'third_party/ffmpeg/win-x64/ffmpeg.exe'), [
    '-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=640x360:rate=30',
    '-t', '2', '-an', '-c:v', 'libx264', '-threads', '1', '-pix_fmt', 'yuv420p',
    '-movflags', 'frag_keyframe+empty_moov', '-f', 'mp4', 'pipe:1'
  ], { windowsHide: true, timeout: 30000 });
  const html = fs.readFileSync(path.join(repo, 'TOOL-LOCAL/Web/index.html'), 'utf8')
    .replace('/src/main.tsx', '/fixture.js');
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  t.after(() => context.close());
  await context.addInitScript(() => {
    window.sent = [];
    window.violations = [];
    document.addEventListener('securitypolicyviolation', event => window.violations.push(event.blockedURI));
    const listeners = new Set();
    window.chrome ??= {};
    window.chrome.webview = {
      postMessage: message => window.sent.push(JSON.parse(message)),
      addEventListener: (_, listener) => listeners.add(listener),
      removeEventListener: (_, listener) => listeners.delete(listener)
    };
    window.reply = message => {
      // Match the native bridge: every reply carries the originating request and account.
      const kinds = { 'tiktok.state': 'tiktok.state.get', 'tiktok.creator': 'tiktok.creator.get',
        'tiktok.media.selected': 'tiktok.media.select', 'tiktok.operation.cancelled': 'tiktok.operation.cancel' };
      if (!message.requestId) {
        if (message.type === 'tiktok.media.selected') window.tiktokModule.selectMedia();
        if (message.type === 'tiktok.state' && window.tiktokModule.state.feature.configured) window.tiktokModule.refresh();
        const kind = kinds[message.type] || (message.type.startsWith('tiktok.upload.') ? 'tiktok.publish.start' : null);
        message.requestId = window.sent.filter(m => m.type === kind).at(-1)?.requestId;
      }
      if (message.payload && ['tiktok.creator', 'tiktok.publish.initialized', 'tiktok.upload.progress', 'tiktok.upload.completed'].includes(message.type))
        message.payload.connectionId ??= window.sent.find(m => m.requestId === message.requestId)?.payload?.connectionId;
      if (message.payload?.activePublish) message.payload.activePublish.connectionId ??= message.payload.connection?.connectionId;
      listeners.forEach(listener => listener({ data: message }));
      if (message.type === 'tiktok.operation.cancelled') {
        const original = window.sent.filter(m => m.type === 'tiktok.publish.start').at(-1);
        listeners.forEach(listener => listener({ data: { type: 'tiktok.error', requestId: original.requestId,
          error: { code: 'tiktok_operation_cancelled', message: 'Đã hủy thao tác.' } } }));
      }
    };
  });
  const page = await context.newPage();
  let failPreview = false;
  let rangeRequests = 0;
  let externalRequests = 0;
  await page.route('**/*', async route => {
    const url = new URL(route.request().url());
    if (url.href === 'https://app.local/') return route.fulfill({ contentType: 'text/html', body: html });
    if (url.href === 'https://app.local/fixture.js') return route.fulfill({ contentType: 'text/javascript', body: interactiveBundle });
    if (url.hostname === 'tiktok-media.app.local') {
      if (failPreview) return route.fulfill({ status: 409, body: '' });
      const range = route.request().headers().range?.match(/^bytes=(\d+)-(\d*)$/);
      if (range) rangeRequests++;
      const start = range ? Number(range[1]) : 0;
      const end = range?.[2] ? Math.min(Number(range[2]), videoBytes.length - 1) : videoBytes.length - 1;
      return route.fulfill({ status: range ? 206 : 200, body: videoBytes.subarray(start, end + 1), headers: {
        'Content-Type': 'video/mp4', 'Accept-Ranges': 'bytes', 'Cache-Control': 'private, no-store',
        'Access-Control-Allow-Origin': 'https://app.local', 'Cross-Origin-Resource-Policy': 'same-site',
        'X-Content-Type-Options': 'nosniff',
        ...(range ? { 'Content-Range': `bytes ${start}-${end}/${videoBytes.length}` } : {})
      } });
    }
    externalRequests++;
    return route.abort();
  });
  await page.goto('https://app.local/');
  await page.addStyleTag({ content: fs.readFileSync(path.join(repo, 'TOOL-LOCAL/Web/src/styles.css'), 'utf8') });
  await page.waitForFunction(() => window.sent.some(message => message.type === 'tiktok.state.get'));
  await page.evaluate(() => {
    window.reply({ type: 'tiktok.state', payload: { enabled: true, configured: true,
      connection: { connectionId: 'test-account', creatorNickname: 'Test Creator' } } });
    window.setTikTokVisible(true);
  });
  const select = id => page.evaluate(id => window.reply({ type: 'tiktok.media.selected', payload: {
    mediaId: id, fileName: 'preview-test.mp4', mimeType: 'video/mp4', sizeBytes: 100000,
    durationSeconds: 2, width: 640, height: 360, framesPerSecond: 30, videoCodec: 'h264',
    previewUrl: `https://tiktok-media.app.local/video/${id}`
  } }), id);
  await select('first-video');
  await page.waitForFunction(() => document.querySelector('video')?.readyState >= 2);
  assert.ok(await page.locator('video').evaluate(video => video.videoWidth === 640 && video.duration >= 2));
  await page.locator('video').evaluate(video => { video.currentTime = 1; });
  await page.waitForFunction(() => !document.querySelector('video').seeking && document.querySelector('video').currentTime >= 1);
  await page.locator('video').evaluate(video => { video.muted = true; return video.play(); });
  await page.waitForFunction(() => document.querySelector('video').currentTime > 1.2);
  await page.locator('video').evaluate(video => video.pause());
  assert.ok(rangeRequests > 0, 'browser reads the video through byte ranges');
  assert.deepEqual(await page.evaluate(() => window.violations), [], 'real app CSP allows the internal preview');
  for (const width of [1440, 375]) {
    await page.setViewportSize({ width, height: 1000 });
    const box = await page.locator('.tiktok-preview-wrap').boundingBox();
    assert.ok(Math.abs(box.width / box.height - 16 / 9) < 0.02, 'preview preserves landscape aspect ratio');
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR) {
      fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
      await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `tiktok-preview-${width}.png`), fullPage: true });
    }
  }
  failPreview = true;
  await select('failed-video');
  await page.locator('.tiktok-preview-error').waitFor();
  assert.match(await page.textContent('.tiktok-preview-error'), /Chưa thể phát video xem trước/);
  failPreview = false;
  await page.getByRole('button', { name: 'Tải lại video' }).click();
  await page.waitForFunction(() => document.querySelector('video')?.readyState >= 2);
  assert.equal(await page.locator('.tiktok-preview-error').count(), 0);
  await select('another-video');
  await page.waitForFunction(() => document.querySelector('video')?.readyState >= 2);
  // Adding the internal host must not enable arbitrary remote media.
  await page.evaluate(() => {
    const video = document.createElement('video');
    video.src = 'https://untrusted.example/blocked.mp4';
    video.preload = 'auto';
    document.body.append(video);
  });
  await page.waitForFunction(() => window.violations.includes('https://untrusted.example/blocked.mp4'));
  assert.equal(externalRequests, 0, 'untrusted media is blocked before any outbound request');
});

test('creator reads are deduplicated on page entry and can retry after success or error', async t => {
  const context = await browser.newContext();
  t.after(() => context.close());
  const page = await context.newPage();
  await page.route('**/*', route => route.request().url() === 'https://videomaker.test/'
    ? route.fulfill({ contentType: 'text/html', body: '<div id="root"></div>' }) : route.abort());
  await page.goto('https://videomaker.test/');
  await page.evaluate(() => {
    window.sent = [];
    const listeners = new Set();
    window.chrome ??= {};
    window.chrome.webview = {
      postMessage: message => window.sent.push(JSON.parse(message)),
      addEventListener: (_, listener) => listeners.add(listener),
      removeEventListener: (_, listener) => listeners.delete(listener)
    };
    window.reply = message => {
      // Match the native bridge: every reply carries the originating request and account.
      const kinds = { 'tiktok.state': 'tiktok.state.get', 'tiktok.creator': 'tiktok.creator.get',
        'tiktok.media.selected': 'tiktok.media.select', 'tiktok.operation.cancelled': 'tiktok.operation.cancel' };
      if (!message.requestId) {
        if (message.type === 'tiktok.media.selected') window.tiktokModule.selectMedia();
        if (message.type === 'tiktok.state' && window.tiktokModule.state.feature.configured) window.tiktokModule.refresh();
        const kind = kinds[message.type] || (message.type.startsWith('tiktok.upload.') ? 'tiktok.publish.start' : null);
        message.requestId = window.sent.filter(m => m.type === kind).at(-1)?.requestId;
      }
      if (message.payload && ['tiktok.creator', 'tiktok.publish.initialized', 'tiktok.upload.progress', 'tiktok.upload.completed'].includes(message.type))
        message.payload.connectionId ??= window.sent.find(m => m.requestId === message.requestId)?.payload?.connectionId;
      if (message.payload?.activePublish) message.payload.activePublish.connectionId ??= message.payload.connection?.connectionId;
      listeners.forEach(listener => listener({ data: message }));
      if (message.type === 'tiktok.operation.cancelled') {
        const original = window.sent.filter(m => m.type === 'tiktok.publish.start').at(-1);
        listeners.forEach(listener => listener({ data: { type: 'tiktok.error', requestId: original.requestId,
          error: { code: 'tiktok_operation_cancelled', message: 'Đã hủy thao tác.' } } }));
      }
    };
  });
  await page.addScriptTag({ content: interactiveBundle });
  await page.waitForFunction(() => window.sent.some(message => message.type === 'tiktok.state.get'));
  await page.evaluate(() => window.reply({ type: 'tiktok.state', payload: {
    enabled: true, configured: true, connection: { connectionId: 'test-account', creatorNickname: 'Test Creator' }
  } }));
  await page.waitForFunction(() => window.tiktokModule.state.feature.connection);
  await page.evaluate(() => window.setTikTokVisible(true));
  await page.waitForFunction(() => window.sent.some(message => message.type === 'tiktok.creator.get'));
  const count = () => page.evaluate(() => window.sent.filter(message => message.type === 'tiktok.creator.get').length);
  await page.evaluate(() => { window.tiktokModule.refresh(); window.tiktokModule.refreshCreator(); });
  assert.equal(await count(), 1, 'page mount and repeated refresh share the pending creator read');
  await page.evaluate(() => {
    const request = window.sent.find(message => message.type === 'tiktok.creator.get');
    window.reply({ type: 'tiktok.creator', requestId: request.requestId, payload: {
      creatorUsername: 'test_creator', creatorNickname: 'Test Creator', privacyLevelOptions: ['SELF_ONLY'],
      commentDisabled: false, duetDisabled: false, stitchDisabled: false, maximumVideoDurationSeconds: 600
    } });
  });
  await page.waitForFunction(() => window.tiktokModule.state.creator);
  await page.evaluate(() => window.tiktokModule.refreshCreator());
  assert.equal(await count(), 2, 'successful read allows a new refresh');
  await page.evaluate(() => {
    window.reply({ type: 'tiktok.error', requestId: 'unrelated-operation', error: { message: 'Other request' } });
    window.tiktokModule.refreshCreator();
  });
  assert.equal(await count(), 2, 'unrelated replies do not release an in-flight read');
  await page.evaluate(() => {
    const request = window.sent.filter(message => message.type === 'tiktok.creator.get').at(-1);
    window.reply({ type: 'tiktok.error', requestId: request.requestId, error: { message: 'Please try again' } });
  });
  await page.waitForFunction(() => window.tiktokModule.state.error === 'Please try again');
  assert.match(await page.textContent('.tiktok-error'), /Please try again/);
  await page.evaluate(() => window.tiktokModule.refresh());
  assert.equal(await count(), 3, 'failed read allows retry instead of locking the UI');
});

test('account identity stays readable beside disconnect and stacks on narrow screens', async t => {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  t.after(() => context.close());
  const page = await context.newPage();
  await page.route('**/*', route => route.abort());
  const noop = () => {};
  const html = renderToStaticMarkup(createElement(TikTokPage, { module: {
    state: { selectedConnectionId: 'test-account', jobs: [], history: null, historyLoading: false, feature: { enabled: true, configured: true, connection: {
      connectionId: 'test-account', creatorNickname: 'Đức lập trình', creatorUsername: 'test_creator'
    } }, creator: { creatorUsername: 'test_creator', creatorNickname: 'Đức lập trình', privacyLevelOptions: ['SELF_ONLY'],
      commentDisabled: false, duetDisabled: false, stitchDisabled: false, maximumVideoDurationSeconds: 600,
      publishingIssue: { code: 'tiktok_private_test_account_required', message: 'Synthetic account requirement' }
    }, media: null, upload: null, publish: null, loading: false, busy: false, uploadCompleted: false, error: null },
    refresh: noop, refreshCreator: noop, disconnect: noop, clearError: noop
  } }));
  await page.setContent(html);
  await page.addStyleTag({ content: fs.readFileSync(path.join(repo, 'TOOL-LOCAL/Web/src/styles.css'), 'utf8') });
  assert.equal(await page.locator('.tiktok-error').count(), 0);
  assert.match(await page.textContent('.tiktok-readiness'), /Tài khoản riêng tư/);
  assert.equal(await page.locator('.tiktok-readiness button').isEnabled(), true);
  assert.equal(await page.locator('.tiktok-submit-row .start-button').isDisabled(), true);
  for (const width of [1440, 768, 375]) {
    await page.setViewportSize({ width, height: 1000 });
    const dimensions = await page.evaluate(() => {
      const box = selector => document.querySelector(selector).getBoundingClientRect().toJSON();
      return { details: box('.tiktok-account-choice'), name: box('.tiktok-account-choice > span'),
        avatar: box('.tiktok-account-avatar'), button: box('.tiktok-account-bar button'),
        overflow: document.documentElement.scrollWidth > window.innerWidth };
    });
    assert.ok(dimensions.details.width >= 180, `identity has room at ${width}px`);
    assert.ok(dimensions.name.height < 32, 'short creator name fits on one line');
    assert.ok(dimensions.avatar.width >= 42, 'avatar does not shrink');
    assert.ok(dimensions.button.height >= 44, 'disconnect remains a usable target');
    assert.equal(dimensions.overflow, false, `page does not overflow at ${width}px`);
    if (width > 650) assert.ok(dimensions.button.width < 220, 'desktop disconnect does not consume the row');
    else assert.ok(dimensions.button.y >= dimensions.details.bottom, 'mobile disconnect follows identity');
    if (process.env.VIDEOMAKER_SCREENSHOT_DIR && width !== 768) {
      fs.mkdirSync(process.env.VIDEOMAKER_SCREENSHOT_DIR, { recursive: true });
      await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, `tiktok-account-${width}.png`), fullPage: true });
    }
  }
  await page.locator('.tiktok-account-choice > span').evaluate(element => { element.textContent = 'Tên tài khoản TikTok rất dài '.repeat(8); });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth), false);
});

test('publish dialog follows real bridge events, traps focus and distinguishes upload from publication', async t => {
  const { page, reply, status } = await openPublishingFixture(t);
  const screenshots = process.env.VIDEOMAKER_SCREENSHOT_DIR;
  for (const viewport of [{ width: 1440, height: 900 }, { width: 1366, height: 768 }, { width: 1280, height: 720 }]) {
    await page.setViewportSize(viewport);
    const layout = await page.evaluate(() => {
      const box = selector => document.querySelector(selector).getBoundingClientRect().toJSON();
      const main = document.querySelector('.app-main');
      return { account: box('.tiktok-account-bar'), submit: box('.tiktok-submit-row'),
        video: box('.tiktok-preview-wrap'), form: box('.tiktok-form-card'),
        overflow: main.scrollWidth > main.clientWidth + 1 };
    });
    assert.equal(layout.overflow, false, `complete desktop composer fits ${viewport.width}x${viewport.height}`);
    assert.ok(layout.submit.bottom <= viewport.height, 'publish action is visible without scrolling');
    assert.ok(layout.account.height <= 76, 'account uses a compact row');
    assert.ok(layout.video.right < layout.form.x, 'preview and form remain side by side');
    if (screenshots) await page.screenshot({ path: path.join(screenshots, `tiktok-composer-${viewport.width}.png`) });
  }
  // Portrait videos also fit the same viewport rather than pushing the form down.
  await reply('tiktok.media.selected', { ...fixtureMedia, mediaId: 'portrait', width: 360, height: 640 });
  assert.ok(await page.locator('.tiktok-preview-wrap').evaluate(el => Math.abs(el.clientWidth / el.clientHeight - 9 / 16) < .02));
  assert.ok(await page.locator('.tiktok-submit-row').evaluate(el => el.getBoundingClientRect().bottom <= innerHeight));
  await reply('tiktok.media.selected', fixtureMedia);
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.getByRole('checkbox', { name: /Nội dung này quảng bá/ }).check();
  await page.getByRole('checkbox', { name: 'Thương hiệu của bạn', exact: true }).check();
  assert.ok(await page.locator('.tiktok-submit-row').evaluate(el => el.getBoundingClientRect().bottom <= innerHeight), 'expanded disclosure keeps the publish action visible; history may scroll');
  await page.getByRole('checkbox', { name: /Nội dung này quảng bá/ }).uncheck();
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.getByRole('textbox', { name: 'Caption' }).fill('Video kiểm tra giao diện');
  await page.getByRole('combobox', { name: 'Ai có thể xem video?' }).selectOption('SELF_ONLY');
  await page.getByRole('checkbox', { name: 'Nội dung do AI tạo' }).check();
  await page.getByRole('checkbox', { name: /Bằng việc đăng/ }).check();
  await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('heading', { name: 'Đang chuẩn bị bài đăng' }).waitFor();
  assert.equal(await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.publish.start').length), 1);
  for (let index = 0; index < 8; index++) {
    await page.keyboard.press('Tab');
    assert.equal(await dialog.evaluate(el => el.contains(document.activeElement)), true, 'keyboard focus stays inside the overlay');
  }
  const requestId = await page.evaluate(() => window.sent.find(m => m.type === 'tiktok.publish.start').requestId);
  await reply('tiktok.publish.initialized', { publishJobId: 'job-test' }, requestId);
  await reply('tiktok.upload.progress', { publishJobId: 'job-test', percent: 45, uploadedBytes: 45000, totalBytes: 100000, completedChunks: 0, totalChunks: 1 });
  await page.waitForFunction(() => document.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow') === '45');
  const box = await dialog.boundingBox();
  assert.ok(Math.abs(box.x + box.width / 2 - 640) <= 1 && Math.abs(box.y + box.height / 2 - 360) <= 1, 'dialog is centered in the viewport');
  assert.match(await dialog.evaluate(el => getComputedStyle(el, '::backdrop').backgroundColor), /0\.25/);
  if (screenshots) await page.screenshot({ path: path.join(screenshots, 'tiktok-upload-dialog.png') });
  await reply('tiktok.upload.progress', { publishJobId: 'job-test', percent: 100, uploadedBytes: 100000, totalBytes: 100000, completedChunks: 1, totalChunks: 1 });
  await dialog.getByText('Đã tải đủ dữ liệu, đang chờ TikTok xác nhận.').waitFor();
  assert.equal(await dialog.getByRole('heading', { name: 'Đã đăng thành công' }).count(), 0, '100% upload does not claim publication');
  await reply('tiktok.upload.completed', { publishJobId: 'job-test' });
  await status('PROCESSING_DOWNLOAD');
  await dialog.getByRole('heading', { name: 'TikTok đang xử lý video' }).waitFor();
  await page.evaluate(() => {
    window.tiktokModule.refresh();
    const requestId = window.sent.filter(m => m.type === 'tiktok.state.get').at(-1).requestId;
    window.reply({ type: 'tiktok.error', requestId, error: { code: 'test_network', message: 'Chưa lấy được trạng thái mới.' } });
  });
  await dialog.getByRole('alert').waitFor();
  await status('PROCESSING_DOWNLOAD');
  await page.waitForFunction(() => !document.querySelector('.tiktok-dialog-warning'));
  assert.equal(await dialog.getByRole('button', { name: 'Hủy tải lên' }).count(), 0, 'cannot promise cancellation after TikTok receives the video');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('dialog')?.open);
  assert.equal(await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).isDisabled(), true);
  await status('PROCESSING_DOWNLOAD');
  assert.equal(await dialog.count(), 0, 'polling does not reopen a minimized dialog');
  await page.getByRole('button', { name: 'Xem tiến trình' }).click();
  await status('PUBLISH_COMPLETE', true);
  await dialog.getByRole('heading', { name: 'Đã đăng thành công' }).waitFor();
  if (screenshots) await page.screenshot({ path: path.join(screenshots, 'tiktok-success-dialog.png') });
  await dialog.getByRole('button', { name: 'Đóng', exact: true }).click();
  assert.equal(await page.locator('.tiktok-error').count(), 0, 'successful polling clears a temporary connection warning');
  assert.equal(await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).isEnabled(), true);
  assert.equal(await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.publish.start').length), 1, 'viewing or dismissing progress never submits another video');

  // A fresh attempt must not show the previous success/progress while preparing.
  await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).click();
  await dialog.getByRole('heading', { name: 'Đang chuẩn bị bài đăng' }).waitFor();
  assert.equal(await dialog.getByRole('progressbar').count(), 0);
  const retryId = await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.publish.start').at(-1).requestId);
  await page.evaluate(requestId => window.reply({ type: 'tiktok.error', requestId,
    error: { code: 'test_validation', message: 'Hãy chọn lại quyền xem bài đăng.' } }), retryId);
  await dialog.getByRole('heading', { name: 'Chưa thể hoàn tất bài đăng' }).waitFor();
  assert.match(await dialog.textContent(), /Hãy chọn lại quyền xem bài đăng/);
  assert.equal(await dialog.locator('.spin').count(), 0, 'validation errors end the loading indicator');
  await dialog.getByRole('button', { name: 'Đóng và kiểm tra lại' }).click();
  await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).click();
  await dialog.getByRole('button', { name: 'Hủy tải lên' }).click();
  assert.ok(await page.evaluate(() => window.sent.some(m => m.type === 'tiktok.operation.cancel')));
  await reply('tiktok.operation.cancelled');
  await dialog.getByRole('heading', { name: 'Đã dừng thao tác tải lên' }).waitFor();
  assert.equal(await dialog.locator('.spin').count(), 0);
  await dialog.getByRole('button', { name: 'Đóng', exact: true }).click();

  await page.setViewportSize({ width: 375, height: 812 });
  await page.getByRole('button', { name: 'Xem tiến trình' }).click();
  const mobileBox = await dialog.boundingBox();
  assert.ok(mobileBox.x >= 0 && mobileBox.x + mobileBox.width <= 375 && mobileBox.y + mobileBox.height <= 812);
  await dialog.getByRole('button', { name: 'Đóng', exact: true }).click();
  assert.equal(await page.locator('.app-main').evaluate(el => el.scrollWidth > el.clientWidth), false);
  await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).scrollIntoViewIfNeeded();
  assert.ok(await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).isVisible(), 'mobile form remains reachable by scrolling');
});

test('restored jobs show processing and provider rejection inside the dialog', async t => {
  const { page, reply, status } = await openPublishingFixture(t);
  await reply('tiktok.state', { enabled: true, configured: true, connection: fixtureConnection,
    activePublish: { publishJobId: 'job-test', status: 'PROCESSING_DOWNLOAD', isTerminal: false, uploadedBytes: 100000, publicPostIds: [] } });
  await page.locator('.tiktok-running-jobs button').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('heading', { name: 'TikTok đang xử lý video' }).waitFor();
  await status('FAILED', true, 'spam_risk_text');
  await dialog.getByRole('heading', { name: 'Chưa thể hoàn tất bài đăng' }).waitFor();
  assert.equal(await dialog.locator('.spin').count(), 0);
  assert.ok((await dialog.locator('#tiktokPublishDetail').textContent()).length > 20);
  await dialog.getByRole('button', { name: 'Đóng và kiểm tra lại' }).click();
  assert.equal(await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.publish.start').length), 0, 'recovery never resubmits');
});

test('creator policy changes during initialization end loading and show the account setup guidance', async t => {
  const { page, reply } = await openPublishingFixture(t);
  await page.getByRole('combobox', { name: 'Ai có thể xem video?' }).selectOption('SELF_ONLY');
  await page.getByRole('checkbox', { name: /Bằng việc đăng/ }).check();
  await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).click();
  const requestId = await page.evaluate(() => window.sent.find(m => m.type === 'tiktok.publish.start').requestId);
  await reply('tiktok.creator', { creatorUsername: 'test_creator', creatorNickname: 'Dân lập trình',
    privacyLevelOptions: ['SELF_ONLY'], commentDisabled: false, duetDisabled: false, stitchDisabled: false,
    maximumVideoDurationSeconds: 600, publishingIssue: {
      code: 'tiktok_private_test_account_required', message: 'Hãy chuyển tài khoản TikTok sang riêng tư để đăng thử.'
    } }, requestId);
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('heading', { name: 'Chưa thể hoàn tất bài đăng' }).waitFor();
  assert.match(await dialog.textContent(), /Hãy chuyển tài khoản TikTok sang riêng tư/);
  assert.equal(await dialog.locator('.spin').count(), 0);
  await dialog.getByRole('button', { name: 'Đóng và kiểm tra lại' }).click();
  await page.getByRole('heading', { name: 'Cần chuẩn bị tài khoản trước khi đăng' }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).isDisabled(), true);
  assert.equal(await page.evaluate(() => window.tiktokModule.state.publish), null);
});

test('rapid A B A switching rejects stale replies, resets consent, and keeps concurrent jobs isolated', async t => {
  const { page, reply } = await openPublishingFixture(t);
  const accountB = { connectionId: 'account-b', creatorUsername: 'creator_b', creatorNickname: 'Creator B', status: 'Connected' };
  await reply('tiktok.state', { enabled: true, configured: true, multiAccountEnabled: true, connections: [fixtureConnection, accountB] });
  await page.getByRole('combobox', { name: 'Ai có thể xem video?' }).selectOption('SELF_ONLY');
  await page.getByRole('checkbox', { name: /Bằng việc đăng/ }).check();
  await page.evaluate(() => window.tiktokModule.refreshCreator());
  const oldA = await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.creator.get').at(-1).requestId);
  await page.getByRole('combobox', { name: 'Tài khoản nhận bài đăng' }).selectOption('account-b');
  const requestB = await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.creator.get').at(-1).requestId);
  await page.getByRole('combobox', { name: 'Tài khoản nhận bài đăng' }).selectOption('test-account');
  const latestA = await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.creator.get').at(-1).requestId);
  assert.notEqual(oldA, latestA);
  await reply('tiktok.error', undefined, oldA); // Old error cannot overwrite a current form.
  await reply('tiktok.creator', { connectionId: 'account-b', creatorUsername: 'stale_b' }, requestB);
  await reply('tiktok.creator', { connectionId: 'test-account', creatorUsername: 'stale_a' }, oldA);
  assert.equal(await page.evaluate(() => window.tiktokModule.state.creator), null);
  assert.equal(await page.evaluate(() => window.tiktokModule.state.error), null);
  const creator = { connectionId: 'test-account', creatorUsername: 'test_creator', creatorNickname: 'Dân lập trình',
    privacyLevelOptions: ['SELF_ONLY'], commentDisabled: false, duetDisabled: false, stitchDisabled: false, maximumVideoDurationSeconds: 600 };
  await reply('tiktok.creator', creator, latestA);
  assert.equal(await page.getByRole('combobox', { name: 'Ai có thể xem video?' }).inputValue(), '');
  assert.equal(await page.getByRole('checkbox', { name: /Bằng việc đăng/ }).isChecked(), false);
  await page.getByRole('combobox', { name: 'Ai có thể xem video?' }).selectOption('SELF_ONLY');
  await page.getByRole('checkbox', { name: /Bằng việc đăng/ }).check();
  await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).click();
  const publishA = await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.publish.start').at(-1));
  assert.equal(publishA.payload.connectionId, 'test-account');
  assert.notEqual(publishA.payload.clientRequestId, publishA.payload.mediaId);
  await reply('tiktok.publish.initialized', { publishJobId: 'job-a', connectionId: 'test-account' }, publishA.requestId);
  assert.equal(await page.getByRole('combobox', { name: 'Tài khoản nhận bài đăng' }).isDisabled(), true);
  await reply('tiktok.upload.completed', { publishJobId: 'job-a', connectionId: 'test-account' }, publishA.requestId);
  await page.getByRole('dialog').getByRole('button', { name: 'Thu nhỏ', exact: true }).click();
  await page.getByRole('combobox', { name: 'Tài khoản nhận bài đăng' }).selectOption('account-b');
  await reply('tiktok.creator', { ...creator, connectionId: 'account-b', creatorUsername: 'creator_b' });
  await page.getByRole('combobox', { name: 'Ai có thể xem video?' }).selectOption('SELF_ONLY');
  await page.getByRole('checkbox', { name: /Bằng việc đăng/ }).check();
  await page.getByRole('button', { name: 'Đăng lên TikTok', exact: true }).click();
  const publishB = await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.publish.start').at(-1));
  assert.equal(publishB.payload.connectionId, 'account-b');
  assert.notEqual(publishB.payload.clientRequestId, publishA.payload.clientRequestId);
  await reply('tiktok.publish.initialized', { publishJobId: 'job-b', connectionId: 'account-b' }, publishB.requestId);
  await reply('tiktok.upload.completed', { publishJobId: 'job-b', connectionId: 'account-b' }, publishB.requestId);
  await page.getByRole('dialog').getByRole('button', { name: 'Thu nhỏ', exact: true }).click();
  const jobs = ['a', 'b'].map(letter => ({ publishJobId: 'job-' + letter,
    connectionId: letter === 'a' ? 'test-account' : 'account-b', creatorUsername: 'creator_' + letter,
    status: 'PROCESSING_DOWNLOAD', isTerminal: false, uploadedBytes: 100000, publicPostIds: [], updatedAtUtc: new Date().toISOString() }));
  await reply('tiktok.state', { enabled: true, configured: true, multiAccountEnabled: true, connections: [fixtureConnection, accountB], activePublishes: jobs });
  assert.equal(await page.locator('.tiktok-running-jobs button').count(), 2);
  await page.locator('.tiktok-running-jobs button').filter({ hasText: '@creator_a' }).click();
  assert.match(await page.getByRole('dialog').textContent(), /@creator_a/);
  assert.equal(await page.getByRole('dialog').getByRole('button', { name: 'Hủy tải lên' }).count(), 0);
  assert.equal(await page.getByRole('dialog').locator('.tiktok-dialog-summary strong').count(), 0, 'another job never shows the currently selected file');
  await page.getByRole('dialog').getByRole('button', { name: 'Thu nhỏ', exact: true }).click();
  await page.getByRole('combobox', { name: 'Tài khoản nhận bài đăng' }).selectOption('test-account');
  await page.getByRole('button', { name: 'Quản lý (2)', exact: true }).click();
  await page.getByRole('dialog').getByRole('textbox', { name: 'Tìm tài khoản TikTok' }).fill('creator_b');
  assert.equal(await page.locator('.tiktok-account-row').count(), 1);
  await page.getByRole('dialog').getByRole('button', { name: 'Ngắt', exact: true }).click();
  assert.match(await page.getByRole('dialog').textContent(), /Có 1 bài sẽ dừng theo dõi/);
  if (process.env.VIDEOMAKER_SCREENSHOT_DIR) await page.screenshot({ path: path.join(process.env.VIDEOMAKER_SCREENSHOT_DIR, 'tiktok-multi-account-manager.png') });
  await page.getByRole('dialog').getByRole('button', { name: 'Ngắt tài khoản này', exact: true }).click();
  const disconnect = await page.evaluate(() => window.sent.filter(m => m.type === 'tiktok.oauth.disconnect').at(-1));
  assert.equal(disconnect.payload.connectionId, 'account-b');
  await page.evaluate(requestId => window.reply({ type: 'tiktok.error', requestId,
    error: { code: 'synthetic_failure', message: 'Chưa ngắt được tài khoản B.' } }), disconnect.requestId);
  await page.getByRole('dialog').getByRole('alert').filter({ hasText: 'Chưa ngắt được tài khoản B.' }).waitFor();
  assert.equal(await page.evaluate(() => window.tiktokModule.state.busy), false);
});

const fixtureConnection = { connectionId: 'test-account', creatorNickname: 'Dân lập trình', creatorUsername: 'test_creator' };
const fixtureMedia = { mediaId: 'test-media', fileName: 'video-kiem-tra-giao-dien.mp4', mimeType: 'video/mp4',
  sizeBytes: 100000, durationSeconds: 2, width: 640, height: 360, framesPerSecond: 30, videoCodec: 'h264',
  previewUrl: 'https://tiktok-media.app.local/video/test' };
async function openPublishingFixture(t) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  t.after(() => context.close());
  const page = await context.newPage();
  const video = execFileSync(path.join(repo, 'third_party/ffmpeg/win-x64/ffmpeg.exe'), [
    '-hide_banner', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=640x360:rate=30',
    '-t', '2', '-an', '-c:v', 'libx264', '-threads', '1', '-pix_fmt', 'yuv420p',
    '-movflags', 'frag_keyframe+empty_moov', '-f', 'mp4', 'pipe:1'
  ], { windowsHide: true, timeout: 30000 });
  await page.route('**/*', route => {
    const url = route.request().url();
    if (url === 'https://videomaker.test/') return route.fulfill({ contentType: 'text/html', body: '<div id="root"></div>' });
    if (url === fixtureMedia.previewUrl) return route.fulfill({ contentType: 'video/mp4', body: video });
    return route.abort();
  });
  await page.goto('https://videomaker.test/');
  await page.evaluate(() => {
    window.tiktokUseShell = true;
    window.sent = [];
    const listeners = new Set();
    window.chrome ??= {};
    window.chrome.webview = { postMessage: message => window.sent.push(JSON.parse(message)),
      addEventListener: (_, listener) => listeners.add(listener), removeEventListener: (_, listener) => listeners.delete(listener) };
    window.reply = message => {
      // Match the native bridge: every reply carries the originating request and account.
      const kinds = { 'tiktok.state': 'tiktok.state.get', 'tiktok.creator': 'tiktok.creator.get',
        'tiktok.media.selected': 'tiktok.media.select', 'tiktok.operation.cancelled': 'tiktok.operation.cancel' };
      if (!message.requestId) {
        if (message.type === 'tiktok.media.selected') window.tiktokModule.selectMedia();
        if (message.type === 'tiktok.state' && window.tiktokModule.state.feature.configured) window.tiktokModule.refresh();
        const kind = kinds[message.type] || (message.type.startsWith('tiktok.upload.') ? 'tiktok.publish.start' : null);
        message.requestId = window.sent.filter(m => m.type === kind).at(-1)?.requestId;
      }
      if (message.payload && ['tiktok.creator', 'tiktok.publish.initialized', 'tiktok.upload.progress', 'tiktok.upload.completed'].includes(message.type))
        message.payload.connectionId ??= window.sent.find(m => m.requestId === message.requestId)?.payload?.connectionId;
      if (message.payload?.activePublish) message.payload.activePublish.connectionId ??= message.payload.connection?.connectionId;
      listeners.forEach(listener => listener({ data: message }));
      if (message.type === 'tiktok.operation.cancelled') {
        const original = window.sent.filter(m => m.type === 'tiktok.publish.start').at(-1);
        listeners.forEach(listener => listener({ data: { type: 'tiktok.error', requestId: original.requestId,
          error: { code: 'tiktok_operation_cancelled', message: 'Đã hủy thao tác.' } } }));
      }
    };
  });
  await page.addStyleTag({ content: fs.readFileSync(path.join(repo, 'TOOL-LOCAL/Web/src/styles.css'), 'utf8') });
  await page.addScriptTag({ content: interactiveBundle });
  await page.waitForFunction(() => window.setTikTokVisible);
  await page.evaluate(connection => {
    window.reply({ type: 'tiktok.state', payload: { enabled: true, configured: true, connection } });
    window.setTikTokVisible(true);
  }, fixtureConnection);
  await page.waitForFunction(() => window.sent.some(m => m.type === 'tiktok.creator.get'));
  const reply = (type, payload, requestId) => page.evaluate(async message => {
    window.reply(message);
    // Let React commit the host response and reset account consent before the next UI action.
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  }, { type, payload, requestId });
  await reply('tiktok.creator', { creatorUsername: 'test_creator', creatorNickname: 'Dân lập trình',
    privacyLevelOptions: ['SELF_ONLY', 'PUBLIC_TO_EVERYONE'], commentDisabled: false, duetDisabled: false, stitchDisabled: false,
    maximumVideoDurationSeconds: 600 });
  await reply('tiktok.media.selected', fixtureMedia);
  await page.waitForFunction(() => document.querySelector('video')?.readyState >= 2);
  const status = (status, isTerminal = false, failureReason) => reply('tiktok.state', {
    enabled: true, configured: true, connections: [fixtureConnection],
    recentPublishes: [{ connectionId: fixtureConnection.connectionId, publishJobId: 'job-test', status,
      isTerminal, failureReason, uploadedBytes: 100000, publicPostIds: [], updatedAtUtc: new Date().toISOString() }]
  });
  return { page, reply, status };
}
