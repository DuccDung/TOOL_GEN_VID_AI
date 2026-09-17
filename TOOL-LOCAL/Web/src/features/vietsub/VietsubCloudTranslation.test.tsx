// @vitest-environment jsdom
import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage } from '../../types';
import { VietsubTranslationModeModal } from './VietsubSettingsPanel';
import { VietsubTranslationGpuFallbackModal } from './VietsubTranslationGpuFallbackModal';
import { useVietsubModule } from './useVietsubModule';

const bridge = vi.hoisted(() => ({ listener: null as null | ((message: HostMessage) => void),
  posts: [] as { type: string; requestId: string; payload: unknown }[], seq: 0 }));
vi.mock('../../bridge', () => ({ isHosted: true,
  postToHost: (type: string, payload?: unknown) => {
    const requestId = `request-${++bridge.seq}`; bridge.posts.push({ type, requestId, payload }); return requestId;
  },
  subscribeToHost: (listener: (message: HostMessage) => void) => { bridge.listener = listener; return () => { bridge.listener = null; }; }
}));
let root: Root, container: HTMLDivElement;
let module: ReturnType<typeof useVietsubModule>;
function Harness({ org = 'org' }: { org?: string }) { module = useVietsubModule(true, org); return null; }
const workspace = (revision: number) => ({ activeTrackId: 'track', tracks: [{ trackId: 'track', source: 'PADDLE_OCR_LOCAL',
  languageCode: 'en', revision, cueCount: 100, translatedCueCount: 0, warningCueCount: 0, displayName: 'OCR', updatedAtUtc: '' }] });
const stateMessage = (revision = 1, projectId = 'project') => ({ type: 'vietsub.state', payload: {
  enabled: true, busy: false, selectedProject: { projectId, name: 'Fixture' }, subtitleWorkspace: workspace(revision), jobs: []
} });
async function emit(message: HostMessage) { await act(async () => { bridge.listener!(message); }); }
const localJob = (id = 'job') => ({ type: 'vietsub.job.changed', payload: {
  id, projectId: 'project', type: 'TRANSLATE_LOCAL', status: 'RUNNING', updatedAtUtc: '2026-09-17T00:00:00Z',
  progressPercent: 10, attemptCount: 1
} });
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  bridge.posts = []; bridge.seq = 0;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

describe('Dịch Cloud', () => {
  it('gửi lựa chọn CPU hoặc Auto từ modal và khóa thay đổi khi đang chạy', async () => {
    const start = vi.fn();
    const props = { busy: false, runtime: { status: 'READY' as const, ready: true,
      sourceLanguages: ['en'], supportsSceneContext: true, supportsReviewPass: false, message: 'Ready' },
      onStartLocal: start, onInstallLocal: vi.fn(), onDismiss: vi.fn() };
    await act(async () => root.render(createElement(VietsubTranslationModeModal, props)));
    expect(document.querySelectorAll('.is-local button')).toHaveLength(1);
    expect(document.body.textContent).not.toContain('Kiểm tra / sửa tăng tốc NVIDIA');
    expect(document.body.textContent).toContain('Khi bấm dịch, ứng dụng tự kiểm tra GPU');
    const select = document.querySelector<HTMLSelectElement>('[aria-label="Xử lý dịch local"]')!;
    await act(async () => { select.value = 'CPU_ONLY'; select.dispatchEvent(new Event('change', { bubbles: true })); });
    const localButton = Array.from(document.querySelectorAll<HTMLButtonElement>('.is-local button'))
      .find(button => button.textContent?.includes('Dịch bằng Local'))!;
    await act(async () => localButton.click());
    expect(start).toHaveBeenCalledWith('CPU_ONLY');
    await act(async () => root.render(createElement(VietsubTranslationModeModal, { ...props, busy: true })));
    expect(select.disabled).toBe(true);
    expect(Array.from(document.querySelectorAll<HTMLButtonElement>('.is-local button')).every(b => b.disabled)).toBe(true);
  });

  it('giữ lựa chọn tăng tốc qua hộp xác nhận RAM', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    await emit({ type: 'vietsub.translation.runtime.status', payload: { status: 'READY', ready: true,
      requiresResourceConfirmation: true, resourceWarningCode: 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED', resourceWarningMessage: 'Thiếu RAM' } });
    await act(async () => module.startTranslation('CONTINUE', 'AUTO'));
    expect(bridge.posts.filter(p => p.type === 'vietsub.job.translate')).toHaveLength(0);
    await act(async () => module.continueTranslationAfterResourceWarning());
    expect(bridge.posts.find(p => p.type === 'vietsub.job.translate')?.payload)
      .toMatchObject({ executionPolicy: 'AUTO', confirmResourceWarning: true });
  });

  it('bỏ trạng thái GPU từ project hoặc tổ chức cũ', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    await emit({ type: 'vietsub.translation.runtime.status', payload: { status: 'READY', ready: true } });
    await emit(localJob());
    const payload = { projectId: 'old', organizationId: 'org', jobId: 'job', effectiveBackend: 'cuda12', deviceName: 'NVIDIA' };
    await emit({ type: 'vietsub.translation.execution', payload });
    expect(module.state.translationRuntime?.effectiveBackend).toBeUndefined();
    await emit({ type: 'vietsub.translation.execution', payload: { ...payload, projectId: 'project', organizationId: 'old' } });
    expect(module.state.translationRuntime?.effectiveBackend).toBeUndefined();
    await emit({ type: 'vietsub.translation.execution', payload: { ...payload, projectId: 'project' } });
    expect(module.state.translationRuntime?.effectiveBackend).toBe('cuda12');
  });

  it('báo chuyển CPU một lần mỗi job, đóng modal không gửi lại lệnh dịch', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    await emit(localJob());
    const payload = { projectId: 'project', organizationId: 'org', jobId: 'job', effectiveBackend: 'cpu',
      cpuFallback: true, fallbackCode: 'TRANSLATION_GPU_NOT_INSTALLED', fallbackMessage: 'Thiếu gói GPU. Đang tiếp tục bằng CPU.' };
    await emit({ type: 'vietsub.translation.execution', payload: { ...payload, jobId: 'old-job' } });
    expect(module.state.translationGpuFallbackAlert).toBeFalsy();
    await emit({ type: 'vietsub.translation.execution', payload });
    expect(module.state.translationGpuFallbackAlert).toMatchObject({ jobId: 'job', dismissed: false, message: payload.fallbackMessage });
    const postsBeforeDismiss = bridge.posts.length;
    await act(async () => module.dismissTranslationGpuFallbackAlert());
    await emit({ type: 'vietsub.translation.execution', payload });
    expect(module.state.translationGpuFallbackAlert?.dismissed).toBe(true);
    expect(bridge.posts).toHaveLength(postsBeforeDismiss);
    await emit(localJob('next-job'));
    await emit({ type: 'vietsub.translation.execution', payload: { ...payload, jobId: 'next-job' } });
    expect(module.state.translationGpuFallbackAlert).toMatchObject({ jobId: 'next-job', dismissed: false });
    await emit(stateMessage(1, 'next-project'));
    expect(module.state.translationGpuFallbackAlert).toBeNull();
  });

  it('không báo modal cho CPU được chọn chủ động hoặc GPU đang dùng được', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    await emit(localJob());
    for (const backend of ['cpu', 'cuda12']) {
      await emit({ type: 'vietsub.translation.execution', payload: {
        projectId: 'project', organizationId: 'org', jobId: 'job', effectiveBackend: backend, cpuFallback: false
      } });
      expect(module.state.translationGpuFallbackAlert).toBeFalsy();
    }
  });

  it('modal GPU chỉ thông báo và đóng bằng Đã hiểu, không yêu cầu bấm dịch lần nữa', async () => {
    const dismiss = vi.fn();
    await act(async () => root.render(createElement(VietsubTranslationGpuFallbackModal, {
      message: 'Driver NVIDIA chưa phù hợp. Ứng dụng chuyển sang CPU.', onDismiss: dismiss
    })));
    const dialog = document.querySelector('[role="alertdialog"]')!;
    expect(dialog.textContent).toContain('Đã chuyển sang dịch bằng CPU');
    expect(dialog.textContent).toContain('Driver NVIDIA chưa phù hợp');
    expect(dialog.querySelectorAll('button')).toHaveLength(1);
    const button = dialog.querySelector('button')!;
    expect(button.textContent).toBe('Đã hiểu');
    expect(document.activeElement).toBe(button);
    await act(async () => button.click());
    expect(dismiss).toHaveBeenCalledTimes(1);
    expect(bridge.posts).toHaveLength(0);
  });
  it('cho phép dịch bằng một nút dù Local chưa cài, không có bộ chọn model hoặc API key', async () => {
    const cloud = vi.fn(), local = vi.fn();
    await act(async () => root.render(createElement(VietsubTranslationModeModal, {
      busy: false, runtime: { status: 'NOT_INSTALLED', ready: false, engineId: 'local', engineVersion: '1', sourceLanguages: ['en'], supportsSceneContext: true, supportsReviewPass: false, message: 'Chưa cài Local' },
      cloudAvailability: { available: true }, onStartCloud: cloud, onStartLocal: local,
      onInstallLocal: local, onDismiss: () => { }
    })));
    const section = document.querySelector('.is-cloud')!;
    expect(section.querySelector('select,input')).toBeNull();
    expect(section.textContent).not.toMatch(/OpenAI|gpt-|API key/i);
    const button = section.querySelector('button')!;
    expect(button.disabled).toBe(false);
    await act(async () => button.click());
    expect(cloud).toHaveBeenCalledTimes(1); expect(local).not.toHaveBeenCalled();
  });

  it.each([null, { available: false, message: 'Dịch Cloud chưa được bật.' }])('khóa Cloud khi chưa sẵn sàng: %j', async availability => {
    await act(async () => root.render(createElement(VietsubTranslationModeModal, {
      busy: false, cloudAvailability: availability, onStartCloud: vi.fn(), onStartLocal: vi.fn(), onInstallLocal: vi.fn(), onDismiss: vi.fn()
    })));
    expect(document.querySelector<HTMLButtonElement>('.is-cloud button')!.disabled).toBe(true);
  });

  it('lưu bản nháp trước khi gửi, dùng revision mới và chặn bấm lặp trong lúc chờ lưu', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    let finish!: (saved: boolean) => void;
    const pending = new Promise<boolean>(resolve => { finish = resolve; });
    module.registerBeforeLeave(() => pending);
    let first!: Promise<void>;
    await act(async () => { first = module.startCloudTranslation(); void module.startCloudTranslation(); });
    expect(bridge.posts.filter(p => p.type === 'vietsub.job.translate.cloud')).toHaveLength(0);
    await emit(stateMessage(2));
    await act(async () => { finish(true); await first; });
    const sent = bridge.posts.filter(p => p.type === 'vietsub.job.translate.cloud');
    expect(sent).toHaveLength(1);
    expect(sent[0].payload).toEqual({ expectedTrackId: 'track', expectedTrackRevision: 2 });
    expect(module.state.translationResourceAlert).toBeNull();
  });

  it('không gửi khi lưu thất bại hoặc người dùng đổi dự án trong lúc lưu', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    module.registerBeforeLeave(async () => false);
    await act(async () => { await module.startCloudTranslation(); });
    let finish!: (saved: boolean) => void;
    module.registerBeforeLeave(() => new Promise(resolve => { finish = resolve; }));
    let pending!: Promise<void>;
    await act(async () => { pending = module.startCloudTranslation(); });
    await emit(stateMessage(1, 'another-project'));
    await act(async () => { finish(true); await pending; });
    expect(bridge.posts.filter(p => p.type === 'vietsub.job.translate.cloud')).toHaveLength(0);
  });

  it('loại trạng thái Cloud đến muộn từ tổ chức cũ', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    const query = bridge.posts.filter(p => p.type === 'vietsub.cloud.availability').at(-1)!;
    await act(async () => root.render(createElement(Harness, { org: 'org-new' })));
    await emit({ type: 'vietsub.cloud.availability', requestId: query.requestId,
      payload: { projectId: 'project', organizationId: 'org', availability: { available: true } } });
    expect(module.state.cloudAvailability).toBeNull();
  });

  it('chặn yêu cầu điều khiển lặp cho tới khi server phản hồi', async () => {
    await act(async () => root.render(createElement(Harness)));
    await emit(stateMessage());
    await act(async () => { module.pauseJob('job'); module.pauseJob('job'); });
    expect(bridge.posts.filter(p => p.type === 'vietsub.job.pause')).toHaveLength(1);
    expect(module.state.busy).toBe(true);
    await emit(stateMessage());
    expect(module.state.busy).toBe(false);
  });
});
