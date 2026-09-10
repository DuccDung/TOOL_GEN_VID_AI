// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { postToHost } from './bridge';
import type { CurrentLicense, DashboardState, HostMessage } from './types';

const bridge = vi.hoisted(() => ({ receive: (_message: HostMessage) => {}, sequence: 0 }));
vi.mock('./bridge', () => ({
  isHosted: true,
  postToHost: vi.fn(() => `request-${++bridge.sequence}`),
  subscribeToHost: (callback: typeof bridge.receive) => { bridge.receive = callback; return () => {}; }
}));
vi.mock('./features/vietsub/useVietsubModule', () => ({
  useVietsubModule: () => ({ state: { enabled: false, loading: false, busy: false } })
}));

const active: CurrentLicense = {
  hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 1, activeDeviceCount: 1,
  offlineGraceHours: 0, serverTimeUtc: '2026-09-10T00:00:00Z', heartbeatIntervalSeconds: 300,
  leaseExpiresAtUtc: '2026-09-10T00:15:00Z', accessState: 'Active'
};
const locked: CurrentLicense = { ...active, leaseExpiresAtUtc: null, accessState: 'SessionLimit',
  accessReasonCode: 'concurrent_session_limit', accessMessage: 'Gói đã đạt số phiên chạy đồng thời tối đa.' };
const dashboard = (license: CurrentLicense): DashboardState => ({
  profile: { userId: 'user', email: 'user@example.test', displayName: 'Người dùng', accountStatus: 'Active', roles: [] },
  organizations: [], selectedOrganizationId: '', projects: [], selectedProject: null, models: [], assetLibrary: null,
  providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: false, message: 'Chưa kiểm tra công cụ media.', checkedAtUtc: '' }, generationRunning: false,
  features: { vietsubEnabled: false, speechSynchronizationEnabled: false }, sceneFirstFrames: [], license
});

describe('license access recovery', () => {
  let container: HTMLDivElement;
  let root: ReturnType<typeof createRoot>;
  const emit = async (message: HostMessage) => { await act(async () => bridge.receive(message)); };
  const dialog = () => container.querySelector<HTMLElement>('[role="dialog"]')!;
  const button = (label: string) => [...dialog().querySelectorAll('button')].find(b => b.textContent === label)!;
  beforeEach(async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    Element.prototype.scrollTo = vi.fn();
    vi.clearAllMocks();
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
    await act(async () => root.render(<App />));
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

  it('shows the session error even before loading finishes, with logout and no payment calls', async () => {
    await emit({ type: 'license.invalidated', payload: { message: locked.accessMessage, license: locked } });
    expect(dialog().textContent).toContain('Đã đạt giới hạn phiên sử dụng');
    expect(dialog().textContent).toContain(locked.accessMessage);
    expect(dialog().textContent).not.toContain('Chọn gói để bắt đầu');
    expect(button('Đăng xuất').disabled).toBe(false);
    expect(button('Kiểm tra lại').disabled).toBe(false);
    expect(postToHost).toHaveBeenCalledTimes(1); // app.ready only: no automatic rejection loop.
    expect(dialog().contains(document.activeElement)).toBe(true);
    await act(async () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })));
    expect(dialog()).not.toBeNull();
  });

  it('keeps logout available while checking again, deduplicates clicks and allows retry after failure', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard(locked) });
    await act(async () => button('Kiểm tra lại').click());
    expect(postToHost).toHaveBeenLastCalledWith('license.refresh', undefined);
    expect(button('Đang kiểm tra…').disabled).toBe(true);
    expect(button('Đăng xuất').disabled).toBe(false);
    const logout = button('Đăng xuất');
    await act(async () => { logout.click(); logout.click(); });
    expect(vi.mocked(postToHost).mock.calls.filter(call => call[0] === 'auth.logout')).toHaveLength(1);
    const logoutId = vi.mocked(postToHost).mock.results.at(-1)!.value as string;
    expect(button('Đang đăng xuất…').disabled).toBe(true);
    await emit({ type: 'operation.error', requestId: logoutId, error: { code: 'network_error', message: 'Không thể kết nối. Hãy thử đăng xuất lại.' } });
    expect(dialog().querySelector('[role="alert"]')?.textContent).toContain('Không thể kết nối');
    await act(async () => button('Đăng xuất').click());
    expect(vi.mocked(postToHost).mock.calls.filter(call => call[0] === 'auth.logout')).toHaveLength(2);
  });

  it('handles a running session losing access and unlocks only on a successful check', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard(active) });
    expect(dialog()).toBeNull();
    await emit({ type: 'license.invalidated', payload: { message: locked.accessMessage, license: locked } });
    expect(dialog()).not.toBeNull();
    await act(async () => button('Kiểm tra lại').click());
    let id = vi.mocked(postToHost).mock.results.at(-1)!.value as string;
    await emit({ type: 'dashboard.state', requestId: id, payload: dashboard(locked) });
    expect(button('Kiểm tra lại').disabled).toBe(false);
    await act(async () => button('Kiểm tra lại').click());
    id = vi.mocked(postToHost).mock.results.at(-1)!.value as string;
    await emit({ type: 'dashboard.state', requestId: id, payload: dashboard(active) });
    expect(dialog()).toBeNull();
  });

  it.each([
    ['DeviceLimit', 'device_limit_reached', 'Đã đạt giới hạn thiết bị'],
    ['Unavailable', 'session_unavailable', 'Chưa thể xác minh quyền sử dụng']
  ] as const)('shows %s as an access error with logout', async (accessState, accessReasonCode, title) => {
    await emit({ type: 'dashboard.state', payload: dashboard({ ...locked, accessState, accessReasonCode }) });
    expect(dialog().textContent).toContain(title);
    expect(button('Đăng xuất').disabled).toBe(false);
  });
});
