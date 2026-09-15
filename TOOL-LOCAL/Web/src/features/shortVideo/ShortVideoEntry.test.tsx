// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../../App';
import { postToHost } from '../../bridge';
import type { DashboardState, HostMessage, ProjectDashboard } from '../../types';

const bridge = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>(), sequence: 0 }));
vi.mock('../../bridge', () => ({
  isHosted: true,
  postToHost: vi.fn(() => `request-${++bridge.sequence}`),
  subscribeToHost: (listener: (message: HostMessage) => void) => {
    bridge.listeners.add(listener);
    return () => { bridge.listeners.delete(listener); };
  }
}));
vi.mock('../vietsub/useVietsubModule', () => ({
  useVietsubModule: () => ({ state: { enabled: false, loading: false, busy: false } })
}));

function dashboard(enabled = true): DashboardState {
  return {
    profile: { userId: 'user', email: 'user@example.test', displayName: 'Người dùng', accountStatus: 'Active', roles: [] },
    organizations: [], selectedOrganizationId: 'org', projects: [], selectedProject: null, models: [], assetLibrary: null,
    providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
    mediaTools: { ready: false, message: 'Chưa kiểm tra media.', checkedAtUtc: '' }, generationRunning: false,
    features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false, shortVideoCharacterOutfitEnabled: enabled },
    sceneFirstFrames: [],
    license: { hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 1, activeDeviceCount: 1,
      offlineGraceHours: 0, serverTimeUtc: '2026-09-10T00:00:00Z', heartbeatIntervalSeconds: 300,
      leaseExpiresAtUtc: '2026-09-10T00:15:00Z', accessState: 'Active' }
  };
}

const project: ProjectDashboard = {
  project: { projectId: 'outfit-project', organizationId: 'org', name: 'Trang phục mùa hè', topic: 'Trang phục mùa hè',
    platform: 'TikTok', aspectRatio: '9:16', targetDurationSeconds: 8, status: 'ScenePlanning', actualCost: 0, updatedAtUtc: '' },
  languageCode: 'vi-VN', createdAtUtc: '', totalScenes: 1, approvedScenes: 0, failedScenes: 0,
  pendingJobs: 0, runningJobs: 0, failedJobs: 0, overallProgressPercent: 0, pipeline: [], characters: [], scenes: [],
  render: { status: 'Pending', progressPercent: 0, completedScenes: 0, totalScenes: 1 },
  audioStrategy: 'ProviderNative', speechProductionPolicy: 'ProviderNativeVerified',
  workflowStructureType: 'DirectShortVideo', shortVideoMode: 'CharacterOutfit', requiresVietnameseContentRegeneration: false
};
let container: HTMLDivElement;
let root: Root;
function button(label: string) {
  const found = [...container.querySelectorAll('button')].find(element => element.textContent?.trim() === label);
  if (!found) throw new Error(`Missing button: ${label}`);
  return found;
}
async function click(element: HTMLElement) { await act(async () => element.click()); }
async function emit(message: HostMessage) {
  await act(async () => { for (const listener of [...bridge.listeners]) listener(message); });
}
async function openShortVideo(state = dashboard()) {
  await emit({ type: 'dashboard.state', payload: state });
  await click(button('Tạo Video Ngắn'));
}

beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
  Element.prototype.scrollTo = vi.fn();
  HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) { this.setAttribute('open', ''); });
  HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) { this.removeAttribute('open'); });
  vi.clearAllMocks();
  bridge.sequence = 0;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root.render(<App />));
});
afterEach(async () => {
  await act(async () => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals();
});

describe('entry to character and outfit from the short-video page', () => {
  it('opens the outfit mode and reaches both image controls without starting generation', async () => {
    await openShortVideo();
    expect(container.querySelector('dialog')).toBeNull();
    expect(container.querySelector('[aria-label="Chọn ảnh nhân vật"]')).not.toBeNull();
    expect(container.querySelector('[aria-label="Chọn ảnh trang phục"]')).not.toBeNull();
    expect(postToHost).toHaveBeenLastCalledWith('short-library.get', { organizationId: 'org' });
    await emit({ type: 'dashboard.state', payload: { ...dashboard(), selectedProject: project, projects: [project.project] } });
    expect(postToHost).toHaveBeenCalledWith('outfit.get', expect.objectContaining({ projectId: 'outfit-project', organizationId: 'org' }));
    expect(vi.mocked(postToHost).mock.calls.some(([type]) => ['short-video.generate', 'generation.video', 'outfit.compose', 'outfit.video'].includes(type))).toBe(false);
  });

  it('keeps the normal new-project chooser alongside the inline composer', async () => {
    await openShortVideo();
    await click(container.querySelector<HTMLButtonElement>('[aria-label="Tạo video mới"]')!);
    const dialog = container.querySelector('dialog')!;
    expect(dialog.textContent).toContain('Bạn muốn tạo video nào?');
    expect(dialog.querySelector('select')).toBeNull();
  });

  it('keeps text-only mode when disabled and locks image controls during generation', async () => {
    await openShortVideo(dashboard(false));
    expect(container.querySelector('.short-video-outfit-entry')).toBeNull();
    await emit({ type: 'dashboard.state', payload: { ...dashboard(), generationRunning: true } });
    expect(container.querySelector<HTMLButtonElement>('[aria-label="Chọn ảnh nhân vật"]')?.disabled).toBe(true);
    expect(container.querySelector<HTMLButtonElement>('[aria-label="Chọn ảnh trang phục"]')?.disabled).toBe(true);
    expect(container.querySelector('dialog')).toBeNull();
  });
});
