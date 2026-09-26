// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage } from '../../types';
import { SystemSetupPanel } from './SystemSetupPanel';
import { useSystemSetup } from './useSystemSetup';
import { acceptSetupSnapshot, type SetupSnapshot } from './types';

const bridge = vi.hoisted(() => ({
  listener: null as null | ((message: HostMessage) => void),
  posts: [] as { type: string; requestId: string; payload?: unknown }[], sequence: 0
}));
vi.mock('../../bridge', () => ({
  postToHost(type: string, payload?: unknown) {
    const requestId = `setup-${++bridge.sequence}`;
    bridge.posts.push({ type, requestId, payload }); return requestId;
  },
  subscribeToHost(listener: (message: HostMessage) => void) {
    bridge.listener = listener; return () => { bridge.listener = null; };
  }
}));
function Harness({ organization = 'org', visible = true }: { organization?: string; visible?: boolean }) {
  const setup = useSystemSetup(organization);
  return visible ? <SystemSetupPanel setup={setup} /> : <div>Trang dự án</div>;
}
const initial = (): SetupSnapshot => ({ contextGeneration: 'context', organizationId: 'org', revision: 1,
  components: ['media', 'ocr', 'qwen', 'piper'].map(id => ({ id, name: id, version: '1', state: 'UNKNOWN',
    message: 'Chưa kiểm tra', canInstall: true, canVerify: true, canRepair: true })) });
let root: Root;
let container: HTMLDivElement;
const button = (label: string) => [...container.querySelectorAll<HTMLButtonElement>('button')].find(b => b.textContent === label)!;
const last = (type: string) => bridge.posts.filter(p => p.type === type).at(-1)!;
async function emit(type: string, payload: SetupSnapshot, requestId?: string) {
  await act(async () => bridge.listener!({ type, payload, requestId }));
}
async function showQwenResourceWarning() {
  await act(async () => button('Cài thành phần cần thiết').click());
  const start = last('system.setup.start');
  const operationId = (start.payload as { operationId: string }).operationId;
  const running: SetupSnapshot = { ...initial(), revision: 2,
    operation: { operationId, mode: 'start', state: 'Running', componentIds: ['media', 'ocr', 'qwen', 'piper'],
      sequence: 2, currentComponent: 'qwen', allSelectedReady: false, allRequiredReady: false } };
  await emit('system.setup.accepted', running, start.requestId);
  await emit('system.setup.completed', { ...running, revision: 3,
    components: running.components.map(component => component.id === 'qwen'
      ? { ...component, state: 'NEEDS_VERIFICATION', errorCode: 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED',
          resourceProfileId: 'qwen3-cpu-low-memory-v1', message: 'Cần xác nhận RAM.' }
      : { ...component, state: 'READY' }),
    operation: { ...running.operation!, state: 'Failed', sequence: 3 } });
  return operationId;
}
beforeEach(async () => {
  vi.useFakeTimers();
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  bridge.posts = []; bridge.sequence = 0;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root.render(<Harness />));
  await emit('system.setup.status', initial(), last('system.setup.get').requestId);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  expect(bridge.listener).toBeNull();
  expect(vi.getTimerCount()).toBe(0);
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('Setup application workflow', () => {
  it('prepares bundled Piper once at startup and does not restart after cancellation or navigation', async () => {
    const checked: SetupSnapshot = { ...initial(), revision: 2, startupRequired: true,
      components: initial().components.map(c => c.id === 'piper'
        ? { ...c, state: 'NOT_INSTALLED', canPrepareOffline: true } : { ...c, state: 'READY' }),
      operation: { operationId: 'check-offline', mode: 'check', state: 'PartiallyCompleted', componentIds: ['piper'],
        sequence: 2, allSelectedReady: false, allRequiredReady: false } };
    await act(async () => vi.advanceTimersByTime(6000));
    await emit('system.setup.status', checked, last('system.setup.get').requestId);
    const start = last('system.setup.start');
    expect(start.payload).toMatchObject({ componentIds: ['piper'], expectedOrganizationId: 'org' });
    const operationId = (start.payload as { operationId: string }).operationId;
    const running: SetupSnapshot = { ...checked, revision: 3,
      operation: { ...checked.operation!, operationId, mode: 'start', state: 'Running', sequence: 1 } };
    await emit('system.setup.accepted', running, start.requestId);
    await emit('system.setup.completed', { ...running, revision: 4,
      operation: { ...running.operation!, state: 'Cancelled', sequence: 2 } });
    await act(async () => root.render(<Harness visible={false} />));
    await act(async () => root.render(<Harness />));
    await act(async () => vi.advanceTimersByTime(6000));
    await emit('system.setup.status', { ...checked, revision: 5 }, last('system.setup.get').requestId);
    expect(bridge.posts.filter(p => p.type === 'system.setup.start')).toHaveLength(1);
  });

  it.each([['settings', false, true], ['missing bundle', true, false]] as const)(
    'does not automatically prepare Piper for %s', async (_name, startupRequired, canPrepareOffline) => {
      const checked: SetupSnapshot = { ...initial(), startupRequired,
        components: initial().components.map(c => ({ ...c, state: 'NOT_INSTALLED', canPrepareOffline })),
        operation: { operationId: 'check', mode: 'check', state: 'Failed', componentIds: ['piper'],
          sequence: 2, allSelectedReady: false, allRequiredReady: false } };
      await act(async () => vi.advanceTimersByTime(6000));
      await emit('system.setup.status', checked, last('system.setup.get').requestId);
      expect(bridge.posts.filter(p => p.type === 'system.setup.start')).toHaveLength(0);
    });

  it('changes polling while busy and removes the poller and listener on unmount', async () => {
    const count = () => bridge.posts.filter(post => post.type === 'system.setup.get').length;
    const initialCount = count();
    await act(async () => vi.advanceTimersByTime(6000));
    expect(count()).toBe(initialCount + 1);
    await act(async () => button('Cài thành phần cần thiết').click());
    await act(async () => vi.advanceTimersByTime(2500));
    expect(count()).toBe(initialCount + 2);
    await act(async () => root.render(null));
    expect(bridge.listener).toBeNull();
    expect(vi.getTimerCount()).toBe(0);
    await act(async () => vi.advanceTimersByTime(18000));
    expect(count()).toBe(initialCount + 2);
  });

  it('ignores a late native rejection from the previous organization', async () => {
    await act(async () => button('Cài thành phần cần thiết').click());
    const previous = last('system.setup.start');
    await act(async () => root.render(<Harness organization="other-org" />));
    await act(async () => bridge.listener!({ type: 'operation.error', requestId: previous.requestId,
      error: { code: 'system_setup_busy', message: 'Lỗi từ tổ chức cũ' } }));
    expect(container.textContent).not.toContain('Lỗi từ tổ chức cũ');
    await emit('system.setup.status', { ...initial(), organizationId: 'other-org', contextGeneration: 'new' },
      last('system.setup.get').requestId);
    expect(button('Cài thành phần cần thiết').disabled).toBe(false);
  });

  it('starts without project and allows Piper to be omitted', async () => {
    await act(async () => container.querySelector<HTMLInputElement>('input')!.click());
    await act(async () => button('Cài thành phần cần thiết').click());
    const post = last('system.setup.start');
    expect(post.payload).toMatchObject({ componentIds: ['media', 'ocr', 'qwen'], expectedOrganizationId: 'org', contextGeneration: 'context' });
    expect(post.payload).not.toHaveProperty('projectId');
    expect(button('Cài thành phần cần thiết').disabled).toBe(true);
  });
  it('keeps operation alive across navigation and restores a lost terminal event through GET', async () => {
    await act(async () => button('Cài thành phần cần thiết').click());
    const post = last('system.setup.start');
    const { operationId } = post.payload as { operationId: string };
    const running: SetupSnapshot = { ...initial(), revision: 2, operation: { operationId, mode: 'start', state: 'Running',
      componentIds: ['qwen'], sequence: 2, currentComponent: 'qwen', percent: 60, allSelectedReady: false, allRequiredReady: false } };
    await emit('system.setup.accepted', running, post.requestId);
    await act(async () => root.render(<Harness visible={false} />));
    expect(container.textContent).toContain('Trang dự án');
    await emit('system.setup.progress', { ...running, revision: 3, operation: { ...running.operation!, sequence: 3, percent: 70 } });
    await act(async () => root.render(<Harness />));
    expect(container.querySelector('progress')?.value).toBe(70);
    await emit('system.setup.completed', { ...running, revision: 4, operation: { ...running.operation!, sequence: 4, state: 'Completed', allSelectedReady: true } });
    expect(button('Cài thành phần cần thiết').disabled).toBe(false);
    await emit('system.setup.progress', running);
    expect(container.querySelector('progress')).toBeNull();
    expect(bridge.posts.filter(p => p.type === 'system.setup.start')).toHaveLength(1);
  });
  it('sends cancel for the current operation and handles partial success without claiming all ready', async () => {
    await act(async () => button('Cài thành phần cần thiết').click());
    const post = last('system.setup.start');
    const { operationId } = post.payload as { operationId: string };
    const running: SetupSnapshot = { ...initial(), operation: { operationId, mode: 'start', state: 'Running',
      componentIds: ['qwen', 'piper'], sequence: 1, allSelectedReady: false, allRequiredReady: false } };
    await emit('system.setup.accepted', running, post.requestId);
    await act(async () => button('Hủy Setup').click());
    expect(last('system.setup.cancel').payload).toEqual({ operationId });
    const done: SetupSnapshot = { ...running, revision: 4, components: running.components.map(c => ({ ...c,
      state: c.id === 'piper' ? 'REPAIR_REQUIRED' : 'READY' })), operation: { ...running.operation!, state: 'PartiallyCompleted', sequence: 2 } };
    await emit('system.setup.completed', done);
    expect(container.textContent).not.toContain('Các thành phần đã chọn đã sẵn sàng.');
    await act(async () => button('Kiểm tra lại phần chưa đạt').click());
    expect(last('system.setup.retry').payload).toMatchObject({ previousOperationId: operationId, componentIds: ['piper'] });
    expect((last('system.setup.retry').payload as { operationId: string }).operationId).not.toBe(operationId);
  });
  it('clears pending on native rejection and supports retry', async () => {
    await act(async () => button('Cài thành phần cần thiết').click());
    await act(async () => bridge.listener!({ type: 'operation.error', requestId: last('system.setup.start').requestId,
      error: { code: 'system_setup_busy', message: 'Runtime đang được sử dụng.' } }));
    expect(container.querySelector('[role=alert]')?.textContent).toContain('Runtime đang được sử dụng');
    expect(button('Cài thành phần cần thiết').disabled).toBe(false);
  });
  it('separates a normal retry from accepting the Qwen RAM warning', async () => {
    const operationId = await showQwenResourceWarning();
    expect(container.textContent).toContain('Model đã có');
    expect(container.textContent).toContain('Cần xác nhận bộ nhớ');
    expect(button('Vẫn thử dùng Qwen').disabled).toBe(true);
    await act(async () => button('Kiểm tra lại phần chưa đạt').click());
    expect(last('system.setup.retry').payload).toMatchObject({ previousOperationId: operationId,
      componentIds: ['qwen'] });
    expect(last('system.setup.retry').payload).not.toHaveProperty('resourceWarningAccepted');
  });
  it('sends explicit confirmation only when the user chooses to try Qwen with low RAM', async () => {
    const operationId = await showQwenResourceWarning();
    await act(async () => container.querySelector<HTMLInputElement>('.setup-warning input')!.click());
    expect(button('Vẫn thử dùng Qwen').disabled).toBe(false);
    await act(async () => button('Vẫn thử dùng Qwen').click());
    expect(last('system.setup.retry').payload).toMatchObject({ previousOperationId: operationId,
      componentIds: ['qwen'], resourceWarningAccepted: true,
      confirmedResourceProfileId: 'qwen3-cpu-low-memory-v1' });
  });
  it('drops callbacks after switching organization', async () => {
    await act(async () => root.render(<Harness organization="other-org" />));
    await emit('system.setup.progress', { ...initial(), components: initial().components.map(c => ({ ...c, message: 'OLD CALLBACK' })) });
    expect(container.textContent).not.toContain('OLD CALLBACK');
  });
});

describe('snapshot ordering', () => {
  it('rejects stale GET from an earlier operation', () => {
    const current = { ...initial(), revision: 20 };
    expect(acceptSetupSnapshot(current, { ...initial(), revision: 10 }, 'org', true)).toBe(current);
  });
  it('permits a fresh GET to restore a completed operation when its event was lost', () => {
    const completed: SetupSnapshot = { ...initial(), revision: 10, operation: { operationId: 'operation', state: 'Completed',
      mode: 'start', componentIds: ['qwen'], sequence: 9, allSelectedReady: true, allRequiredReady: false } };
    expect(acceptSetupSnapshot(initial(), completed, 'org', true)).toEqual(completed);
  });
});
