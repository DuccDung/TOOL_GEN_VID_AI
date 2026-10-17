// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { postToHost } from './bridge';
import type { CurrentLicense, DashboardState, HostMessage, LicenseOffer, LicensePaymentCheckout } from './types';

const bridge = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>(), sequence: 0 }));
vi.mock('./bridge', () => ({
  isHosted: true,
  postToHost: vi.fn(() => 'catalog-' + ++bridge.sequence),
  subscribeToHost: (callback: (message: HostMessage) => void) => {
    bridge.listeners.add(callback);
    return () => { bridge.listeners.delete(callback); };
  }
}));
vi.mock('./features/vietsub/useVietsubModule', () => ({
  useVietsubModule: () => ({ state: { enabled: false, loading: false, busy: false } })
}));

const missing: CurrentLicense = {
  hasActiveLicense: false, currentDeviceActivated: false, maxActivatedDevices: 1, activeDeviceCount: 0,
  offlineGraceHours: 0, serverTimeUtc: '2026-10-13T00:00:00Z', heartbeatIntervalSeconds: 300,
  leaseExpiresAtUtc: null, accessState: 'Missing', accessMessage: 'Tài khoản chưa có gói sử dụng.'
};
const dashboard = (license = missing, userId = 'user'): DashboardState => ({
  profile: { userId, email: 'user@example.test', displayName: 'Người dùng', accountStatus: 'Active', roles: [] },
  organizations: [], selectedOrganizationId: '', projects: [], selectedProject: null, models: [], assetLibrary: null,
  providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: false, message: 'Chưa kiểm tra công cụ media.', checkedAtUtc: '' }, generationRunning: false,
  features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false }, sceneFirstFrames: [], license
});
const offer: LicenseOffer = {
  licensePlanId: 'monthly', planCode: 'MONTH', name: 'Gói 30 ngày', description: 'Gói thử nghiệm.',
  priceVnd: 199000, durationDays: 30, maxActivatedDevices: 1, marketingFeatures: ['Xuất video'],
  displayOrder: 1, organizationSeatAvailable: true
};
const pendingPayment: LicensePaymentCheckout = {
  orderCode: 'TESTORDER', transferCode: 'VMTESTORDER', planCode: 'MONTH', planName: 'Gói 30 ngày',
  durationDays: 30, amountVnd: 199000, receiverBankCode: 'VCB', receiverAccountNumber: '11111111',
  receiverAccountName: 'TEST HOLDER', transferContent: 'VMTESTORDER', qrImageUrl: '/test-qr.png',
  status: 'Pending', createdAtUtc: '2026-10-13T00:00:00Z', expiresAtUtc: '2026-10-13T00:15:00Z',
  serverTimeUtc: '2026-10-13T00:00:00Z', reusedExistingPayment: true, isPaid: false, isFulfilled: false, isExpired: false
};

describe('customer license catalog states', () => {
  let container: HTMLDivElement;
  let root: ReturnType<typeof createRoot>;
  const emit = async (message: HostMessage) => {
    await act(async () => { for (const listener of bridge.listeners) listener(message); });
  };
  const gate = () => container.querySelector<HTMLElement>('.license-gate-card')!;
  const button = (label: string) => [...gate().querySelectorAll('button')].find(b => b.textContent?.trim() === label)!;
  const requestIds = (type: string) => {
    const mock = vi.mocked(postToHost).mock;
    return mock.calls.flatMap((call, index) => call[0] === type ? [mock.results[index].value as string] : []);
  };
  const latestOffersRequest = () => requestIds('license.offers.get').at(-1)!;
  const expectLocked = () => {
    expect(gate()).not.toBeNull();
    expect(gate().getAttribute('aria-modal')).toBe('true');
  };
  const expectNoBilling = () => expect(requestIds('license.payment.create')).toHaveLength(0);

  beforeEach(async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    Element.prototype.scrollTo = vi.fn();
    vi.clearAllMocks();
    container = document.createElement('div');
    document.body.append(container);
    root = createRoot(container);
    await act(async () => root.render(<App />));
    await emit({ type: 'dashboard.state', payload: dashboard() });
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers();
  });

  it('shows loading while checking, keeps logout available and does not claim there are no offers yet', () => {
    expect(gate().textContent).toContain('Đang tải các gói sử dụng');
    expect(gate().textContent).not.toContain('Hiện chưa có gói sử dụng');
    expect(button('Đang kiểm tra…').disabled).toBe(true);
    expect(button('Đăng xuất').disabled).toBe(false);
    expect(requestIds('license.offers.get')).toHaveLength(1);
    expect(requestIds('license.payment.current.get')).toHaveLength(1);
    expectNoBilling(); expectLocked();
  });

  it.each(['empty', 'payments_unavailable'] as const)('shows a neutral empty state for %s without a red error', async kind => {
    await emit(kind === 'empty'
      ? { type: 'license.offers', requestId: latestOffersRequest(), payload: [] }
      : { type: 'operation.error', requestId: latestOffersRequest(), error: {
        code: 'payments_unavailable', message: 'Thanh toán đang tạm ngừng. Vui lòng thử lại sau.' } });
    expect(gate().textContent).toContain('Hiện chưa có gói sử dụng');
    expect(gate().textContent).toContain('Hệ thống hiện chưa cung cấp gói sử dụng.');
    expect(gate().textContent).toContain('Vui lòng quay lại sau hoặc liên hệ hỗ trợ.');
    expect(gate().textContent).not.toContain('Thanh toán đang tạm ngừng');
    expect(gate().querySelector('[role="alert"]')).toBeNull();
    expect(gate().querySelector('.license-plan-option')).toBeNull();
    expect(gate().querySelector('[aria-busy="true"]')).toBeNull();
    expect(button('Kiểm tra lại').disabled).toBe(false);
    expect(container.querySelector('.toast-stack')?.textContent).toBe('');
    expectNoBilling(); expectLocked();
  });

  it.each(['network_error', 'unexpected_error', 'organization_access_denied'])('keeps %s distinct from a catalog that is unavailable', async code => {
    await emit({ type: 'operation.error', requestId: latestOffersRequest(), error: { code, message: 'Không thể tải dữ liệu. Hãy thử lại.' } });
    expect(gate().querySelector('[role="alert"]')?.textContent).toContain('Chưa thể tải danh sách gói');
    expect(gate().textContent).toContain('Không thể tải dữ liệu. Hãy thử lại.');
    expect(gate().textContent).not.toContain('Hiện chưa có gói sử dụng');
    expect(button('Kiểm tra lại').disabled).toBe(false);
    expectNoBilling(); expectLocked();
  });

  it('retries only read requests, deduplicates double clicks and returns to real plan selection', async () => {
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: [] });
    await act(async () => { const retry = button('Kiểm tra lại'); retry.click(); retry.click(); });
    expect(requestIds('license.offers.get')).toHaveLength(2);
    expect(requestIds('license.payment.current.get')).toHaveLength(2);
    expect(button('Đang kiểm tra…').disabled).toBe(true);
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: [offer] });
    expect(gate().textContent).toContain('Chọn gói để bắt đầu');
    expect(gate().textContent).toContain('Gói 30 ngày');
    expect(button('Chọn gói này').disabled).toBe(false);
    expect(gate().textContent).not.toContain('Hiện chưa có gói sử dụng');
    expectNoBilling(); expectLocked();
  });

  it('buys Local directly without organization capacity and does not buy merely by viewing', async () => {
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: [
      { ...offer, productScope: 'vietsub', vietsubCloudEnabled: false, organizationPoolName: null, availableOrganizationSeats: null }
    ] });
    expect(gate().textContent).toContain('Kích hoạt trực tiếp · Không cần tổ chức');
    expect(gate().textContent).not.toContain('Cụm tổ chức');
    expect(button('Chọn gói này').disabled).toBe(false);
    expectNoBilling();
    await act(async () => button('Chọn gói này').click());
    expect(requestIds('license.payment.create')).toHaveLength(1);
  });

  it('times out then ignores late success and error from old requests while a new check is pending', async () => {
    vi.useFakeTimers();
    // Restart under the fake clock so the catalog deadline is controlled.
    await emit({ type: 'dashboard.state', payload: dashboard(missing, 'timed-user') });
    const timedRequest = latestOffersRequest();
    await act(async () => vi.advanceTimersByTime(30000));
    expect(gate().textContent).toContain('Máy chủ chưa phản hồi');
    await act(async () => button('Kiểm tra lại').click());
    const nextRequest = latestOffersRequest();
    await emit({ type: 'license.offers', requestId: timedRequest, payload: [offer] });
    expect(gate().textContent).toContain('Đang tải các gói sử dụng');
    await emit({ type: 'operation.error', requestId: timedRequest, error: { code: 'payments_unavailable', message: 'Lỗi cũ' } });
    expect(gate().textContent).not.toContain('Lỗi cũ');
    await emit({ type: 'license.offers', requestId: nextRequest, payload: [] });
    expect(gate().textContent).toContain('Hiện chưa có gói sử dụng');
    expectNoBilling(); expectLocked();
  });

  it('shows Local rights and expected purchase dates before creating a payment', async () => {
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: [{ ...offer,
      productScope: 'vietsub', vietsubCloudEnabled: false, marketingFeatures: [], expectedStartsAtUtc: '2026-10-20T00:00:00Z',
      expectedExpiresAtUtc: '2026-11-19T00:00:00Z' }] });
    expect(gate().textContent).toContain('Chỉ tool dịch · Local');
    expect(gate().textContent).toContain('Dự kiến bắt đầu kỳ mua');
    expect(gate().textContent).not.toContain('Sử dụng đầy đủ quy trình tạo và dựng video');
    expectNoBilling(); expectLocked();
  });

  it('a fulfilled scheduled payment shows its dates and never remains busy waiting for immediate activation', async () => {
    await emit({ type: 'license.payment.current', requestId: requestIds('license.payment.current.get').at(-1)!,
      payload: { payment: pendingPayment } });
    await act(async () => button('Tôi đã thanh toán').click());
    await emit({ type: 'license.payment.status', requestId: requestIds('license.payment.status').at(-1)!, payload: {
      ...pendingPayment, status: 'Fulfilled', isPaid: true, isFulfilled: true, isScheduled: true,
      provisioningStatus: 'Scheduled', startsAtUtc: '2026-10-20T00:00:00Z', licenseExpiresAtUtc: '2026-11-19T00:00:00Z',
      message: 'Đã thanh toán và xếp lịch.' } });
    expect(gate().textContent).toContain('Chờ ngày hiệu lực');
    expect(gate().textContent).toContain('Đã xếp lịch sử dụng');
    expect(gate().textContent).not.toContain('Đang kích hoạt');
    expect(gate().querySelector('.license-back-button')?.hasAttribute('disabled')).toBe(false);
    expectLocked();
  });

  it('does not apply the old catalog or pending order after account changes', async () => {
    const oldOffers = latestOffersRequest();
    const oldCurrent = requestIds('license.payment.current.get').at(-1)!;
    await emit({ type: 'dashboard.state', payload: dashboard(missing, 'new-user') });
    const newOffers = latestOffersRequest();
    await emit({ type: 'license.offers', requestId: oldOffers, payload: [offer] });
    await emit({ type: 'license.payment.current', requestId: oldCurrent, payload: { payment: pendingPayment } });
    expect(gate().textContent).toContain('Đang tải các gói sử dụng');
    expect(gate().querySelector('.license-checkout-view')).toBeNull();
    await emit({ type: 'license.offers', requestId: newOffers, payload: [] });
    expect(gate().textContent).toContain('Hiện chưa có gói sử dụng');
    expectLocked();
  });

  it('still restores an existing checkout when new package sales are disabled', async () => {
    await emit({ type: 'operation.error', requestId: latestOffersRequest(), error: {
      code: 'payments_unavailable', message: 'Thanh toán đang tạm ngừng.' } });
    await emit({ type: 'license.payment.current', requestId: requestIds('license.payment.current.get').at(-1)!,
      payload: { payment: pendingPayment } });
    expect(gate().textContent).toContain('Thanh toán Gói 30 ngày');
    expect(gate().querySelector('.license-checkout-view')).not.toBeNull();
    expect(gate().textContent).not.toContain('Hiện chưa có gói sử dụng');
    expectNoBilling(); expectLocked();
  });

  it('returns to the empty state when sales become unavailable after selecting a plan', async () => {
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: [offer] });
    await act(async () => button('Chọn gói này').click());
    await emit({ type: 'operation.error', requestId: requestIds('license.payment.create').at(-1)!,
      error: { code: 'payments_unavailable', message: 'Thanh toán đang tạm ngừng.' } });
    expect(gate().textContent).toContain('Hiện chưa có gói sử dụng');
    expect(gate().querySelector('[role="alert"]')).toBeNull();
    expect(requestIds('license.payment.create')).toHaveLength(1);
    expectLocked();
  });

  it('keeps the same neutral state for an expired license and never unlocks on empty results', async () => {
    await emit({ type: 'dashboard.state', payload: dashboard({ ...missing, accessState: 'Expired' }) });
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: [] });
    expect(gate().textContent).toContain('Hiện chưa có gói sử dụng');
    expectNoBilling(); expectLocked();
  });

  it('unlocks only when an active license arrives and ignores catalog responses from that earlier gate', async () => {
    const oldOffers = latestOffersRequest();
    await emit({ type: 'dashboard.state', payload: dashboard({ ...missing,
      hasActiveLicense: true, currentDeviceActivated: true, accessState: 'Active' }) });
    expect(gate()).toBeNull();
    await emit({ type: 'operation.error', requestId: oldOffers,
      error: { code: 'payments_unavailable', message: 'Thanh toán đang tạm ngừng.' } });
    expect(gate()).toBeNull();
    expect(container.querySelector('.toast-stack')?.textContent).toBe('');
  });

  it('keeps focus inside the empty modal, blocks Escape and supports logout', async () => {
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: [] });
    await act(async () => {
      button('Kiểm tra lại').focus();
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    });
    expect(document.activeElement).toBe(button('Đăng xuất'));
    await act(async () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })));
    expectLocked();
    await act(async () => button('Đăng xuất').click());
    expect(requestIds('auth.logout')).toHaveLength(1);
    expect(button('Đang đăng xuất…').disabled).toBe(true);
    expectNoBilling();
  });

  it('rejects malformed catalog payloads as errors instead of an empty catalog', async () => {
    await emit({ type: 'license.offers', requestId: latestOffersRequest(), payload: { unexpected: true } });
    expect(gate().textContent).toContain('Danh sách gói chưa hợp lệ');
    expect(gate().querySelector('[role="alert"]')).not.toBeNull();
    expect(gate().textContent).not.toContain('Hiện chưa có gói sử dụng');
    expectLocked();
  });
});
