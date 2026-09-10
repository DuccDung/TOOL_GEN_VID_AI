import { createRoot } from 'react-dom/client';
import '../../src/styles.css';
import type { ShortVideoLibraryAsset, ShortVideoDraft } from '../../src/types';

// Isolated browser fixture. It never calls the application's native bridge or a provider.
const people = ['Nhân vật chính', 'Minh', 'Lan', 'Quang', 'Ngọc'];
const clothes = ['Áo dài xanh', 'Váy trắng', 'Bộ thường', 'Vest đen', 'Áo dài đỏ', 'Cổ trang'];
const colors = ['#91b4c7', '#e6dece', '#61767a', '#293f58', '#bb4b58', '#dfa249'];
const image = (i: number, outfit: boolean) => 'data:image/svg+xml,' + encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="140" height="160" viewBox="0 0 140 160"><rect width="140" height="160" fill="#e7ecf2"/><path d="M45 60 25 73 14 115 35 123 41 99 39 158 104 158 101 99 108 122 129 113 116 72 94 59Z" fill="${colors[i % colors.length]}"/>${outfit ? '<path d="M57 60Q70 80 84 60" fill="none" stroke="#ffffff90" stroke-width="3"/>' : '<ellipse cx="69" cy="42" rx="25" ry="31" fill="#3a3433"/><ellipse cx="70" cy="43" rx="18" ry="23" fill="#dec1a9"/><path d="M48 35q23-38 43 3-16-7-25-16-4 12-18 13" fill="#3a3433"/>'}</svg>`);
const items: ShortVideoLibraryAsset[] = [...people.map((name, i) => ({ name, kind: 'Character' as const, i })), ...clothes.map((name, i) => ({ name, kind: 'Outfit' as const, i }))].map((entry, i) => ({ assetId: `asset-${i}`, version: 1, kind: entry.kind, name: entry.name, image: { sha256: String(i).padStart(64, 'a'), width: 140, height: 160, mimeType: 'image/png', sizeBytes: 4096 }, thumbnailUrl: image(entry.i, entry.kind === 'Outfit'), previewUrl: image(entry.i, entry.kind === 'Outfit'), createdAtUtc: '2026-09-11T00:00:00Z', updatedAtUtc: '2026-09-11T00:00:00Z' }));
const draft: ShortVideoDraft = { content: 'Nhân vật bước chậm giữa phố cổ Hội An lúc bình minh, máy quay lùi mượt, đèn lồng lay nhẹ trong gió, ánh sáng tự nhiên.', aspectRatio: '9:16', durationSeconds: 8, audioEnabled: true, character: items[0], outfit: items[5], background: '', motion: '', serverRevision: 0 };
const listeners = new Set<(event: MessageEvent) => void>(); let revision = 1; const calls: string[] = [];
window.chrome = { webview: {
  postMessage: text => { const message = JSON.parse(text); calls.push(message.type); setTimeout(() => {
    const payload = message.type === 'short-library.get' ? { items, draft: { revision, draft } } : message.type === 'short-library.save-draft' ? { revision: ++revision, draft: message.payload.draft } : null;
    for (const listener of listeners) listener(new MessageEvent('message', { data: { type: 'short-library.result', requestId: message.requestId, payload } }));
  }, 20); }, addEventListener: (_, listener) => { listeners.add(listener); }, removeEventListener: (_, listener) => { listeners.delete(listener); }
} };
// bridge.ts captures WebView at module evaluation; import the composer after the fixture is registered.
const start = async () => {
  const { OutfitShortVideo: Composer } = await import('../../src/features/shortVideo/OutfitShortVideo');
  createRoot(document.getElementById('root')!).render(<Composer project={null} organizationId="org-fixture" enabled busy={false} onExport={() => { throw new Error('Export is disabled in the fixture.'); }} />);
};
void start();
document.body.style.overflow = 'auto';
const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
Object.assign(window, { run: async () => {
  for (let i = 0; i < 80 && document.querySelectorAll('.sv-asset').length < 11; i++) await wait(50);
  await wait(100);
  const errors: string[] = [];
  if (document.documentElement.scrollWidth > window.innerWidth + 1) errors.push('Page overflows horizontally');
  if (document.querySelectorAll('.sv-library-strip').length !== 2) errors.push('Missing asset strips');
  const thumbnails = [...document.querySelectorAll('.sv-thumbnail')];
  if (thumbnails.length < 11) errors.push('Library did not load');
  if (thumbnails.some(el => el.getBoundingClientRect().height > 90)) errors.push('Oversized thumbnails');
  const grid = document.querySelector('.sv-composer-grid')!;
  const left = document.querySelector('.sv-input-panel')!.getBoundingClientRect(), right = document.querySelector('.sv-result-panel')!.getBoundingClientRect();
  if (grid.getBoundingClientRect().width >= 960 && right.left < left.right) errors.push('Columns do not align');
  if (grid.getBoundingClientRect().width < 960 && right.top < left.bottom) errors.push('Results do not stack on small windows');
  if (calls.some(type => /outfit\.|\.create$/.test(type))) errors.push('Unexpected gateway request');
  return { errors, width: window.innerWidth, columns: getComputedStyle(grid).gridTemplateColumns, thumbnails: thumbnails.length,
    strips: [...document.querySelectorAll('.sv-library-strip')].map(strip => ({ height: strip.getBoundingClientRect().height })), inputHeight: left.height,
    parts: [...document.querySelector('.sv-input-panel')!.children].map(e => ({ name: e.className, height: e.getBoundingClientRect().height, margin: getComputedStyle(e).margin })) };
}, openAdd: async () => { (document.querySelector('[aria-label="Chọn ảnh trang phục"]') as HTMLButtonElement).click(); await wait(100); return Boolean(document.querySelector('dialog[open]')); } });
