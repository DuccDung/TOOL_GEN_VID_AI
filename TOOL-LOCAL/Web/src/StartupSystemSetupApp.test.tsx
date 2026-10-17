// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { DashboardState, DesktopDirectRelease, DesktopStandaloneRelease, HostMessage } from './types';
import type { SetupSnapshot } from './features/systemSetup/types';

const bridge = vi.hoisted(() => ({
  listeners: new Set<(message: HostMessage) => void>(),
  posts: [] as { type: string; requestId: string; payload?: unknown }[],
  sequence: 0,
  vietsubFeatureEnabled: [] as boolean[]
}));

vi.mock('./bridge', () => ({
  isHosted: true,
  postToHost(type: string, payload?: unknown) {
    const requestId = `app-${++bridge.sequence}`;
    bridge.posts.push({ type, requestId, payload });
    return requestId;
  },
  subscribeToHost(listener: (message: HostMessage) => void) {
    bridge.listeners.add(listener);
    return () => bridge.listeners.delete(listener);
  }
}));

vi.mock('./features/vietsub/useVietsubModule', () => ({
  useVietsubModule: (featureEnabled: boolean) => {
    bridge.vietsubFeatureEnabled.push(featureEnabled);
    return { state: { enabled: featureEnabled, loading: false, busy: false } };
  }
}));

const dashboard: DashboardState = {
  profile: { userId: 'user', email: 'user@example.test', displayName: 'Người dùng', accountStatus: 'Active', roles: [] },
  organizations: [{ organizationId: 'org', code: 'ORG', name: 'Tổ chức', role: 'Member', status: 'Active',
    monthlyBudgetLimit: 0, reservedCost: 0, actualCost: 0, remainingBudget: 0, currencyCode: 'USD',
    periodStartsAtUtc: '2026-09-01T00:00:00Z', periodEndsAtUtc: '2026-10-01T00:00:00Z' }],
  selectedOrganizationId: 'org', projects: [], selectedProject: null, models: [], assetLibrary: null,
  providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: false, message: 'Chưa kiểm tra công cụ media.', checkedAtUtc: '' },
  generationRunning: false,
  features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false },
  sceneFirstFrames: [],
  license: { hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 1, activeDeviceCount: 1,
    offlineGraceHours: 0, serverTimeUtc: '2026-09-15T00:00:00Z', heartbeatIntervalSeconds: 300,
    leaseExpiresAtUtc: '2026-09-15T00:15:00Z', accessState: 'Active' }
};

const setupSnapshot = (ready: boolean): SetupSnapshot => ({
  contextGeneration: 'context', organizationId: 'org', revision: ready ? 2 : 1, startupRequired: true,
  components: ['media', 'ocr', 'qwen', 'piper'].map(id => ({ id, name: id, version: '1',
    state: ready ? 'READY' : 'UNKNOWN', message: ready ? 'Sẵn sàng' : 'Chưa kiểm tra',
    canInstall: true, canVerify: true, canRepair: true }))
});

const directRelease: DesktopDirectRelease = {
  releaseId: 'direct-release', version: '1.0.2', declaredBuildNumber: 3,
  fileName: `VideoMaker-1.0.2-3-win-x64-${'a'.repeat(64)}.zip`,
  downloadUrl: `https://103.179.190.182:8443/apps/videomaker/VideoMaker-1.0.2-3-win-x64-${'a'.repeat(64)}.zip`,
  expectedSha256: 'a'.repeat(64), profile: 'Production', channel: 'Stable', platform: 'win-x64',
  modelBundleId: 'models-20261016-03', revision: 'direct-revision', publishedAtUtc: '2026-10-17T00:00:00Z',
  isMandatory: false, releaseNotes: 'Bản phát hành từ VPS.', catalogProtocol: 4, applicationDistributionProtocol: 3,
  minimumBootstrapperVersion: '1.2.0'
};

describe('App startup Setup gate', () => {
  let root: Root;
  let container: HTMLDivElement;
  const emit = async (message: HostMessage) => {
    await act(async () => bridge.listeners.forEach(listener => listener(message)));
  };

  beforeEach(async () => {
    vi.useFakeTimers();
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    bridge.listeners.clear();
    bridge.posts = [];
    bridge.sequence = 0;
    bridge.vietsubFeatureEnabled = [];
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
    await act(async () => root.render(<App />));
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it('renders the project screen before overlaying Setup, makes the background inert, then unlocks on READY', async () => {
    await emit({
      type: 'dashboard.state',
      payload: { ...dashboard, features: { ...dashboard.features, vietsubEnabled: true } }
    });
    expect(container.querySelector('.app-main')).not.toBeNull();
    expect(bridge.vietsubFeatureEnabled.at(-1)).toBe(false);
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(false) });

    expect(container.querySelector('[role="dialog"]')?.textContent).toContain('taphoatool cần bổ sung thành phần');
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    expect(container.querySelector('.sidebar')?.hasAttribute('inert')).toBe(true);
    expect(bridge.vietsubFeatureEnabled.at(-1)).toBe(false);

    await emit({ type: 'system.setup.completed', payload: setupSnapshot(true) });
    expect(container.querySelector('.startup-setup-overlay')).toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(false);
    expect(container.querySelector('.sidebar')?.hasAttribute('inert')).toBe(false);
    expect(bridge.vietsubFeatureEnabled.at(-1)).toBe(true);
  });

  it('keeps the application inert while the first native Setup response is delayed', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    expect(container.querySelector('.sidebar')?.hasAttribute('inert')).toBe(true);
    expect(container.querySelector('.startup-setup-dialog')?.textContent).toContain('Đang tải trạng thái');
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId,
      payload: { ...setupSnapshot(true), startupRequired: false } });
    expect(container.querySelector('.startup-setup-overlay')).toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(false);
  });

  it('keeps the gate visible when a native response omits a required component', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId,
      payload: { ...setupSnapshot(true), components: setupSnapshot(true).components.slice(0, 3) } });
    expect(container.querySelector('.startup-setup-overlay')).not.toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    expect(container.textContent).toContain('Trạng thái Setup chưa đầy đủ');
  });

  it('quarantines a malformed correlated status after an earlier READY instead of trusting old readiness', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    let get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(true) });
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(false);
    await act(async () => vi.advanceTimersByTime(6000));
    get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: { ...setupSnapshot(true), revision: 3, components: [] } });
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    expect(container.querySelector('.startup-setup-overlay')).not.toBeNull();
  });

  it('replaces an unanswered initial GET spinner with a recoverable status without opening the background', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    await act(async () => vi.advanceTimersByTime(16000));
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Chưa nhận trạng thái Setup');
    expect(container.querySelector('.startup-setup-loading .spin')).toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    const reload = [...container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.includes('Tải lại trạng thái'))!;
    await act(async () => reload.click());
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(true) });
    expect(container.querySelector('.startup-setup-overlay')).toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(false);
  });

  it('gives update its own dialog while retaining the component gate underneath', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(false) });
    await emit({ type: 'update.available', payload: { isUpdateAvailable: true, isMandatory: false,
      release: { releaseId: 'release', productName: 'VideoMaker', version: '1.2.0', buildNumber: 2,
        channel: 'Stable', platform: 'win-x64', publishedAtUtc: '2026-10-17T00:00:00Z', fileName: 'app.zip',
        downloadUrl: '/fixture.zip', sizeBytes: 1, sha256: 'a'.repeat(64) } } });
    expect(container.querySelectorAll('[role="dialog"]')).toHaveLength(1);
    expect(container.querySelector('.update-overlay')).not.toBeNull();
    expect(container.querySelector('.startup-setup-overlay')).toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    const dismiss = [...container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === 'Để sau')!;
    await act(async () => dismiss.click());
    expect(container.querySelectorAll('[role="dialog"]')).toHaveLength(1);
    expect(container.querySelector('.startup-setup-overlay')).not.toBeNull();
  });

  it('shows a direct catalog update and dismisses it without inventing a measured package size', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    await emit({ type: 'update.available', payload: { isUpdateAvailable: true, isMandatory: false, release: null, directRelease } });
    const modal = container.querySelector('.update-overlay')!;
    expect(modal.textContent).toContain('taphoatool 1.0.2');
    expect(modal.textContent).toContain('Build 3 · Stable');
    expect(modal.textContent).not.toMatch(/0\.0 KB|undefined|NaN| MB| KB/);
    expect(container.querySelector('.startup-setup-overlay')).toBeNull();
    const dismiss = [...modal.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === 'Để sau')!;
    await act(async () => dismiss.click());
    expect(bridge.posts.filter(post => post.type === 'update.dismiss')).toHaveLength(1);
    expect(container.querySelector('.update-overlay')).toBeNull();
    expect(container.querySelector('.startup-setup-overlay')).not.toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
  });

  it('shows standalone updates without registered Setup metadata and keeps the startup gate after dismissal', async () => {
    const { minimumBootstrapperVersion: _legacyVersion, ...facts } = directRelease;
    const release: DesktopStandaloneRelease = { ...facts, version: '1.0.3', declaredBuildNumber: 4,
      catalogProtocol: 5, minimumSetupVersion: '1.3.0', apiBaseUrl: 'https://taphoatool.com/' };
    await emit({ type: 'dashboard.state', payload: dashboard });
    await emit({ type: 'update.available', payload: { isUpdateAvailable: true, isMandatory: false, standaloneRelease: release } });
    const modal = container.querySelector('.update-overlay')!;
    expect(modal.textContent).toContain('taphoatool 1.0.3');
    expect(modal.textContent).toContain('Build 4 · Stable');
    expect(modal.textContent).not.toMatch(/undefined|NaN| MB| KB/);
    await act(async () => [...modal.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === 'Để sau')!.click());
    expect(container.querySelector('.update-overlay')).toBeNull();
    expect(container.querySelector('.startup-setup-overlay')).not.toBeNull();
  });

  it('keeps mandatory direct updates blocking, prevents duplicate apply and allows retry after native failure', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    await emit({ type: 'update.available', payload: { isUpdateAvailable: true, isMandatory: true, directRelease: { ...directRelease, isMandatory: true } } });
    const apply = () => container.querySelector<HTMLButtonElement>('.update-primary')!;
    expect(container.querySelector('.update-overlay')?.textContent).toContain('CẬP NHẬT BẮT BUỘC');
    expect([...container.querySelectorAll('.update-overlay button')].some(button => button.textContent === 'Để sau')).toBe(false);
    await act(async () => apply().click());
    expect(apply().disabled).toBe(true);
    await act(async () => apply().click());
    expect(bridge.posts.filter(post => post.type === 'update.apply')).toHaveLength(1);
    await emit({ type: 'update.failed', payload: { message: 'Không tải được file ZIP từ VPS.' } });
    expect(container.querySelector('.update-error')?.textContent).toContain('Không tải được file ZIP từ VPS.');
    expect(apply().disabled).toBe(false);
    await act(async () => apply().click());
    expect(bridge.posts.filter(post => post.type === 'update.apply')).toHaveLength(2);
    expect(container.querySelector('.update-error')).toBeNull();
  });

  it('returns from a direct update notice to the actual component readiness gate', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(false) });
    await emit({ type: 'update.available', payload: { isUpdateAvailable: true, isMandatory: false, directRelease } });
    expect(container.querySelectorAll('[role="dialog"]')).toHaveLength(1);
    expect(container.querySelector('.update-overlay')).not.toBeNull();
    await emit({ type: 'update.none' });
    expect(container.querySelector('.update-overlay')).toBeNull();
    expect(container.querySelector('.startup-setup-overlay')).not.toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
  });

  it.each([4, 5])('confirms catalog %i repair with unknown size and preserves native request correlation', async catalogProtocol => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(true) });
    await emit({ type: 'media.tools.install.available', payload: { releaseId: directRelease.releaseId, version: '1.0.2',
      buildNumber: 3, channel: 'Stable', sizeBytes: null, catalogProtocol } });
    const confirmation = container.querySelector('.confirmation-card')!;
    expect(confirmation.textContent).toContain('1.0.2 (build 3) từ VPS');
    expect(confirmation.textContent).not.toMatch(/0\.0 KB|undefined|NaN/);
    const install = confirmation.querySelector<HTMLButtonElement>('.confirmation-submit')!;
    expect(install.textContent?.trim()).toBe('Cài bộ xử lý video');
    expect(install.disabled).toBe(false);
    await act(async () => install.click());
    const request = bridge.posts.filter(post => post.type === 'media.tools.install').at(-1)!;
    expect(request).toBeDefined();
    await emit({ type: 'media.tools.install.progress', requestId: 'another-operation', payload: { stage: 'download', percent: 50, message: 'Wrong operation' } });
    expect(container.textContent).not.toContain('Wrong operation');
    await emit({ type: 'media.tools.install.progress', requestId: request.requestId, payload: { stage: 'download', percent: 51, message: 'Đang tải đúng gói sửa chữa.' } });
    expect(container.querySelector('.update-progress')?.textContent).toContain('Đang tải đúng gói sửa chữa.');
    await emit({ type: 'media.tools.install.failed', requestId: request.requestId, payload: { message: 'Kiểm tra gói thất bại.' } });
    expect(container.querySelector('.update-progress')).toBeNull();
    expect(container.textContent).toContain('Kiểm tra gói thất bại.');
  });

  it('does not reopen a global repair dialog from progress belonging to a failed startup repair', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(false) });
    const check = bridge.posts.filter(post => post.type === 'system.setup.check').at(-1)!;
    const repair: SetupSnapshot = { ...setupSnapshot(false), revision: 3,
      components: setupSnapshot(false).components.map(component => component.id === 'ocr'
        ? { ...component, state: 'REPAIR_REQUIRED', canInstall: false } : { ...component, state: 'READY' }),
      operation: { operationId: (check.payload as { operationId: string }).operationId, mode: 'check',
        state: 'Failed', sequence: 3, componentIds: ['media', 'ocr', 'qwen', 'piper'],
        allSelectedReady: false, allRequiredReady: false } };
    await emit({ type: 'system.setup.completed', payload: repair });
    const repairButton = container.querySelector<HTMLButtonElement>('.startup-setup-primary')!;
    expect(repairButton.textContent).toContain('Sửa bộ ứng dụng');
    await act(async () => repairButton.click());
    const install = bridge.posts.filter(post => post.type === 'media.tools.install').at(-1)!;
    await emit({ type: 'media.tools.install.progress', requestId: install.requestId,
      payload: { stage: 'download', percent: 10, message: 'Downloading repair fixture' } });
    await emit({ type: 'media.tools.install.failed', requestId: install.requestId,
      payload: { code: 'fixture_failure', message: 'Repair failed fixture' } });
    await emit({ type: 'media.tools.install.progress', requestId: install.requestId,
      payload: { stage: 'download', percent: 90, message: 'LATE REPAIR PROGRESS' } });
    await act(async () => vi.advanceTimersByTime(6000));
    const refresh = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: refresh.requestId,
      payload: { ...setupSnapshot(true), revision: 4 } });
    expect(container.querySelector('.startup-setup-overlay')).toBeNull();
    expect(container.querySelector('.media-install-card')).toBeNull();
    expect(container.textContent).not.toContain('LATE REPAIR PROGRESS');
  });

  it('does not toast a late Setup rejection after GET has already settled that request', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(false) });
    const check = bridge.posts.filter(post => post.type === 'system.setup.check').at(-1)!;
    await act(async () => vi.advanceTimersByTime(2500));
    const refresh = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: refresh.requestId,
      payload: { ...setupSnapshot(false), revision: 3,
        operation: { operationId: (check.payload as { operationId: string }).operationId, mode: 'check',
          state: 'Failed', componentIds: ['media', 'ocr', 'qwen', 'piper'], sequence: 3,
          allSelectedReady: false, allRequiredReady: false } } });
    await emit({ type: 'operation.error', requestId: check.requestId,
      error: { code: 'system_setup_busy', message: 'ERROR FROM SETTLED SETUP' } });
    expect(container.textContent).not.toContain('ERROR FROM SETTLED SETUP');
  });

  it('still reports uncorrelated preparation failures and tracks a normal repair by its initiating request', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard });
    const get = bridge.posts.filter(post => post.type === 'system.setup.get').at(-1)!;
    await emit({ type: 'system.setup.status', requestId: get.requestId, payload: setupSnapshot(true) });
    await emit({ type: 'media.tools.install.failed', payload: { code: 'prepare_failed', message: 'PREPARATION FAILURE' } });
    expect(container.textContent).toContain('PREPARATION FAILURE');
    await emit({ type: 'media.tools.install.available', payload: { releaseId: 'repair-release', productName: 'VideoMaker',
      version: '1.2.0', buildNumber: 2, channel: 'Stable', platform: 'win-x64', publishedAtUtc: '2026-10-17T00:00:00Z',
      fileName: 'app.zip', downloadUrl: '/fixture.zip', sizeBytes: 1, sha256: 'a'.repeat(64) } });
    const confirm = [...container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.includes('Cài bộ xử lý video'))!;
    await act(async () => confirm.click());
    const request = bridge.posts.filter(post => post.type === 'media.tools.install').at(-1)!;
    await emit({ type: 'media.tools.install.progress', requestId: request.requestId,
      payload: { stage: 'download', percent: 50, message: 'CURRENT REPAIR PROGRESS' } });
    expect(container.querySelector('.media-install-card')?.textContent).toContain('CURRENT REPAIR PROGRESS');
    await emit({ type: 'media.tools.install.failed', requestId: 'unrelated-repair',
      payload: { code: 'fixture', message: 'UNRELATED REPAIR FAILURE' } });
    expect(container.querySelector('.media-install-card')?.textContent).toContain('CURRENT REPAIR PROGRESS');
    expect(container.textContent).not.toContain('UNRELATED REPAIR FAILURE');
    await emit({ type: 'media.tools.install.failed', requestId: request.requestId,
      payload: { code: 'fixture', message: 'CURRENT REPAIR FAILURE' } });
    expect(container.querySelector('.media-install-card')).toBeNull();
    expect(container.textContent).toContain('CURRENT REPAIR FAILURE');
  });
});
