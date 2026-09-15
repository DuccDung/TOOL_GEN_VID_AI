// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage } from '../../types';
import { StartupSystemSetupModal } from './StartupSystemSetupModal';
import { useSystemSetup } from './useSystemSetup';
import type { SetupSnapshot } from './types';

const bridge = vi.hoisted(() => ({
  listener: null as null | ((message: HostMessage) => void),
  posts: [] as { type: string; requestId: string; payload?: unknown }[],
  sequence: 0
}));

vi.mock('../../bridge', () => ({
  postToHost(type: string, payload?: unknown) {
    const requestId = `startup-${++bridge.sequence}`;
    bridge.posts.push({ type, requestId, payload });
    return requestId;
  },
  subscribeToHost(listener: (message: HostMessage) => void) {
    bridge.listener = listener;
    return () => { bridge.listener = null; };
  }
}));

function Harness() {
  const setup = useSystemSetup('org');
  return <><button type="button" id="background-action">Tạo dự án</button><StartupSystemSetupModal setup={setup} /></>;
}

const snapshot = (): SetupSnapshot => ({
  contextGeneration: 'startup-context',
  organizationId: 'org',
  revision: 1,
  startupRequired: true,
  components: ['media', 'ocr', 'qwen', 'piper'].map(id => ({
    id,
    name: id === 'media' ? 'Xử lý video · FFmpeg' : id,
    version: '1',
    state: 'UNKNOWN',
    message: 'Chưa kiểm tra',
    canInstall: true,
    canVerify: true,
    canRepair: true
  }))
});

let root: Root;
let container: HTMLDivElement;
const last = (type: string) => bridge.posts.filter(post => post.type === type).at(-1)!;
const button = (label: string) => [...container.querySelectorAll<HTMLButtonElement>('button')]
  .find(item => item.textContent?.includes(label))!;
async function emit(type: string, payload: SetupSnapshot, requestId?: string) {
  await act(async () => bridge.listener!({ type, payload, requestId }));
}
async function showQwenResourceWarning() {
  await emit('system.setup.status', snapshot(), last('system.setup.get').requestId);
  const check = last('system.setup.check');
  const operationId = (check.payload as { operationId: string }).operationId;
  const running: SetupSnapshot = {
    ...snapshot(), revision: 2,
    operation: { operationId, mode: 'check', state: 'Running', componentIds: ['media', 'ocr', 'qwen', 'piper'],
      sequence: 2, currentComponent: 'qwen', allSelectedReady: false, allRequiredReady: false }
  };
  await emit('system.setup.accepted', running, check.requestId);
  const warning: SetupSnapshot = {
    ...running,
    revision: 3,
    components: running.components.map(component => component.id === 'qwen'
      ? { ...component, state: 'NEEDS_VERIFICATION', errorCode: 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED',
          resourceProfileId: 'standard', downloadBytes: 2_497_280_256, message: 'Cần xác nhận tài nguyên.' }
      : { ...component, state: 'READY', message: 'Sẵn sàng' }),
    operation: { ...running.operation!, state: 'Failed', sequence: 3 }
  };
  await emit('system.setup.completed', warning);
  return operationId;
}

beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  bridge.posts = [];
  bridge.sequence = 0;
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => root.render(<Harness />));
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe('Startup System Setup modal', () => {
  it('shows the project background first, checks automatically and cannot be dismissed with Escape', async () => {
    expect(container.textContent).toContain('Tạo dự án');
    expect(container.querySelector('[role="dialog"]')).not.toBeNull();
    await emit('system.setup.status', snapshot(), last('system.setup.get').requestId);

    const check = last('system.setup.check');
    expect(check.payload).toMatchObject({
      expectedOrganizationId: 'org',
      contextGeneration: 'startup-context',
      componentIds: ['media', 'ocr', 'qwen', 'piper']
    });
    expect(container.querySelectorAll('th')).toHaveLength(3);
    await act(async () => window.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'Escape', bubbles: true, cancelable: true
    })));
    expect(container.querySelector('[role="dialog"]')).not.toBeNull();

    await act(async () => button('Hủy và thoát').click());
    expect(last('system.setup.exit')).toBeDefined();
  });

  it('makes the RAM warning clear and retries the correlated Qwen check after confirmation', async () => {
    const operationId = await showQwenResourceWarning();
    expect(container.textContent).toContain('Model Qwen đã có');
    expect(container.textContent).toContain('Cần xác nhận bộ nhớ');
    expect(container.textContent).not.toContain('Tải xuống:');
    expect(container.textContent).not.toContain('Tôi đã đóng các ứng dụng nặng');
    expect(container.querySelector('progress')).toBeNull();
    expect(button('Kiểm tra lại').disabled).toBe(false);
    expect(button('Vẫn thử dùng Qwen').disabled).toBe(true);
    await act(async () => container.querySelector<HTMLInputElement>('.startup-setup-resource-warning input')!.click());
    expect(button('Vẫn thử dùng Qwen').disabled).toBe(false);
    await act(async () => button('Vẫn thử dùng Qwen').click());
    expect(last('system.setup.retry').payload).toMatchObject({
      previousOperationId: operationId,
      componentIds: ['qwen'],
      resourceWarningAccepted: true,
      confirmedResourceProfileId: 'standard'
    });
  });

  it('lets the user check Qwen again without accepting the low-memory risk', async () => {
    const operationId = await showQwenResourceWarning();
    await act(async () => button('Kiểm tra lại').click());
    expect(last('system.setup.retry').payload).toMatchObject({
      previousOperationId: operationId,
      componentIds: ['qwen']
    });
    expect(last('system.setup.retry').payload).not.toHaveProperty('resourceWarningAccepted');
  });

  it('uses the verified application package when a bundled component needs repair', async () => {
    await emit('system.setup.status', snapshot(), last('system.setup.get').requestId);
    const check = last('system.setup.check');
    const operationId = (check.payload as { operationId: string }).operationId;
    const repair: SetupSnapshot = {
      ...snapshot(), revision: 3,
      components: snapshot().components.map(component => component.id === 'media'
        ? { ...component, state: 'REPAIR_REQUIRED', canInstall: false, canRepair: true, message: 'Cần sửa package.' }
        : { ...component, state: 'READY', message: 'Sẵn sàng' }),
      operation: { operationId, mode: 'check', state: 'Failed', componentIds: ['media', 'ocr', 'qwen', 'piper'],
        sequence: 3, allSelectedReady: false, allRequiredReady: false }
    };
    await emit('system.setup.accepted', { ...repair, revision: 2,
      operation: { ...repair.operation!, state: 'Running', sequence: 2 } }, check.requestId);
    await emit('system.setup.completed', repair);

    await act(async () => button('Sửa bộ ứng dụng').click());
    expect(last('media.tools.install')).toBeDefined();
    expect(container.textContent).toContain('Đang chuẩn bị package sửa chữa');
  });
});
