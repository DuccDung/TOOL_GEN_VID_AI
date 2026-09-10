// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GenerationProviderStatus, HostMessage, ProjectDashboard, SceneFirstFrameSummary } from '../../types';
import { TextShortVideo } from './TextShortVideo';

const host = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>(), sent: [] as HostMessage[], next: 0 }));
vi.mock('../../bridge', () => ({
  postToHost: (type: string, payload: unknown) => { const requestId = `request-${++host.next}`; host.sent.push({ type, payload, requestId }); return requestId; },
  subscribeToHost: (listener: (message: HostMessage) => void) => { host.listeners.add(listener); return () => host.listeners.delete(listener); }
}));
const project = { project: { projectId: 'project', organizationId: 'org', name: 'Studio', topic: 'Studio', aspectRatio: '9:16', targetDurationSeconds: 8 },
  videoProviderCode: 'fal', videoModelCode: 'fal-ai/veo3.1/image-to-video', workflowStructureType: 'DirectShortVideo',
  scenes: [{ sceneId: 'scene', canGenerate: true, status: 'PromptReady' }], render: { status: 'Pending' }, audioStrategy: 'ProviderNative' } as ProjectDashboard;
const frame = { sceneId: 'scene', sceneFirstFrameId: 'frame', status: 'Approved', isCurrent: true, previewUrl: '/frame.png', version: 1, rowVersion: 'row' } as SceneFirstFrameSummary;
const status = { videoReady: true, videoProviderCode: 'fal', videoModel: 'fal-ai/veo3.1/image-to-video', openAiImageReady: true } as GenerationProviderStatus;
let container: HTMLDivElement; let root: Root;
const create = vi.fn(), approveImage = vi.fn(), requestImage = vi.fn();
function button(label: string) { const result = [...container.querySelectorAll('button')].find(b => b.textContent === label); if (!result) throw new Error(`Missing ${label}`); return result; }
async function click(element: HTMLElement) { await act(async () => element.click()); }
async function reply(type: string, payload: unknown, error?: HostMessage['error']) { const requestId = host.sent.at(-1)!.requestId; await act(async () => { for (const listener of host.listeners) listener({ type, payload, requestId, error }); }); }
async function mount(selected: ProjectDashboard | null = project, frames = [frame]) {
  await act(async () => root.render(<TextShortVideo project={selected} organizationId="org" providerStatus={status} mediaTools={{ ready: true, message: '', checkedAtUtc: '' }} busy={false} outfitEnabled onCreateOutfit={vi.fn()}
    onCreate={create} frames={frames} onRequestImage={requestImage} onApproveImage={approveImage} onRejectImage={vi.fn()} onDownloadImage={vi.fn()} onApproveVideo={vi.fn()} onRender={vi.fn()} onExport={vi.fn()} />));
}
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) { this.setAttribute('open', ''); });
  HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) { this.removeAttribute('open'); });
  host.sent = []; host.next = 0; vi.clearAllMocks(); container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

describe('Veo short video', () => {
  it('resumes a completed video and shows the download error without another quote', async () => {
    await mount({ ...project, lastErrorMessage: 'Video đã tạo nhưng chưa tải được về máy.', scenes: [{ ...project.scenes[0], status: 'Generated', canResumeVideo: true, videoRequestStatus: 'Completed' }] });
    expect(button('Video').getAttribute('aria-pressed')).toBe('true');
    expect(container.textContent).toContain('đang chờ tải về máy');
    expect(container.textContent).toContain('Video đã tạo nhưng chưa tải được về máy.');
    await click(button('Tiếp tục / tải lại video đã gửi'));
    expect(host.sent.at(-1)?.type).toBe('outfit.text-resume');
    expect(host.sent.some(x => x.type === 'outfit.text-quote' || x.type === 'outfit.text-video')).toBe(false);
    await reply('operation.error', null, { code: 'provider_output_cache_invalid', message: 'Chưa tải được video.' });
    expect(button('Tiếp tục / tải lại video đã gửi').disabled).toBe(false);
  });
  it('shows only supported settings and saves a new project without calling AI', async () => {
    await mount(null, []); expect(host.sent).toHaveLength(0);
    expect(container.textContent).toContain('4 giây'); expect(container.textContent).toContain('6 giây'); expect(container.textContent).toContain('8 giây');
    expect(container.textContent).not.toContain('15 giây'); expect(container.textContent).not.toContain('1:1');
    const field = container.querySelector('textarea')!;
    await act(async () => { Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(field, 'Một con thuyền'); field.dispatchEvent(new Event('input', { bubbles: true })); });
    await click(button('6 giây')); await click(button('Lưu dự án và xem trước'));
    expect(create).toHaveBeenCalledWith(expect.objectContaining({ content: 'Một con thuyền', durationSeconds: 6 })); expect(host.sent).toHaveLength(0);
  });
  it('requires image load and explicit review before approving the first frame', async () => {
    await mount(project, [{ ...frame, status: 'PendingReview' }]);
    expect(button('Tạo video 8 giây').disabled).toBe(true); expect(button('Duyệt ảnh này').disabled).toBe(true);
    await act(async () => container.querySelector('img')!.dispatchEvent(new Event('load')));
    await click(container.querySelector('input[type=checkbox]')!);
    await click(button('Duyệt ảnh này')); expect(approveImage).toHaveBeenCalledWith(expect.objectContaining({ sceneFirstFrameId: 'frame' }));
    expect(host.sent).toHaveLength(0);
  });
  it('uses a server quote and a separate confirmation, then closes the dialog while processing', async () => {
    await mount(); await click(button('Tạo video 8 giây'));
    expect(host.sent.at(-1)).toMatchObject({ type: 'outfit.text-quote', payload: { projectId: 'project', organizationId: 'org' } });
    await reply('outfit.text-quote', { quoteId: 'quote', estimatedCost: .8, currencyCode: 'USD', modelCode: 'veo-snapshot', resolution: '720p', expiresAtUtc: new Date(Date.now() + 600000).toISOString() });
    expect(container.querySelector('dialog')?.textContent).toContain('veo-snapshot'); expect(host.sent).toHaveLength(1);
    await click(button('Xác nhận chi phí và tạo video'));
    expect(container.querySelector('dialog')).toBeNull();
    expect(host.sent.at(-1)).toMatchObject({ type: 'outfit.text-video', payload: { quoteId: 'quote', confirmed: true, projectId: 'project', organizationId: 'org' } });
    expect(button('Tạo video 8 giây').disabled).toBe(true);
    await reply('operation.error', null, { code: 'fal_rate_limited', message: 'Veo đang giới hạn tần suất.' });
    expect(container.textContent).toContain('Veo đang giới hạn tần suất.'); expect(button('Tạo video 8 giây').disabled).toBe(false);
  });
  it('requires explicit migration settings for old Kling projects and never starts AI on migration', async () => {
    await mount({ ...project, videoProviderCode: 'kling', project: { ...project.project, targetDurationSeconds: 15, aspectRatio: '1:1' } }, []);
    expect(button('Tạo video 15 giây').disabled).toBe(true);
    await click(button('Chuyển dự án sang Veo')); expect(host.sent).toHaveLength(0);
    expect(container.querySelector('dialog')?.textContent).toContain('cần tạo và duyệt ảnh đầu vào mới');
    await click(button('Xác nhận chuyển sang Veo'));
    expect(host.sent.at(-1)).toMatchObject({ type: 'outfit.migrate', payload: { projectId: 'project', organizationId: 'org', expectedProviderCode: 'kling', durationSeconds: 8, aspectRatio: '9:16', confirmed: true } });
    await reply('outfit.migrated', { projectId: 'project' }); expect(host.sent).toHaveLength(1);
  });
});
