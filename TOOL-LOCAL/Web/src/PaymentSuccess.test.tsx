// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import { postToHost } from './bridge';
import type { CurrentLicense, DashboardState, HostMessage, LicensePaymentCheckout, LicensePaymentStatus } from './types';

const bridge = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>(), sequence: 0 }));
vi.mock('./bridge', () => ({
  isHosted: true,
  postToHost: vi.fn(() => `payment-${++bridge.sequence}`),
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
  offlineGraceHours: 0, serverTimeUtc: '2026-10-15T00:00:00Z', heartbeatIntervalSeconds: 300,
  accessState: 'Missing', leaseExpiresAtUtc: null
};
const active: CurrentLicense = { ...missing, hasActiveLicense: true, currentDeviceActivated: true,
  accessState: 'Active', planCode: 'MONTH', planName: 'Gói 30 ngày',
  startsAtUtc: '2026-10-15T00:00:00Z', expiresAtUtc: '2026-11-14T00:00:00Z' };
const dashboard = (license = missing, userId = 'user'): DashboardState => ({
  profile: { userId, email: 'user@example.test', displayName: 'Khách hàng', accountStatus: 'Active', roles: [] },
  organizations: [], selectedOrganizationId: '', projects: [], selectedProject: null, models: [], assetLibrary: null,
  providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: false, message: 'Fixture media.', checkedAtUtc: '' }, generationRunning: false,
  features: { vietsubEnabled: false, speechSynchronizationEnabled: false, tikTokEnabled: false }, sceneFirstFrames: [], license
});
const checkout: LicensePaymentCheckout = {
  orderCode: 'VMO123456', transferCode: 'VM123456', planCode: 'MONTH', planName: 'Gói 30 ngày',
  durationDays: 30, amountVnd: 70000, receiverBankCode: 'VCB', receiverAccountNumber: '11111111',
  receiverAccountName: 'TEST HOLDER', transferContent: 'VM123456', qrImageUrl: '/test-qr.png',
  status: 'Pending', createdAtUtc: '2026-10-15T00:00:00Z', expiresAtUtc: '2026-10-15T00:15:00Z',
  serverTimeUtc: '2026-10-15T00:00:00Z', reusedExistingPayment: false, isPaid: false, isFulfilled: false, isExpired: false
};
const fulfilled: LicensePaymentStatus = { ...checkout, status: 'Fulfilled', isPaid: true, isFulfilled: true,
  startsAtUtc: active.startsAtUtc, licenseExpiresAtUtc: active.expiresAtUtc };

describe('payment success before entering the application', () => {
  let container: HTMLDivElement;
  let root: ReturnType<typeof createRoot>;
  const emit = async (message: HostMessage) => {
    await act(async () => { for (const listener of bridge.listeners) listener(message); });
  };
  const success = () => container.querySelector<HTMLDialogElement>('.payment-success-dialog');
  const button = (label: string) => [...container.querySelectorAll<HTMLButtonElement>('button')]
    .find(element => element.textContent?.trim() === label)!;
  const requestIds = (type: string) => {
    const mock = vi.mocked(postToHost).mock;
    return mock.calls.flatMap((call, index) => call[0] === type ? [mock.results[index].value as string] : []);
  };
  const restoreCheckout = async () => emit({ type: 'license.payment.current',
    requestId: requestIds('license.payment.current.get').at(-1)!, payload: { payment: checkout } });
  const checkStatus = async (status = fulfilled) => {
    await act(async () => button('Tôi đã thanh toán').click());
    const requestId = requestIds('license.payment.status').at(-1)!;
    await emit({ type: 'license.payment.status', requestId, payload: status });
    return requestId;
  };

  beforeEach(async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    HTMLDialogElement.prototype.showModal = vi.fn(function (this: HTMLDialogElement) {
      this.open = true; (this.querySelector<HTMLButtonElement>('button:not(:disabled)') ?? this).focus();
    });
    HTMLDialogElement.prototype.close = vi.fn(function (this: HTMLDialogElement) { this.open = false; });
    Element.prototype.scrollTo = vi.fn();
    vi.clearAllMocks();
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
    await act(async () => root.render(<App />));
    await emit({ type: 'dashboard.state', payload: dashboard() });
  });
  afterEach(async () => {
    await act(async () => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals();
  });

  it('retains the receipt through activation and dashboard refresh until the customer explicitly enters', async () => {
    await restoreCheckout();
    const requestId = await checkStatus();
    expect(success()?.textContent).toContain('Thanh toán thành công!');
    expect(button('Đang kích hoạt gói…').disabled).toBe(true);
    expect(container.querySelector('.license-gate-card')).toBeNull();
    await emit({ type: 'license.activated', requestId, payload: active });
    await emit({ type: 'dashboard.state', requestId, payload: dashboard(active) });
    expect(success()?.textContent).toContain('Gói 30 ngày');
    expect(success()?.textContent).toContain('70.000 đ');
    expect(success()?.textContent).toContain('15/10/2026');
    expect(success()?.textContent).toContain('14/11/2026');
    expect(success()?.textContent).toContain('VMO123456');
    expect(button('Vào ứng dụng').disabled).toBe(false);
    expect(document.activeElement).toBe(button('Vào ứng dụng'));
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    await emit({ type: 'dashboard.state', payload: dashboard(active) });
    expect(success()).not.toBeNull();
    await act(async () => button('Vào ứng dụng').click());
    expect(success()).toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(false);
    expect(requestIds('license.payment.create')).toHaveLength(0);
    await emit({ type: 'license.payment.status', requestId, payload: fulfilled });
    expect(success()).toBeNull();
  });

  it('keeps the checkout blocking when a license heartbeat arrives before payment confirmation', async () => {
    await restoreCheckout();
    await emit({ type: 'dashboard.state', payload: dashboard(active) });
    expect(container.querySelector('.license-checkout-view')).not.toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
    expect(success()).toBeNull();
    await checkStatus();
    expect(button('Vào ứng dụng').disabled).toBe(false);
    expect(success()).not.toBeNull();
  });

  it('does not treat Paid awaiting provisioning as an activated package', async () => {
    await restoreCheckout();
    await checkStatus({ ...fulfilled, status: 'Paid', isFulfilled: false, message: 'Đã nhận tiền, đang chờ kích hoạt.' });
    expect(success()).toBeNull();
    expect(container.querySelector('.license-gate-card')?.textContent).toContain('Đã nhận tiền, đang chờ kích hoạt.');
    expect(button('Vào ứng dụng')).toBeUndefined();
  });

  it('allows activation retry after an error without creating another payment or unlocking the app', async () => {
    await restoreCheckout();
    const requestId = await checkStatus();
    await emit({ type: 'operation.error', requestId,
      error: { code: 'license_activation_pending', message: 'Chưa thể kích hoạt thiết bị. Hãy kiểm tra lại.' } });
    expect(success()?.querySelector('[role="alert"]')?.textContent).toContain('Chưa thể kích hoạt');
    expect(button('Vào ứng dụng')).toBeUndefined();
    await act(async () => button('Kiểm tra kích hoạt').click());
    const retryId = requestIds('license.payment.status').at(-1)!;
    expect(retryId).not.toBe(requestId);
    expect(button('Đang kích hoạt gói…').disabled).toBe(true);
    await emit({ type: 'license.payment.status', requestId: retryId, payload: fulfilled });
    await emit({ type: 'license.activated', requestId: retryId, payload: active });
    expect(button('Vào ứng dụng').disabled).toBe(false);
    expect(requestIds('license.payment.create')).toHaveLength(0);
  });

  it('does not show an enter-app confirmation for a scheduled future package', async () => {
    await restoreCheckout();
    await checkStatus({ ...fulfilled, isScheduled: true, startsAtUtc: '2026-11-15T00:00:00Z' });
    expect(success()).toBeNull();
    expect(container.querySelector('.license-gate-card')?.textContent).toContain('Đã xếp lịch sử dụng');
    expect(button('Vào ứng dụng')).toBeUndefined();
  });

  it('clears the receipt on account change and rejects both late status and activation messages', async () => {
    await restoreCheckout();
    const requestId = await checkStatus();
    await emit({ type: 'dashboard.state', payload: dashboard(missing, 'other-user') });
    expect(success()).toBeNull();
    await emit({ type: 'license.payment.status', requestId, payload: fulfilled });
    await emit({ type: 'license.activated', requestId, payload: active });
    expect(success()).toBeNull();
    expect(container.querySelector('.license-gate-card')).not.toBeNull();
  });

  it('requires an explicit button action, prevents Escape and traps Tab in the success dialog', async () => {
    await restoreCheckout();
    const requestId = await checkStatus();
    await emit({ type: 'license.activated', requestId, payload: active });
    const cancel = new Event('cancel', { bubbles: false, cancelable: true });
    await act(async () => { success()!.dispatchEvent(cancel); });
    expect(cancel.defaultPrevented).toBe(true);
    expect(success()?.open).toBe(true);
    for (const shiftKey of [false, true]) {
      const tab = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
      await act(async () => { button('Vào ứng dụng').dispatchEvent(tab); });
      expect(tab.defaultPrevented).toBe(true);
      expect(document.activeElement).toBe(button('Vào ứng dụng'));
    }
  });

  it('opens a newly created checkout and ignores a status belonging to a different order', async () => {
    const offersId = requestIds('license.offers.get').at(-1)!;
    await emit({ type: 'license.offers', requestId: offersId, payload: [{ licensePlanId: 'monthly',
      planCode: checkout.planCode, name: checkout.planName, priceVnd: checkout.amountVnd, durationDays: 30,
      maxActivatedDevices: 1, marketingFeatures: [], displayOrder: 1, organizationSeatAvailable: true }] });
    await act(async () => button('Chọn gói này').click());
    const createId = requestIds('license.payment.create').at(-1)!;
    await emit({ type: 'license.payment.checkout', requestId: createId, payload: checkout });
    expect(container.querySelector('.license-checkout-view')).not.toBeNull();
    await checkStatus({ ...fulfilled, orderCode: 'OTHER-ORDER' });
    expect(success()).toBeNull();
  });

  it('keeps the license gate after access is revoked while the success dialog is open', async () => {
    await restoreCheckout();
    const requestId = await checkStatus();
    await emit({ type: 'license.activated', requestId, payload: active });
    await emit({ type: 'license.invalidated', payload: { license: { ...missing, accessState: 'Revoked' } } });
    await emit({ type: 'license.activated', requestId, payload: active });
    expect(success()).toBeNull();
    expect(container.querySelector('.license-gate-card')).not.toBeNull();
  });

  it('shows required component setup after acknowledgment and keeps the app blocked by that separate gate', async () => {
    await restoreCheckout();
    const requestId = await checkStatus();
    await emit({ type: 'license.activated', requestId, payload: active });
    await emit({ type: 'dashboard.state', requestId, payload: { ...dashboard(active), selectedOrganizationId: 'org' } });
    await emit({ type: 'system.setup.status', requestId: requestIds('system.setup.get').at(-1)!, payload: {
      organizationId: 'org', contextGeneration: 'fixture-context', revision: 1, startupRequired: true,
      components: [{ id: 'media', name: 'FFmpeg', version: '1', state: 'NOT_INSTALLED',
        message: 'Chưa cài', canInstall: true, canVerify: true, canRepair: false }] } });
    expect(success()).not.toBeNull();
    expect(container.querySelector('.startup-setup-dialog')).toBeNull();
    await act(async () => button('Vào ứng dụng').click());
    expect(success()).toBeNull();
    expect(container.querySelector('.startup-setup-dialog')).not.toBeNull();
    expect(container.querySelector('.app-main')?.hasAttribute('inert')).toBe(true);
  });
});
