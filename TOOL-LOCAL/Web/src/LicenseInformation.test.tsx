// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { postToHost } from './bridge';
import type { CurrentLicense, DashboardState, HostMessage, LicenseOffer } from './types';

const bridge = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>(), sequence: 0 }));
vi.mock('./bridge', () => ({
  isHosted: true,
  postToHost: vi.fn(() => `information-${++bridge.sequence}`),
  subscribeToHost: (callback: (message: HostMessage) => void) => {
    bridge.listeners.add(callback);
    return () => { bridge.listeners.delete(callback); };
  }
}));
vi.mock('./features/vietsub/useVietsubModule', () => ({
  useVietsubModule: () => ({ state: { enabled: false, loading: false, busy: false, projects: [], selectedProject: null, jobs: [] } })
}));

const license: CurrentLicense = {
  hasActiveLicense: true, currentDeviceActivated: true, maxActivatedDevices: 2, activeDeviceCount: 1,
  offlineGraceHours: 0, serverTimeUtc: '2026-09-18T00:00:00Z', heartbeatIntervalSeconds: 300,
  accessState: 'Active', planCode: 'MONTH', planName: 'Gói 30 ngày', status: 'Active',
  startsAtUtc: '2026-09-03T00:00:00Z', expiresAtUtc: '2026-10-03T00:00:00Z',
  assignedOrganizationName: 'Tổ chức được cấp'
};
const dashboard = (currentLicense: CurrentLicense | null = license, userId = 'user'): DashboardState => ({
  profile: { userId, email: 'user@example.test', displayName: 'Người dùng', accountStatus: 'Active', roles: [] },
  organizations: [], selectedOrganizationId: '', projects: [], selectedProject: null, models: [], assetLibrary: null,
  providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: false, message: 'Chưa kiểm tra công cụ media.', checkedAtUtc: '' }, generationRunning: false,
  features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false }, sceneFirstFrames: [], license: currentLicense
});
const offer: LicenseOffer = {
  licensePlanId: 'monthly', planCode: 'MONTH', name: 'Gói 30 ngày', description: 'Dành cho nhu cầu hằng tháng.',
  priceVnd: 199000, durationDays: 30, maxActivatedDevices: 2, marketingFeatures: ['Dịch phụ đề', 'Xuất video'],
  displayOrder: 1, organizationSeatAvailable: true
};

describe('license information entry points', () => {
  let container: HTMLDivElement;
  let root: ReturnType<typeof createRoot>;
  const emit = async (message: HostMessage) => {
    await act(async () => { for (const listener of bridge.listeners) listener(message); });
  };
  const dialog = () => container.querySelector<HTMLDialogElement>('.license-information-dialog')!;
  const button = (label: string, scope: ParentNode = container) => [...scope.querySelectorAll('button')].find(b => b.textContent?.trim() === label)!;
  const click = async (element: HTMLButtonElement) => { await act(async () => { element.focus(); element.click(); }); };
  const offersRequest = () => {
    const mocked = vi.mocked(postToHost).mock;
    const index = mocked.calls.map((call, i) => call[0] === 'license.offers.get' ? i : -1).filter(i => i >= 0).at(-1)!;
    return mocked.results[index].value as string;
  };
  const expectNoPayment = () => expect(vi.mocked(postToHost).mock.calls.some(call => call[0].startsWith('license.payment.'))).toBe(false);
  const confirmPersonalSetupReady = async () => {
    const mocked = vi.mocked(postToHost).mock;
    const index = mocked.calls.map((call, current) => call[0] === 'system.setup.get' ? current : -1).filter(current => current >= 0).at(-1)!;
    await emit({ type: 'system.setup.status', requestId: mocked.results[index].value as string,
      payload: { organizationId: '00000000-0000-0000-0000-000000000000', contextGeneration: 'personal-context', revision: 1,
        startupRequired: true, components: ['media', 'ocr', 'qwen', 'piper'].map(id => ({ id, name: id, version: '1',
          state: 'READY', message: 'Đã kiểm tra', canInstall: true, canVerify: true, canRepair: true })) } });
  };

  beforeEach(async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
      this.open = true; this.querySelector<HTMLButtonElement>('button')?.focus();
    });
    HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) { this.open = false; });
    Element.prototype.scrollTo = vi.fn();
    vi.clearAllMocks();
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
    await act(async () => root.render(<App />));
    await emit({ type: 'dashboard.state', payload: dashboard() });
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers();
  });

  it('Local opens translation and keeps every other menu visible and locked without sending commands', async () => {
    await emit({ type:'dashboard.state', payload:dashboard({ ...license, featureFlagsJson:'{"productScope":"vietsub","vietsubCloudEnabled":false}' }) });
    await confirmPersonalSetupReady();
    expect(container.querySelector('.topbar h1')?.textContent).toBe('Dịch phụ đề');
    expect(container.querySelector('.app-main')?.textContent).toContain('Dịch phụ đề chưa sẵn sàng');
    expect(container.querySelector('.vietsub-create-card')).toBeNull();
    expect(container.querySelector('.local-translation-setup summary')?.textContent).toBe('Thiết lập công cụ dịch');
    expect(container.querySelector('.local-translation-setup')?.textContent).not.toContain('Chọn tổ chức');
    const nav = container.querySelector('.sidebar-nav')!;
    expect(nav.textContent).toContain('Tạo Video Dài');
    expect(nav.textContent).toContain('Tải video Bilibili');
    expect(nav.textContent).toContain('API AI tổ chức');
    expect(nav.textContent).toContain('Đăng TikTok');
    expect(nav.textContent).toContain('Cài đặt');
    const available = nav.querySelectorAll('button:not([aria-disabled="true"])');
    expect(available).toHaveLength(1);
    expect(available[0].textContent).toBe('Dịch phụ đề');
    const callsBefore = vi.mocked(postToHost).mock.calls.length;
    for (const locked of nav.querySelectorAll<HTMLButtonElement>('button[aria-disabled="true"]')) {
      expect(locked.querySelector('.nav-lock-icon')).not.toBeNull();
      expect(locked.title).toContain('Gói Local');
      expect(document.getElementById(locked.getAttribute('aria-describedby')!)?.textContent).toContain('Dịch phụ đề');
      await click(locked);
    }
    const create = container.querySelector<HTMLButtonElement>('.new-video-button')!;
    expect(create.getAttribute('aria-disabled')).toBe('true');
    await click(create);
    expect(vi.mocked(postToHost).mock.calls).toHaveLength(callsBefore);
    expect(container.querySelector('.topbar h1')?.textContent).toBe('Dịch phụ đề');
    expect(container.querySelector('[role="dialog"]')).toBeNull();
    expect(button('Thông tin gói')).toBeDefined();
    expect(vi.mocked(postToHost).mock.calls.some(call => call[0] === 'project.create' || call[0].startsWith('generation.'))).toBe(false);
  });

  it('Local closes a previously open settings page and restores menus when the effective plan changes', async () => {
    await click(container.querySelector<HTMLButtonElement>('.sidebar-nav [aria-label="Cài đặt"]')!);
    expect(container.querySelector('.topbar h1')?.textContent).toBe('Cài đặt');
    await emit({ type: 'license.activated', payload: { ...license, featureFlagsJson: '{"productScope":"vietsub"}' } });
    await confirmPersonalSetupReady();
    expect(container.querySelector('.topbar h1')?.textContent).toBe('Dịch phụ đề');
    expect(container.querySelector('.sidebar-nav [aria-label="Cài đặt"]')?.getAttribute('aria-disabled')).toBe('true');
    await click(button('Thông tin gói'));
    expect(dialog().open).toBe(true);
    await click(dialog().querySelector<HTMLButtonElement>('[aria-label="Đóng thông tin gói"]')!);
    expect(container.querySelector('.sidebar [aria-label="Đăng xuất"]')?.getAttribute('aria-disabled')).toBeNull();
    await emit({ type: 'dashboard.state', payload: dashboard(license) });
    expect(container.querySelector('.sidebar-nav [aria-label="Cài đặt"]')?.getAttribute('aria-disabled')).toBeNull();
    expect(container.querySelector('.new-video-button')?.getAttribute('aria-disabled')).toBeNull();
    await click(container.querySelector<HTMLButtonElement>('.sidebar-nav [aria-label="Cài đặt"]')!);
    expect(container.querySelector('.topbar h1')?.textContent).toBe('Cài đặt');
  });

  it('translation Cloud plans keep their existing navigation policy', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard({ ...license, featureFlagsJson: '{"productScope":"vietsub","vietsubCloudEnabled":true}' }) });
    expect(container.querySelector('.sidebar-nav [aria-label="Cài đặt"]')?.getAttribute('aria-disabled')).toBeNull();
    expect(container.querySelector('.sidebar-nav [aria-label="Tạo Video Dài"]')).toBeNull();
    expect(container.querySelector('.new-video-button')).toBeNull();
  });

  it('opens account details from the sidebar and restores focus on Escape/cancel without payment requests', async () => {
    const trigger = button('Thông tin gói');
    await click(trigger);
    expect(dialog().open).toBe(true);
    expect(dialog().textContent).toContain('Gói 30 ngày');
    expect(dialog().textContent).toContain('Đang hoạt động');
    expect(dialog().textContent).toContain('3 tháng 10, 2026');
    expect(dialog().textContent).toContain('1 / 2 thiết bị');
    expect(dialog().textContent).toContain('Tổ chức được cấp');
    expect(postToHost).not.toHaveBeenCalledWith('license.offers.get');
    expectNoPayment();
    const close = dialog().querySelector<HTMLButtonElement>('[aria-label="Đóng thông tin gói"]')!;
    const last = button('Xem các gói nâng cấp', dialog());
    await act(async () => {
      last.focus(); last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    });
    expect(document.activeElement).toBe(close);
    await act(async () => close.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })));
    expect(document.activeElement).toBe(last);
    await act(async () => dialog().dispatchEvent(new Event('cancel', { bubbles: false, cancelable: true })));
    expect(dialog()).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('loads catalog from the upgrade button and displays server prices, benefits and availability in order', async () => {
    await click(button('Nâng cấp gói'));
    expect(dialog().textContent).toContain('Đang tải');
    const id = offersRequest();
    // An unrelated dashboard refresh must not discard the catalog request.
    await emit({ type: 'dashboard.state', payload: dashboard() });
    await emit({ type: 'license.offers', requestId: id, payload: [
      { ...offer, licensePlanId: 'yearly', planCode: 'YEAR', name: 'Gói 365 ngày', displayOrder: 2, durationDays: 365, priceVnd: 1499000, organizationSeatAvailable: false }, offer
    ] });
    const cards = [...dialog().querySelectorAll('article')];
    expect(cards).toHaveLength(2);
    expect(cards[0].textContent).toContain('199.000');
    expect(cards[0].textContent).toContain('Gói hiện tại');
    expect(cards[0].textContent).toContain('Dịch phụ đề');
    expect(cards[0].textContent).toContain('Tối đa 2 thiết bị');
    expect(cards[0].textContent).toContain('Đang còn chỗ');
    expect(cards[1].textContent).toContain('Tạm hết chỗ');
    expectNoPayment();
  });

  it('switches between details and catalog inside the same dismissible dialog', async () => {
    await click(button('Thông tin gói'));
    await click(button('Xem các gói nâng cấp', dialog()));
    await emit({ type: 'license.offers', requestId: offersRequest(), payload: [offer] });
    await click(button('Xem gói hiện tại', dialog()));
    expect(dialog().textContent).toContain('Ngày hết hạn');
    await click(button('Đóng', dialog()));
    expect(dialog()).toBeNull();
    expectNoPayment();
  });

  it('shows an empty catalog with reload instead of an endless spinner or made-up plans', async () => {
    await click(button('Nâng cấp gói'));
    await emit({ type: 'license.offers', requestId: offersRequest(), payload: [] });
    expect(dialog().textContent).toContain('Hiện chưa có gói sử dụng');
    expect(dialog().textContent).not.toContain('Đang tải');
    await act(async () => { const retry = button('Kiểm tra lại', dialog()); retry.click(); retry.click(); });
    expect(vi.mocked(postToHost).mock.calls.filter(call => call[0] === 'license.offers.get')).toHaveLength(2);
    expectNoPayment();
  });

  it('keeps server errors in the dialog, supports retry and ignores errors from a closed request', async () => {
    await click(button('Nâng cấp gói'));
    const oldId = offersRequest();
    await click(button('Đóng', dialog()));
    await click(button('Nâng cấp gói'));
    const newId = offersRequest();
    await emit({ type: 'operation.error', requestId: oldId, error: { code: 'network_error', message: 'Lỗi cũ' } });
    expect(dialog().textContent).toContain('Đang tải');
    expect(container.textContent).not.toContain('Lỗi cũ');
    await emit({ type: 'operation.error', requestId: newId, error: { code: 'payments_disabled', message: 'Thanh toán đang tạm đóng.' } });
    expect(dialog().querySelector('[role="alert"]')?.textContent).toContain('Thanh toán đang tạm đóng.');
    expect(container.querySelector('.toast-stack')?.textContent).toBe('');
    await click(button('Kiểm tra lại', dialog()));
    await emit({ type: 'license.offers', requestId: offersRequest(), payload: [offer] });
    expect(dialog().querySelectorAll('article')).toHaveLength(1);
    expectNoPayment();
  });

  it('shows unavailable package sales as information in the upgrade dialog without a payment request', async () => {
    await click(button('Nâng cấp gói'));
    await emit({ type: 'operation.error', requestId: offersRequest(),
      error: { code: 'payments_unavailable', message: 'Thanh toán đang tạm ngừng. Vui lòng thử lại sau.' } });
    expect(dialog().textContent).toContain('Hiện chưa có gói sử dụng');
    expect(dialog().textContent).not.toContain('Thanh toán đang tạm ngừng');
    expect(dialog().querySelector('[role="alert"]')).toBeNull();
    await click(button('Kiểm tra lại', dialog()));
    await emit({ type: 'license.offers', requestId: offersRequest(), payload: [offer] });
    expect(dialog().querySelectorAll('article')).toHaveLength(1);
    expectNoPayment();
  });

  it('times out read-only requests and ignores stale success after retry', async () => {
    vi.useFakeTimers();
    await click(button('Nâng cấp gói'));
    const oldId = offersRequest();
    await act(async () => vi.advanceTimersByTime(30000));
    expect(dialog().textContent).toContain('Máy chủ chưa phản hồi');
    await click(button('Kiểm tra lại', dialog()));
    await emit({ type: 'license.offers', requestId: oldId, payload: [offer] });
    expect(dialog().textContent).toContain('Đang tải');
    await emit({ type: 'license.offers', requestId: offersRequest(), payload: [] });
    expect(dialog().textContent).toContain('Hiện chưa có gói sử dụng');
  });

  it('closes when account changes and never shows the previous response in the new account', async () => {
    await click(button('Nâng cấp gói'));
    const oldId = offersRequest();
    await emit({ type: 'dashboard.state', payload: dashboard(license, 'another-user') });
    expect(dialog()).toBeNull();
    await click(button('Nâng cấp gói'));
    await emit({ type: 'license.offers', requestId: oldId, payload: [offer] });
    expect(dialog().textContent).toContain('Đang tải');
    await emit({ type: 'license.offers', requestId: offersRequest(), payload: [] });
    expect(dialog().querySelectorAll('article')).toHaveLength(0);
  });

  it('yields to the license gate when access is revoked', async () => {
    await click(button('Nâng cấp gói'));
    const id = offersRequest();
    await emit({ type: 'license.invalidated', payload: { license: { ...license, hasActiveLicense: false, accessState: 'Revoked' }, message: 'Đã thu hồi' } });
    expect(dialog()).toBeNull();
    expect(container.querySelector('.license-gate-card')?.textContent).toContain('Gói sử dụng đã bị thu hồi');
    await emit({ type: 'license.offers', requestId: id, payload: [offer] });
    expect(dialog()).toBeNull();
    expectNoPayment();
  });

  it('distinguishes missing license information from an active plan without an expiry date', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard(null) });
    await click(button('Thông tin gói'));
    expect(dialog().textContent).toContain('Chưa nhận được thông tin gói');
    expect(dialog().textContent).not.toContain('Không giới hạn');
    await emit({ type: 'dashboard.state', payload: dashboard({ ...license, expiresAtUtc: null, startsAtUtc: null }) });
    expect(dialog().textContent).toContain('Không giới hạn');
    expect(dialog().querySelector('dl')?.textContent).toContain('Chưa có thông tin');
  });
});
