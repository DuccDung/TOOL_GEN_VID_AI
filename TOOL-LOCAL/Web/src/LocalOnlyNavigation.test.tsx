// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { beforeEach, afterEach, expect, it, vi } from 'vitest';
import App from './App';
import type { HostMessage } from './types';

const fixture = vi.hoisted(() => ({
  receive: (_message: HostMessage) => {},
  posts: [] as string[],
  closeProject: vi.fn(async () => true),
  prepare: vi.fn(async () => true),
  selected: null as null | { projectId: string }
}));
vi.mock('./bridge', () => ({ isHosted: true,
  postToHost: (type: string) => { fixture.posts.push(type); return `r${fixture.posts.length}`; },
  subscribeToHost: (callback: typeof fixture.receive) => { fixture.receive = callback; return () => {}; }
}));
vi.mock('./features/vietsub/useVietsubModule', () => ({ useVietsubModule: () => ({
  state: { enabled: true, loading: false, busy: false, projects: [], selectedProject: fixture.selected },
  closeProject: fixture.closeProject, prepareToLeaveEditor: fixture.prepare
}) }));
vi.mock('./features/vietsub/VietsubPage', () => ({ VietsubPage: () => <div data-testid="vietsub-page">Thư viện Vietsub</div> }));
let root: ReturnType<typeof createRoot>, container: HTMLDivElement;
const dashboard = {
  profile: { userId: 'fixture', email: 'fixture@example.test', displayName: 'Fixture', accountStatus: 'Active', roles: [] },
  organizations: [], selectedOrganizationId: '', projects: [], selectedProject: null, assetLibrary: null, models: [],
  providerStatus: { openAiReady: false, klingReady: false, videoReady: false },
  mediaTools: { ready: true, message: '', checkedAtUtc: '' }, generationRunning: false,
  features: { vietsubEnabled: true, speechSynchronizationEnabled: false, vietsubLocalOnly: true }, sceneFirstFrames: [],
  license: { hasActiveLicense: true, currentDeviceActivated: true, accessState: 'Active', leaseExpiresAtUtc: '2099-01-01T00:00:00Z' }
};
beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
  fixture.posts = []; fixture.selected = null; fixture.prepare.mockResolvedValue(true); fixture.closeProject.mockClear();
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root.render(<App />));
  await act(async () => fixture.receive({ type: 'dashboard.state', payload: dashboard }));
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

it('mở Vietsub và chỉ hiển thị điều hướng local cùng cài đặt', async () => {
  expect(container.querySelector('[data-testid="vietsub-page"]')).not.toBeNull();
  const nav = container.querySelector('.sidebar-nav')!;
  expect(nav.textContent).toContain('Dịch phụ đề');
  expect(nav.textContent).toContain('Cài đặt');
  expect(nav.textContent).not.toMatch(/Tạo Video|API AI|AI Models|Nhân vật|Lịch sử render/);
  const settings = [...nav.querySelectorAll('button')].find(b => b.textContent === 'Cài đặt')!;
  await act(async () => settings.click());
  expect(container.textContent).toContain('Vietsub local');
  expect(container.querySelector('[role="switch"]')).toBeNull();
  expect(fixture.posts.every(p => ['app.ready', 'desktop.settings.get'].includes(p))).toBe(true);
});

it('nút tạo dự án giữ bản nháp khi lưu lỗi và không gửi project.create video', async () => {
  fixture.selected = { projectId: 'existing' }; fixture.prepare.mockResolvedValue(false);
  await act(async () => fixture.receive({ type: 'dashboard.state', payload: dashboard }));
  const create = container.querySelector<HTMLButtonElement>('.new-video-button')!;
  expect(create.textContent).toContain('Tạo dự án mới');
  await act(async () => create.click());
  expect(fixture.closeProject).not.toHaveBeenCalled();
  fixture.prepare.mockResolvedValue(true);
  await act(async () => create.click());
  expect(fixture.closeProject).toHaveBeenCalledTimes(1);
  expect(fixture.posts).not.toContain('project.create');
  expect(fixture.posts).not.toContain('providers.settings.get');
});
