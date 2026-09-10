// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../../App';
import type { DashboardState, HostMessage } from '../../types';
import { postToHost } from '../../bridge';
import { applyBilibiliUpdate, initialBilibiliState, type BilibiliState } from './types';

const bridge = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>(), sequence: 0 }));
vi.mock('../../bridge', () => ({
  isHosted: true, postToHost: vi.fn(() => `request-${++bridge.sequence}`),
  subscribeToHost: (listener: (message: HostMessage) => void) => { bridge.listeners.add(listener); return () => bridge.listeners.delete(listener); }
}));
vi.mock('../vietsub/useVietsubModule', () => ({ useVietsubModule: () => ({ state: { enabled: false, loading: false, busy: false } }) }));

let container: HTMLDivElement;
let root: Root;
const snapshot: BilibiliState = {
  revision: 5, runtimeReady: true, runtimeVersion: '2026.08.19', operation: 'Idle', folderLabel: 'Bilibili',
  scan: { id: 'scan-1', count: 2, complete: true, status: 'Completed', message: 'Đã quét xong 2 video.' }, jobs: [],
  entries: [
    { id: 'entry-1', title: 'Video thứ nhất', url: 'https://www.bilibili.com/video/BV13x41117TL', durationSeconds: 30 },
    { id: 'entry-2', title: 'Video thứ hai', url: 'https://www.bilibili.com/video/BV13x41117TL?p=2', durationSeconds: 60 }
  ]
};
const dashboard: DashboardState = {
  profile: { userId: 'user-1', email: 'a@example.test', accountStatus: 'Active', roles: [] }, organizations: [],
  selectedOrganizationId: 'org', projects: [], selectedProject: null, assetLibrary: null, models: [], sceneFirstFrames: [],
  providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: true, message: 'OK', checkedAtUtc: '' }, generationRunning: false,
  features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false },
  license: { hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 1, activeDeviceCount: 1,
    offlineGraceHours: 0, serverTimeUtc: '', heartbeatIntervalSeconds: 300, accessState: 'Active' }
};
async function emit(message: HostMessage) { await act(async () => bridge.listeners.forEach(listener => listener(message))); }
async function click(button: HTMLElement) { await act(async () => button.click()); }
function button(label: string) { return [...container.querySelectorAll('button')].find(item => item.textContent?.includes(label))!; }
async function openModule() {
  await emit({ type: 'dashboard.state', payload: dashboard });
  await click(container.querySelector('[aria-label="Tải video Bilibili"]')!);
  const index = vi.mocked(postToHost).mock.calls.map(([type]) => type).lastIndexOf('bilibili.state.get');
  const requestId = vi.mocked(postToHost).mock.results[index].value as string;
  await emit({ type: 'bilibili.state', payload: snapshot });
  await emit({ type: 'bilibili.ack', requestId });
}
beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
  Element.prototype.scrollTo = vi.fn();
  vi.clearAllMocks(); bridge.sequence = 0;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root.render(<App />));
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('Bilibili desktop workflow', () => {
  it('opens from left menu and loads state without scanning or downloading', async () => {
    await openModule();
    expect(container.querySelector('.bili-page')).not.toBeNull();
    expect(container.textContent).toContain('Video thứ nhất');
    expect(vi.mocked(postToHost).mock.calls.some(([type]) => ['bilibili.scan', 'bilibili.download', 'bilibili.install'].includes(type))).toBe(false);
  });
  it('downloads only checked entries with the native scan id and locks duplicate submissions', async () => {
    await openModule();
    await click(container.querySelector('[aria-label="Chọn Video thứ hai"]')!);
    await click(button('Tải các video đã chọn'));
    expect(postToHost).toHaveBeenLastCalledWith('bilibili.download', { scanId: 'scan-1', entryIds: ['entry-2'], quality: 'best' });
    expect(button('Tải các video đã chọn').disabled).toBe(true);
    expect(container.querySelector<HTMLInputElement>('[aria-label="Chọn Video thứ nhất"]')!.disabled).toBe(true);
  });
  it('selects all results across pagination and clears selection for a new scan', async () => {
    await openModule();
    const many = Array.from({ length: 30 }, (_, i) => ({ ...snapshot.entries[0], id: `item-${i}`, title: `Video ${i}` }));
    await emit({ type: 'bilibili.state', payload: { ...snapshot, revision: 6, entries: many } });
    expect(container.querySelectorAll('.bili-video-row')).toHaveLength(24);
    await click(container.querySelector('.bili-select-all input')!);
    expect(container.querySelector('.bili-download-bar')!.textContent).toContain('30');
    await emit({ type: 'bilibili.state', payload: { ...snapshot, revision: 7, scan: { ...snapshot.scan, id: 'scan-2' } } });
    expect(button('Tải các video đã chọn').disabled).toBe(true);
  });
  it('retains partial results and exposes a clear incomplete notice', async () => {
    await openModule();
    await emit({ type: 'bilibili.state', payload: { ...snapshot, revision: 6, scan: { ...snapshot.scan, status: 'Partial', complete: false } } });
    expect(container.querySelector('.bili-partial')!.textContent).toContain('Danh sách chưa đầy đủ');
    await click(container.querySelector('[aria-label="Chọn Video thứ nhất"]')!);
    expect(button('Tải các video đã chọn').disabled).toBe(false);
  });
  it('cancels an active job by id without sending a filesystem path', async () => {
    await openModule();
    await emit({ type: 'bilibili.state', payload: { ...snapshot, revision: 6, operation: 'Downloading', jobs: [
      { id: 'job-1', video: snapshot.entries[0], quality: '720', status: 'Downloading', percent: 32, downloadedBytes: 1200 }
    ] } });
    await click(container.querySelector('.bili-job-actions button')!);
    expect(postToHost).toHaveBeenLastCalledWith('bilibili.cancel', { jobId: 'job-1' });
    expect(container.querySelector('progress')!.value).toBe(32);
  });
  it('releases pending state on error and ignores a reply to an unrelated request', async () => {
    await openModule();
    await click(container.querySelector('[aria-label="Chọn Video thứ nhất"]')!);
    await click(button('Tải các video đã chọn'));
    const requestId = vi.mocked(postToHost).mock.results.at(-1)!.value as string;
    await emit({ type: 'bilibili.error', requestId: 'unrelated', error: { code: 'x', message: 'Stale' } });
    expect(button('Tải các video đã chọn').disabled).toBe(true);
    await emit({ type: 'bilibili.error', requestId, error: { code: 'blocked', message: 'Bilibili đang hạn chế truy cập.' } });
    expect(button('Tải các video đã chọn').disabled).toBe(false);
    expect(container.querySelector('.bili-error')!.textContent).toContain('Bilibili đang hạn chế truy cập');
  });
});

describe('Bilibili host response ordering', () => {
  it('does not restore stale snapshots or old scan results', () => {
    expect(applyBilibiliUpdate(snapshot, 'bilibili.state', { ...initialBilibiliState, revision: 4 })).toBe(snapshot);
    expect(applyBilibiliUpdate({ ...snapshot, operation: 'Scanning' }, 'bilibili.scan.progress', {
      revision: 7, scan: { ...snapshot.scan, id: 'old-scan' }, entries: []
    }).entries).toEqual(snapshot.entries);
  });
  it('merges streamed results once per video', () => {
    const result = applyBilibiliUpdate({ ...snapshot, operation: 'Scanning' }, 'bilibili.scan.progress', {
      revision: 6, scan: snapshot.scan, entries: [snapshot.entries[0]]
    });
    expect(result.entries).toHaveLength(2);
  });
});
