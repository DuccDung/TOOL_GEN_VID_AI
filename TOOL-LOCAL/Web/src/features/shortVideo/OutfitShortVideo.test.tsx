// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage, ProjectDashboard, ShortVideoDraft, ShortVideoLibraryAsset, ShortVideoLibraryState, ShortVideoOutfitView, ShortVideoQuote } from '../../types';
const host = vi.hoisted(() => ({ listener: null as ((message: HostMessage) => void) | null, sent: [] as HostMessage[], next: 0 }));
vi.mock('../../bridge', () => ({ postToHost: (type: string, payload: unknown) => { const requestId = `request-${++host.next}`; host.sent.push({ type, payload, requestId }); return requestId; }, subscribeToHost: (listener: (message: HostMessage) => void) => { host.listener = listener; return () => { host.listener = null; }; } }));
import { OutfitShortVideo } from './OutfitShortVideo';

let container: HTMLDivElement; let root: Root;
const project = { project: { projectId: 'project', organizationId: 'org', name: 'Outfit', topic: 'Studio', targetDurationSeconds: 8, aspectRatio: '9:16' }, scenes: [], render: { status: 'Pending' }, audioStrategy: 'SilentOutput' } as unknown as ProjectDashboard;
const info = { sha256: 'a'.repeat(64), mimeType: 'image/png', sizeBytes: 100, width: 720, height: 1280 };
const draft: ShortVideoDraft = { content: 'Studio', aspectRatio: '9:16', durationSeconds: 8, audioEnabled: false, character: null, outfit: null, background: 'Studio', motion: 'Walk', serverRevision: 2 };
function library(): ShortVideoLibraryState { return { items: [], draft: { revision: 1, draft: { ...draft } } }; }
function state(status?: string): ShortVideoOutfitView { return { state: { projectId: 'project', revision: 2, character: info, outfit: info, background: 'Studio', motion: 'Walk', enabled: true, composition: status ? { ...info, compositionId: 'composition', status, revision: 2, contentUrl: '/image' } : null }, characterPreview: '/character.png', outfitPreview: '/outfit.png', compositionPreview: status ? '/composition.png' : null }; }
function button(text: string) { const found = [...container.querySelectorAll('button')].find(x => x.textContent === text || x.getAttribute('aria-label') === text); if (!found) throw new Error(`Missing button: ${text}`); return found; }
async function reply(type: string, payload: unknown, requestId = host.sent.at(-1)!.requestId) { await act(async () => host.listener?.({ type, payload, requestId })); }
async function click(element: HTMLElement) { await act(async () => element.click()); }
async function mount(enabled = true, value: ProjectDashboard | null = project) { await act(async () => root.render(<OutfitShortVideo project={value} organizationId="org" busy={false} enabled={enabled} onExport={() => {}} />)); }
async function load(status?: string, value = library()) { await reply('short-library.result', value, host.sent.find(x => x.type === 'short-library.get')!.requestId); await reply('outfit.state', state(status), host.sent.find(x => x.type === 'outfit.get')!.requestId); }
async function input(field: HTMLInputElement | HTMLTextAreaElement, value: string) { await act(async () => { Object.getOwnPropertyDescriptor(field instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, 'value')!.set!.call(field, value); field.dispatchEvent(new Event('input', { bubbles: true })); }); }
beforeEach(() => { vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) { this.setAttribute('open', ''); }); HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) { this.removeAttribute('open'); }); host.sent = []; host.next = 0; container = document.createElement('div'); document.body.appendChild(container); root = createRoot(container); });
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

describe('outfit short-video composer', () => {
  it('recovers a completed video without asking for another paid generation', async () => {
    await mount(true, { ...project, lastErrorMessage: 'Video đã tạo nhưng chưa tải được về máy.', scenes: [{ sceneId: 'scene', status: 'Generated', videoRequestStatus: 'Completed', canResumeVideo: true }] } as ProjectDashboard);
    await load('Approved');
    expect(button('Video').getAttribute('aria-selected')).toBe('true');
    expect(container.textContent).toContain('đang chờ tải về máy');
    expect(container.textContent).toContain('Video đã tạo nhưng chưa tải được về máy.');
    await click(button('Tiếp tục / tải lại video đã gửi'));
    expect(host.sent.at(-1)).toMatchObject({ type: 'outfit.resume', payload: { projectId: 'project', organizationId: 'org' } });
    expect(host.sent.some(x => x.type === 'outfit.quote' || x.type === 'outfit.video')).toBe(false);
    await act(async () => host.listener?.({ type: 'operation.error', requestId: host.sent.at(-1)!.requestId, error: { code: 'provider_output_cache_invalid', message: 'Chưa tải được video.' } }));
    expect(button('Video').getAttribute('aria-selected')).toBe('true');
    expect(button('Tiếp tục / tải lại video đã gửi').disabled).toBe(false);
    expect(container.textContent).toContain('Chưa tải được video.');
  });
  it('opens an existing preview in the video tab after reopening a project', async () => {
    await mount(true, { ...project, scenes: [{ sceneId: 'scene', status: 'AudioReviewRequired', preview: { url: '/video.mp4' } }] } as ProjectDashboard);
    await load('Approved');
    expect(container.querySelector('video')?.getAttribute('src')).toBe('/video.mp4');
    expect(host.sent.some(x => x.type === 'outfit.resume' || x.type === 'outfit.video')).toBe(false);
  });
  it('waits for both scoped library and project state, ignoring dashboard responses', async () => {
    await mount();
    expect(host.sent.map(x => x.type)).toEqual(['short-library.get', 'outfit.get']);
    await reply('dashboard.state', {});
    expect(button('Chọn ảnh nhân vật').disabled).toBe(true);
    await load(); expect(button('Chọn ảnh nhân vật').disabled).toBe(false);
    expect(container.querySelectorAll('.sv-library-strip')).toHaveLength(2);
  });
  it('shows the library before project creation and recovers a native draft', async () => {
    await mount(true, null);
    expect(host.sent.map(x => x.type)).toEqual(['short-library.get']);
    const saved = library(); saved.draft.draft!.serverRevision = 0;
    await reply('short-library.result', saved);
    expect(container.querySelector('textarea')?.value).toBe('Studio');
    expect(button('Chọn ảnh trang phục').disabled).toBe(false);
    expect(host.sent.some(x => /compose|\.video|\.create/.test(x.type))).toBe(false);
  });
  it('requires separate cost confirmation using the exact quote and scope', async () => {
    await mount(); await load(); await click(button('Xem trước trang phục'));
    expect(host.sent.at(-1)).toMatchObject({ type: 'outfit.quote', payload: { kind: 'Image', revision: 2, organizationId: 'org', projectId: 'project' } });
    const quote: ShortVideoQuote = { quoteId: 'quote', kind: 'Image', estimatedCost: .42, currencyCode: 'USD', modelCode: 'gpt-image-2', resolution: '720x1280', nativeAudio: false, expiresAtUtc: new Date(Date.now() + 600000).toISOString(), revision: 2 };
    await reply('outfit.quote', quote);
    expect(host.sent.some(x => x.type === 'outfit.compose')).toBe(false);
    await click(button('Xác nhận chi phí và tạo ảnh'));
    expect(host.sent.at(-1)).toMatchObject({ type: 'outfit.compose', payload: { quoteId: 'quote', confirmed: true, projectId: 'project', organizationId: 'org', revision: 2 } });
  });
  it('requires image load and review, and does not enable video before approval', async () => {
    await mount(); await load('PendingReview');
    expect(button('Chờ duyệt ảnh mặc thử').disabled).toBe(true);
    expect(button('Duyệt ảnh này').disabled).toBe(true);
    await act(async () => container.querySelector<HTMLImageElement>('.outfit-composition')!.dispatchEvent(new Event('load')));
    await click(container.querySelector<HTMLInputElement>('input[type=checkbox]')!);
    expect(button('Duyệt ảnh này').disabled).toBe(false);
    await click(button('Duyệt ảnh này'));
    expect(host.sent.at(-1)).toMatchObject({ type: 'outfit.approve', payload: { compositionId: 'composition', revision: 2, confirmed: true } });
  });
  it('selects one outfit without losing the character and invalidates existing video actions', async () => {
    const value = library();
    const asset: ShortVideoLibraryAsset = { assetId: 'asset', version: 1, kind: 'Outfit', name: 'Áo dài xanh', image: { ...info, sha256: 'b'.repeat(64) }, thumbnailUrl: '/thumb', previewUrl: '/full', createdAtUtc: '', updatedAtUtc: '' }; value.items = [asset];
    await mount(); await load('Approved', value); expect(button('Tạo video 8 giây').disabled).toBe(false);
    await click(button('Chọn trang phục: Áo dài xanh'));
    expect(container.textContent).toContain('Đầu vào đã thay đổi');
    expect(container.querySelector('[alt="Ảnh nhân vật đang dùng"]')).not.toBeNull();
    expect(host.sent.some(x => x.type === 'outfit.video')).toBe(false);
    await click(button('Video')); expect(button('Tiếp tục / tải lại video đã gửi').disabled).toBe(true);
    await click(button('Xem trước trang phục')); expect(container.querySelector('dialog')?.textContent).toContain('Lưu thay đổi đầu vào');
  });
  it('uploads through a native token and cancellation retains the existing image', async () => {
    await mount(); await load(); await click(button('Chọn ảnh nhân vật'));
    await click([...container.querySelectorAll('dialog button')].find(x => x.textContent?.includes('Chọn ảnh nhân vật')) as HTMLElement);
    expect(host.sent.at(-1)?.type).toBe('short-library.pick'); expect(host.sent.at(-1)?.payload).not.toHaveProperty('path');
    const upload = { uploadId: 'upload', suggestedName: 'Nhân vật A', image: info, previewUrl: '/preview' }; await reply('short-library.result', upload);
    await click(button('Chọn ảnh khác')); await reply('short-library.result', null);
    expect(container.querySelector('dialog img')?.getAttribute('src')).toBe('/preview');
    await click(button('Lưu và chọn'));
    expect(host.sent.at(-1)).toMatchObject({ type: 'short-library.commit', payload: { uploadId: 'upload', name: 'Nhân vật A', kind: 'Character' } });
    expect(host.sent.some(x => x.type === 'outfit.compose')).toBe(false);
  });
  it('preserves separate background/motion and blocks overlong shared background', async () => {
    await mount(); await load();
    const fields = container.querySelectorAll('textarea'); expect(fields[1].value).toBe('Studio'); expect(fields[2].value).toBe('Walk');
    await input(fields[1], ''); await input(fields[0], 'x'.repeat(1501));
    expect(button('Xem trước trang phục').disabled).toBe(true); expect(container.textContent).toContain('bối cảnh riêng');
    await input(fields[1], 'Studio'); expect(button('Xem trước trang phục').disabled).toBe(false);
    expect(fields[2].value).toBe('Walk');
  });
  it('rejects responses for a different project', async () => {
    await mount(); await reply('short-library.result', library(), host.sent[0].requestId);
    const wrong = state(); wrong.state.projectId = 'foreign'; await reply('outfit.state', wrong);
    expect(button('Chọn ảnh nhân vật').disabled).toBe(true); expect(container.textContent).toContain('không khớp dự án');
  });
  it('does not access the library or gateway when the feature is disabled', async () => {
    await mount(false); expect(host.sent).toHaveLength(0); expect(button('Chọn ảnh nhân vật').disabled).toBe(true);
  });
  it('supports keyboard navigation of result tabs without creating a request', async () => {
    await mount(); await load();
    const tab = button('Ảnh mặc thử'); tab.focus();
    await act(async () => tab.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })));
    expect(button('Video').getAttribute('aria-selected')).toBe('true'); expect(document.activeElement).toBe(button('Video'));
    expect(host.sent.map(message => message.type)).toEqual(['short-library.get', 'outfit.get']);
  });
  it('closes the native modal when access becomes unavailable', async () => {
    await mount(); await load(); await click(button('Chọn ảnh nhân vật'));
    expect(container.querySelector('dialog[open]')).not.toBeNull();
    await mount(false); expect(container.querySelector('dialog')).toBeNull();
    expect(button('Chọn ảnh nhân vật').disabled).toBe(true);
  });
  it('opens the creation quote once after the new project loads without submitting AI', async () => {
    const quote: ShortVideoQuote = { quoteId: 'new-quote', kind: 'Image', estimatedCost: .42, currencyCode: 'USD', modelCode: 'gpt-image-2', resolution: '720x1280', nativeAudio: false, expiresAtUtc: new Date(Date.now() + 600000).toISOString(), revision: 2 };
    await act(async () => root.render(<OutfitShortVideo project={project} organizationId="org" busy={false} enabled onExport={() => {}} createdNotice={{ projectId: 'project', organizationId: 'org', quote }} />));
    expect(container.querySelector('dialog')).toBeNull();
    await load(); expect(button('Xác nhận chi phí và tạo ảnh')).toBeTruthy();
    expect(host.sent.some(x => x.type === 'outfit.compose')).toBe(false);
    await click(button('Đóng báo giá')); expect(container.querySelector('dialog')).toBeNull();
  });
});
