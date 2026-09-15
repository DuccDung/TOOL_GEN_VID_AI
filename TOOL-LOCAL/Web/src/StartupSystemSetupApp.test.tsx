// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { DashboardState, HostMessage } from './types';
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

describe('App startup Setup gate', () => {
  let root: Root;
  let container: HTMLDivElement;
  const emit = async (message: HostMessage) => {
    await act(async () => bridge.listeners.forEach(listener => listener(message)));
  };

  beforeEach(async () => {
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

    expect(container.querySelector('[role="dialog"]')?.textContent).toContain('VideoMaker cần bổ sung thành phần');
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    expect(container.querySelector('.sidebar')?.hasAttribute('inert')).toBe(true);
    expect(bridge.vietsubFeatureEnabled.at(-1)).toBe(false);

    await emit({ type: 'system.setup.completed', payload: setupSnapshot(true) });
    expect(container.querySelector('.startup-setup-overlay')).toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(false);
    expect(container.querySelector('.sidebar')?.hasAttribute('inert')).toBe(false);
    expect(bridge.vietsubFeatureEnabled.at(-1)).toBe(true);
  });
});
