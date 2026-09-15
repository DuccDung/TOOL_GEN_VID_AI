// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../../App';
import { postToHost } from '../../bridge';
import type { DashboardState, HostMessage } from '../../types';
import type { PublishingInput, PublishingPreview, PublishingSchedule, PublishingState } from './types';

const bridge = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>(), sequence: 0 }));
vi.mock('../../bridge', () => ({ isHosted: true, postToHost: vi.fn(() => `pub-request-${++bridge.sequence}`),
  subscribeToHost: (listener: (message: HostMessage) => void) => { bridge.listeners.add(listener); return () => bridge.listeners.delete(listener); } }));
vi.mock('../vietsub/useVietsubModule', () => ({ useVietsubModule: () => ({ state: { enabled: false, loading: false, busy: false } }) }));
let container: HTMLDivElement; let root: Root;
const input: PublishingInput = { title: 'Giới thiệu sản phẩm', description: 'Nhân vật cầm sản phẩm trong phòng sáng.', characterImageId: 'character', productImageId: 'product',
  startDate: '2026-09-12', endDate: '2026-09-18', publishTime: '19:00', timeZoneId: 'Asia/Ho_Chi_Minh', weekdays: 127, leadMinutes: 120, latePolicy: 'SameDay',
  durationSeconds: 8, aspectRatio: '9:16', maximumCostPerRun: 5, targets: [{ platform: 'YouTube', connectionId: 'youtube-1', privacy: 'private', madeForKids: false, brandOrganic: false, brandContent: false }] };
const schedule: PublishingSchedule = { scheduleId: 'schedule-1', revision: 2, status: 'Draft', input, nextPublishAtUtc: null, updatedAtUtc: '2026-09-11T05:00:00Z' };
const state: PublishingState = { enabled: true, unavailableReason: null, schedules: [schedule], runs: [],
  connections: [{ connectionId: 'youtube-1', platform: 'YouTube', displayName: 'Kênh của tôi', status: 'Connected' }],
  platforms: [{ platform: 'TikTok', configured: true, message: null }, { platform: 'YouTube', configured: true, message: null }, { platform: 'Facebook', configured: false, message: 'Chưa cấu hình ứng dụng Meta.' }] };
const dashboard: DashboardState = { profile: { userId: 'user-1', email: 'a@example.test', accountStatus: 'Active', roles: [] }, organizations: [], selectedOrganizationId: 'org-1',
  projects: [], selectedProject: null, assetLibrary: null, models: [], sceneFirstFrames: [], providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: true, message: 'OK', checkedAtUtc: '' }, generationRunning: false, features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: true },
  license: { hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 1, activeDeviceCount: 1, offlineGraceHours: 0, serverTimeUtc: '', heartbeatIntervalSeconds: 300, accessState: 'Active' } };
async function emit(message: HostMessage) { await act(async () => bridge.listeners.forEach(listener => listener(message))); }
async function click(element: HTMLElement) { await act(async () => element.click()); }
function button(text: string, parent: ParentNode = document) { return [...parent.querySelectorAll<HTMLButtonElement>('button')].find(x => x.textContent?.trim() === text)!; }
function last(type: string) { const index = vi.mocked(postToHost).mock.calls.map(([name]) => name).lastIndexOf(type); return { id: vi.mocked(postToHost).mock.results[index].value as string, payload: vi.mocked(postToHost).mock.calls[index][1] }; }
async function result(action: string, data: unknown, org = 'org-1', id?: string) { await emit({ type: 'publishing.result', requestId: id ?? last(action).id, payload: { organizationId: org, action, data } }); }
async function open() {
  await emit({ type: 'dashboard.state', payload: dashboard });
  await click(container.querySelector('[aria-label="Lên lịch xuất bản"]')!);
  await result('publishing.state.get', structuredClone(state));
}
beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true); vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
  Element.prototype.scrollTo = vi.fn(); vi.clearAllMocks(); bridge.sequence = 0;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root.render(<App/>));
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('Publishing schedule workflow', () => {
  it('opens the real menu and reads state without creating or posting', async () => {
    await open(); expect(container.querySelector('.publishing-page')).not.toBeNull();
    expect(container.textContent).toContain(input.title);
    expect(vi.mocked(postToHost).mock.calls.filter(([type]) => type.startsWith('publishing.')).map(([type]) => type)).toEqual(['publishing.state.get']);
  });
  it('requires explicit automation consent before activating a draft', async () => {
    await open(); await click(button('Kích hoạt'));
    const dialog = document.querySelector('[role="dialog"]')!;
    expect(button('Kích hoạt lịch', dialog).disabled).toBe(true);
    await click(dialog.querySelector('input[type=checkbox]')!); await click(button('Kích hoạt lịch', dialog));
    expect(last('publishing.schedule.change').payload).toEqual({ organizationId: 'org-1', scheduleId: 'schedule-1', expectedRevision: 2, action: 'Activate', confirmAutomaticGeneration: true });
  });
  it('saves edited input as a draft and never implicitly activates or generates', async () => {
    await open(); await click(container.querySelector('[aria-label="Sửa Giới thiệu sản phẩm"]')!);
    await act(async () => document.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    expect(last('publishing.schedule.save').payload).toEqual({ organizationId: 'org-1', scheduleId: 'schedule-1', expectedRevision: 2, input });
    expect(vi.mocked(postToHost).mock.calls.some(([type]) => type === 'publishing.schedule.change')).toBe(false);
  });
  it('clears busy after a matching error and keeps the editor for correction', async () => {
    await open(); await click(container.querySelector('[aria-label="Sửa Giới thiệu sản phẩm"]')!);
    await act(async () => document.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    const id = last('publishing.schedule.save').id;
    expect(button('Đang xử lý…').disabled).toBe(true);
    await emit({ type: 'publishing.error', requestId: id, error: { code: 'publishing_revision_changed', message: 'Lịch đã đổi.' } });
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Lịch đã đổi.'); expect(button('Lưu bản nháp').disabled).toBe(false);
  });
  it('ignores a delayed state from the previous organization', async () => {
    await open(); await click(button('Làm mới')); const old = last('publishing.state.get').id;
    await emit({ type: 'dashboard.state', payload: { ...dashboard, selectedOrganizationId: 'org-2' } });
    await result('publishing.state.get', { ...state, schedules: [] }, 'org-2');
    await result('publishing.state.get', state, 'org-1', old);
    expect(container.textContent).not.toContain(input.title); expect(container.textContent).toContain('Chưa có lịch xuất bản');
  });
  it('does not enable an unconfigured Facebook connector', async () => {
    await open(); const card = [...container.querySelectorAll('.pub-platform')].find(x => x.textContent?.includes('Facebook'))!;
    expect(card.querySelector('button')?.disabled).toBe(true); expect(card.textContent).toContain('Chưa cấu hình ứng dụng Meta.');
  });
  it('prevents editing an active schedule until it is paused', async () => {
    await open(); await click(button('Làm mới')); await result('publishing.state.get', { ...state, schedules: [{ ...schedule, status: 'Active' }] });
    expect(container.querySelector<HTMLButtonElement>('[aria-label="Sửa Giới thiệu sản phẩm"]')!.disabled).toBe(true);
    await click(button('Tạm dừng'));
    expect(last('publishing.schedule.change').payload).toMatchObject({ scheduleId: 'schedule-1', expectedRevision: 2, action: 'Pause', organizationId: 'org-1' });
  });
  it('requires the generated video, privacy choice and consent for TikTok', async () => {
    await open(); const target = { platform: 'TikTok' as const, connectionId: 'tik-1', privacy: 'review', madeForKids: false, brandOrganic: false, brandContent: false };
    const run = { runId: 'run-1', scheduleId: 'schedule-1', title: input.title, status: 'AwaitingReview', input: { ...input, targets: [target] },
      mediaSha256: 'a'.repeat(64), publishAtUtc: '2026-09-12T12:00:00Z', generateAtUtc: '2026-09-12T10:00:00Z', deadlineAtUtc: '2026-09-12T17:00:00Z', updatedAtUtc: '',
      errorCode: null, message: null, projectId: null, estimatedCost: 2, deliveries: [], canResume: false };
    await click(button('Làm mới')); await result('publishing.state.get', { ...state, runs: [run] });
    await click([...container.querySelectorAll<HTMLButtonElement>('[role=tab]')].find(x => x.textContent?.includes('Lượt chạy'))!);
    await click(button('Xem và duyệt'));
    const preview: PublishingPreview = { run, previewUrl: 'https://tiktok-media.app.local/video/test', creators: [{ connectionId: 'tik-1', creatorNickname: 'Nhà sáng tạo', privacyLevelOptions: ['SELF_ONLY'], maximumVideoDurationSeconds: 60 }] };
    await result('publishing.run.preview', preview);
    const dialog = document.querySelector('[role=dialog]')!;
    expect(dialog.querySelector('video')!.getAttribute('src')).toBe(preview.previewUrl);
    expect(button('Xác nhận đăng theo lịch', dialog).disabled).toBe(true);
    const select = dialog.querySelector('select')!;
    await act(async () => { select.value = 'SELF_ONLY'; select.dispatchEvent(new Event('change', { bubbles: true })); });
    const checks = dialog.querySelectorAll<HTMLInputElement>('input[type=checkbox]');
    await click(checks[checks.length - 2]); await click(checks[checks.length - 1]);
    await click(button('Xác nhận đăng theo lịch', dialog));
    expect(last('publishing.run.review').payload).toMatchObject({ organizationId: 'org-1', runId: 'run-1', mediaSha256: run.mediaSha256, approve: true,
      targets: [{ connectionId: 'tik-1', privacy: 'SELF_ONLY', title: input.title }] });
  });
});
