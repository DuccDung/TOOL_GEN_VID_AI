// @vitest-environment jsdom
import { act, createElement } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage } from '../../types';
import { VietsubTranslationModeModal } from './VietsubSettingsPanel';
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
function Harness({ org = 'org', localOnly = false }: { org?: string; localOnly?: boolean }) { module = useVietsubModule(true, org, localOnly); return null; }
const workspace = (revision: number) => ({ activeTrackId: 'track', tracks: [{ trackId: 'track', source: 'PADDLE_OCR_LOCAL',
  languageCode: 'en', revision, cueCount: 100, translatedCueCount: 0, warningCueCount: 0, displayName: 'OCR', updatedAtUtc: '' }] });
const stateMessage = (revision = 1, projectId = 'project') => ({ type: 'vietsub.state', payload: {
  enabled: true, busy: false, selectedProject: { projectId, name: 'Fixture' }, subtitleWorkspace: workspace(revision), jobs: []
} });
async function emit(message: HostMessage) { await act(async () => { bridge.listener!(message); }); }
beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  bridge.posts = []; bridge.seq = 0;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

describe('Dịch Cloud', () => {
  it('chế độ local ẩn Cloud dù server báo sẵn sàng và vẫn cho cài Qwen', async () => {
    const install = vi.fn(), cloud = vi.fn();
    await act(async () => root.render(createElement(VietsubTranslationModeModal, {
      localOnly: true, busy: false,
      runtime: { status: 'NOT_INSTALLED', ready: false, engineId: 'local', engineVersion: '1', sourceLanguages: ['en'], supportsSceneContext: true, supportsReviewPass: false, message: 'Chưa cài Local' },
      cloudAvailability: { available: true }, onStartCloud: cloud, onStartLocal: vi.fn(), onInstallLocal: install, onDismiss: vi.fn()
    })));
    expect(document.body.textContent).not.toContain('Dịch Cloud');
    const localButton = document.querySelector<HTMLButtonElement>('.is-local button')!;
    expect(localButton.disabled).toBe(false);
    await act(async () => localButton.click());
    expect(install).toHaveBeenCalledTimes(1);
    expect(cloud).not.toHaveBeenCalled();
  });

  it('chế độ local không hỏi readiness hoặc gửi Cloud khi mở dự án hay gọi callback trực tiếp', async () => {
    await act(async () => root.render(createElement(Harness, { localOnly: true })));
    await emit(stateMessage());
    await act(async () => { module.refreshCloudAvailability(); await module.startCloudTranslation(); });
    expect(module.state.localOnly).toBe(true);
    expect(bridge.posts.filter(p => p.type === 'vietsub.cloud.availability' || p.type === 'vietsub.job.translate.cloud')).toEqual([]);
    expect(bridge.posts.some(p => p.type === 'vietsub.state.get')).toBe(true);
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
