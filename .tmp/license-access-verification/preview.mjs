import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';

const root = process.cwd();
const output = path.join(root, '.tmp/license-access-verification');
const assets = path.join(root, 'TOOL-LOCAL/Web/dist/assets');
const names = fs.readdirSync(assets);
const script = names.find(name => name.endsWith('.js'));
const style = names.find(name => name.endsWith('.css'));
const license = { hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 1,
  activeDeviceCount: 1, offlineGraceHours: 0, serverTimeUtc: '2026-09-10T00:00:00Z', heartbeatIntervalSeconds: 300,
  leaseExpiresAtUtc: null, accessState: 'SessionLimit', accessReasonCode: 'concurrent_session_limit',
  accessMessage: 'Gói đã đạt số phiên chạy đồng thời tối đa.' };
const html = `<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="stylesheet" href="/${style}"><div id="root"></div><script>
const listeners = new Set(); window.chrome = window.chrome || {}; window.chrome.webview = {
  addEventListener: (_, callback) => listeners.add(callback), removeEventListener: (_, callback) => listeners.delete(callback),
  postMessage: raw => { const message = JSON.parse(raw); if (message.type === 'app.ready') setTimeout(() => {
    const data = { type: 'license.invalidated', payload: {message: ${JSON.stringify(license.accessMessage)}, license: ${JSON.stringify(license)}} };
    listeners.forEach(listener => listener({data}));
  }, 50); }
};</script><script type="module" src="/${script}"></script></html>`;
const server = http.createServer((req, res) => {
  if (req.url === '/') { res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(html); return; }
  const name = req.url.slice(1);
  if (name !== script && name !== style) { res.writeHead(404).end(); return; }
  res.setHeader('Content-Type', name === script ? 'application/javascript' : 'text/css');
  res.end(fs.readFileSync(path.join(assets, name)));
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
try {
  for (const [name, size] of [['desktop', '1100,800'], ['narrow', '600,720']]) {
    const browser = spawn(path.join(process.env['ProgramFiles(x86)'], 'Microsoft/Edge/Application/msedge.exe'),
      ['--headless', '--disable-gpu', '--no-first-run', '--no-default-browser-check', '--disable-extensions',
       '--disable-background-networking', `--window-size=${size}`, '--virtual-time-budget=3000',
       `--user-data-dir=${path.join(output, 'edge-' + name)}`, `--screenshot=${path.join(output, name + '.png')}`,
       `http://127.0.0.1:${server.address().port}/`], {windowsHide: true, stdio: 'ignore'});
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => { browser.kill(); reject(new Error('Preview timed out')); }, 45000);
      browser.once('error', error => { clearTimeout(timer); reject(error); });
      browser.once('exit', code => { clearTimeout(timer); code === 0 ? resolve() : reject(new Error(`Preview exited ${code}`)); });
    });
    console.log(path.join(output, name + '.png'));
  }
} finally { server.close(); }
