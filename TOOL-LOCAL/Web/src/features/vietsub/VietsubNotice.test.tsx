// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VietsubNotice } from './VietsubNotice';
import { useVietsubModule } from './useVietsubModule';
import { VietsubPage, type VietsubPageProps } from './VietsubPage';
import { defaultVietsubSubtitleStyle } from './vietsubSubtitleStyle';
import { defaultVietsubAudioMixSettings } from './vietsubAudioMix';
import type { HostMessage } from '../../types';
import type { VietsubModuleState, VietsubProjectSummary } from './types';

const bridge = vi.hoisted(() => ({ receive: (_message: HostMessage) => {}, sequence: 0 }));
vi.mock('../../bridge', () => ({ isHosted: true,
  postToHost: vi.fn(() => `request-${++bridge.sequence}`),
  subscribeToHost: (callback: typeof bridge.receive) => { bridge.receive = callback; return () => {}; }
}));
let module: ReturnType<typeof useVietsubModule>;
function ModuleHarness() {
  module = useVietsubModule(true, 'organization');
  return <>{(['errorMessage', 'subtitleNotice', 'voiceNotice', 'translationNotice'] as const).map(channel => (
    module.state[channel] && <VietsubNotice key={channel} eventId={module.state.noticeEvents?.[channel]?.id ?? 0}>
      {module.state[channel]}
    </VietsubNotice>
  ))}</>;
}

describe('dismissible Vietsub notices', () => {
  let container: HTMLDivElement;
  let root: ReturnType<typeof createRoot>;
  const emit = async (message: HostMessage) => { await act(async () => bridge.receive(message)); };
  const close = async (index = 0) => { await act(async () => container.querySelectorAll<HTMLButtonElement>('[aria-label="Đóng thông báo"]')[index].click()); };
  beforeEach(() => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    Element.prototype.scrollTo = vi.fn();
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.unstubAllGlobals(); });

  it('dismisses each banner independently, keeps retry and reopens only for a new event/context', async () => {
    const retry = vi.fn();
    const render = async (eventId = 1, context = 'project-a') => { await act(async () => root.render(<div key={context}>
      <VietsubNotice eventId={eventId} actions={<button onClick={retry}>Thử lại</button>}>Thông báo dài</VietsubNotice>
      <VietsubNotice eventId={5}>Thông báo khác</VietsubNotice>
    </div>)); };
    await render();
    const dismiss = container.querySelector<HTMLButtonElement>('[aria-label="Đóng thông báo"]')!;
    expect(dismiss.type).toBe('button'); expect(dismiss.title).toBe('Đóng thông báo');
    dismiss.focus(); expect(document.activeElement).toBe(dismiss);
    await close(); await render(); expect(container.textContent).toBe('Thông báo khác'); expect(retry).not.toHaveBeenCalled();
    await render(2); expect(container.textContent).toContain('Thông báo dài');
    await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === 'Thử lại')!.click());
    expect(retry).toHaveBeenCalledTimes(1);
    await close(); await render(2, 'project-b'); expect(container.textContent).toContain('Thông báo dài');
  });

  it('does not resurrect a dismissed job failure on polling, but shows an identical failure from another attempt', async () => {
    await act(async () => root.render(<ModuleHarness />));
    const job = { id: 'job', projectId: 'project', type: 'SYNTHESIZE_VOICE_LOCAL', status: 'FAILED',
      attemptCount: 1, updatedAtUtc: '2026-09-09T00:00:00Z', errorMessage: 'Không thể tạo giọng.' };
    await emit({ type: 'vietsub.job.changed', payload: job });
    expect(container.textContent).toContain('Không thể tạo giọng.'); await close();
    await emit({ type: 'vietsub.job.status', payload: { ...job, updatedAtUtc: '2026-09-09T00:00:01Z' } });
    expect(container.textContent).toBe(''); expect(module.state.activeJob?.status).toBe('FAILED');
    expect(module.state.voiceNotice).toBe('Không thể tạo giọng.');
    await emit({ type: 'vietsub.job.changed', payload: { ...job, attemptCount: 2 } });
    expect(container.textContent).toContain('Không thể tạo giọng.');
  });

  it('shows identical errors/exports from new requests while preserving a dismissed replay', async () => {
    await act(async () => root.render(<ModuleHarness />));
    const error = { type: 'vietsub.error', requestId: 'error-1', error: { code: 'failed', message: 'Thao tác thất bại' } };
    await emit(error); await close(); await emit(error); expect(container.textContent).toBe('');
    await emit({ ...error, requestId: 'error-2' }); expect(container.textContent).toContain('Thao tác thất bại');
    await emit({ type: 'vietsub.subtitle.export.completed', requestId: 'export-1', payload: { fileName: 'sample.srt' } });
    await close(1); await emit({ type: 'vietsub.subtitle.export.completed', requestId: 'export-1', payload: { fileName: 'sample.srt' } });
    expect(container.textContent).not.toContain('sample.srt');
    await emit({ type: 'vietsub.subtitle.export.completed', requestId: 'export-2', payload: { fileName: 'sample.srt' } });
    expect(container.textContent).toContain('sample.srt');
  });

  it('hides recovery only for the current project opening without mutating its recovery flag', async () => {
    const project: VietsubProjectSummary = { projectId: 'project-a', name: 'Fixture', status: 'READY',
      sourceLanguageCode: 'en', targetLanguageCode: 'vi', needsRecovery: true,
      serverSynchronized: true, updatedAtUtc: '2026-09-09T00:00:00Z' };
    const state: VietsubModuleState = { enabled: true, initialized: true, loading: false, busy: false, stage: 'shell_ready',
      projects: [project], selectedProject: project, jobs: [], subtitleStyle: defaultVietsubSubtitleStyle,
      audioMixSettings: defaultVietsubAudioMixSettings,
      ocrSettings: { languageCode: 'en', profile: 'BALANCED', region: { x: 0, y: 0.6, width: 1, height: 0.4 } } };
    const noop = vi.fn(); const saved = async () => true;
    const props: VietsubPageProps = { state, onRefresh: noop, onCreateProject: noop, onOpenProject: noop, onRenameProject: noop,
      onCloseProject: saved, onImportMedia: noop, onUpdateOcrSettings: saved, onPreviewOcr: noop, onStartOcr: noop,
      onStartTranslation: noop, onInstallTranslationRuntime: noop, onStartVoice: noop, onInstallVoiceRuntime: noop,
      onDismissTranslationResourceAlert: noop, onContinueTranslationAfterResourceWarning: noop,
      onPauseJob: noop, onResumeJob: noop, onRetryJob: noop, onCancelJob: noop, onActivateOcrTrack: noop,
      onImportSrt: noop, onActivateSubtitleTrack: noop, onLoadSubtitlePage: noop, onLoadTimelineWindow: noop,
      onRequestTimelineThumbnails: noop, onRequestTimelineWaveform: noop, onUpdateSubtitleCue: saved,
      onUpdateSubtitleStyle: saved, onUpdateTimelineCue: saved, onSplitSubtitleCue: noop, onAlignSubtitleCue: noop,
      onDuplicateSubtitleCue: noop, onDeleteSubtitleCue: noop, onExportSrt: noop, onExportVideo: async () => true,
      onCancelOperation: noop, onRegisterBeforeLeave: () => noop };
    const render = async (next: VietsubProjectSummary | null) => { await act(async () => root.render(
      <VietsubPage {...props} state={{ ...state, selectedProject: next }} />)); };
    await render(project); await close(); await render({ ...project });
    expect(container.textContent).not.toContain('Dự án được phục hồi'); expect(project.needsRecovery).toBe(true);
    await render({ ...project, projectId: 'project-b' }); expect(container.textContent).toContain('Dự án được phục hồi');
    await close(); await render(null); await render(project); expect(container.textContent).toContain('Dự án được phục hồi');
  });

  it('shows repeated local prerequisite warnings as new attempts', async () => {
    await act(async () => root.render(<ModuleHarness />));
    await act(async () => module.startVoice()); await close();
    await act(async () => module.startVoice()); expect(container.textContent).toContain('Hãy chọn subtitle track');
    await act(async () => module.startTranslation()); await close(1);
    await act(async () => module.startTranslation()); expect(container.querySelectorAll('.vietsub-notice')).toHaveLength(2);
  });

  it('keeps only the newest page response when rapid timeline navigation races', async () => {
    await act(async () => root.render(<ModuleHarness />));
    await emit({ type: 'vietsub.state', payload: { selectedProject: { projectId: 'project' } } });
    const query = { trackId: 'track', offset: 0, pageSize: 50, search: '', status: 'ALL' as const, speaker: '' };
    await act(async () => module.loadSubtitlePage(query)); const first = `request-${bridge.sequence}`;
    await act(async () => module.loadSubtitlePage({ ...query, offset: 50 })); const second = `request-${bridge.sequence}`;
    const page = { ...query, trackRevision: 1, cues: [], totalCount: 100, speakers: [] };
    await emit({ type: 'vietsub.subtitle.page', requestId: second, payload: { ...page, offset: 50 } });
    await emit({ type: 'vietsub.subtitle.page', requestId: first, payload: page });
    expect(module.state.subtitlePage?.offset).toBe(50);
    await emit({ type: 'vietsub.state', payload: { selectedProject: null } });
    await emit({ type: 'vietsub.subtitle.page', requestId: second, payload: page });
    expect(module.state.subtitlePage).toBeNull();
  });
});
