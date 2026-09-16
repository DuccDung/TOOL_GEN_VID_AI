// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LoginPage, type LoginViewState } from './LoginPage';
import { postToHost } from '../../bridge';
import type { HostMessage } from '../../types';

const bridge = vi.hoisted(() => ({ listeners: new Set<(message: HostMessage) => void>() }));
vi.mock('../../bridge', () => ({
  isHosted: true,
  postToHost: vi.fn(() => 'request-id'),
  subscribeToHost: (listener: (message: HostMessage) => void) => {
    bridge.listeners.add(listener);
    return () => { bridge.listeners.delete(listener); };
  }
}));

let container: HTMLDivElement;
let root: Root;
const ready: LoginViewState = { busy: false, status: '', error: false, focusField: 'email' };

async function hostState(payload: LoginViewState) {
  await act(async () => {
    for (const listener of [...bridge.listeners]) listener({ type: 'auth.state', payload });
    await new Promise(resolve => window.setTimeout(resolve, 0));
  });
}

async function change(input: HTMLInputElement, value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

async function click(label: string) {
  const button = [...container.querySelectorAll('button')].find(item => item.textContent?.includes(label));
  if (!button) throw new Error(`Missing button: ${label}`);
  await act(async () => button.click());
}

beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.clearAllMocks();
  bridge.listeners.clear();
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => root.render(<LoginPage />));
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe('WebView login', () => {
  it('waits for session restore and submits one request with the remember choice', async () => {
    expect(postToHost).toHaveBeenCalledWith('auth.ready');
    expect(container.querySelector<HTMLButtonElement>('.login-submit')?.disabled).toBe(true);
    await hostState(ready);
    const email = container.querySelector<HTMLInputElement>('#login-email')!;
    const password = container.querySelector<HTMLInputElement>('#login-password')!;
    await change(email, 'user@example.com');
    await change(password, 'pass1234567');
    await act(async () => container.querySelector<HTMLInputElement>('.login-remember-row input')!.click());
    await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    expect(postToHost).toHaveBeenLastCalledWith('auth.login', {
      email: 'user@example.com', password: 'pass1234567', rememberMe: false
    });
    expect(container.querySelector<HTMLButtonElement>('.login-submit')?.disabled).toBe(true);
    await act(async () => container.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    expect(vi.mocked(postToHost).mock.calls.filter(([type]) => type === 'auth.login')).toHaveLength(1);
  });

  it('shows field errors and restores focus after invalid credentials', async () => {
    await hostState(ready);
    const password = container.querySelector<HTMLInputElement>('#login-password')!;
    await change(password, 'entered-password');
    await hostState({ busy: false, status: 'Email hoặc mật khẩu không đúng.', error: true,
      passwordError: 'Email hoặc mật khẩu không đúng.', focusField: 'password' });
    expect(container.querySelector('#login-password-error')?.textContent).toBe('Email hoặc mật khẩu không đúng.');
    expect(document.activeElement).toBe(password);
    expect(password.value).toBe('entered-password');
    expect(container.querySelector('.login-status')?.getAttribute('role')).toBe('alert');
  });

  it('routes account links through the host without starting provider sign-in', async () => {
    await hostState(ready);
    await click('Quên mật khẩu?');
    expect(postToHost).toHaveBeenLastCalledWith('auth.password-reset.open', { email: '' });
    await click('Đăng ký ngay');
    expect(postToHost).toHaveBeenLastCalledWith('auth.register.open', undefined);
    await click('Đăng nhập với Google');
    expect(postToHost).toHaveBeenLastCalledWith('auth.social.unavailable', { provider: 'Google' });
    expect(vi.mocked(postToHost).mock.calls.some(([type]) => type === 'auth.login')).toBe(false);
  });
});
