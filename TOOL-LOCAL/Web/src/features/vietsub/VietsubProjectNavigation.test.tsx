// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage } from '../../types';
import { VietsubPage } from './VietsubPage';
import { useVietsubModule } from './useVietsubModule';

const bridge = vi.hoisted(() => ({
  listener: null as null | ((message: HostMessage) => void),
  posts: [] as { type: string; requestId: string; payload: unknown }[], sequence: 0
}));
vi.mock('../../bridge', () => ({
  isHosted: true,
  postToHost(type: string, payload?: unknown) {
    const requestId = `request-${++bridge.sequence}`;
    bridge.posts.push({ type, requestId, payload });
    return requestId;
  },
  subscribeToHost(listener: (message: HostMessage) => void) {
    bridge.listener = listener;
    return () => { bridge.listener = null; };
  }
}));

const noOp = () => { };
const saved = async () => true;
function Harness() {
  const module = useVietsubModule(true, 'organization');
  return <VietsubPage
    state={module.state} onRefresh={module.refresh}
    onCreateProject={module.createProject} onOpenProject={module.openProject} onRenameProject={module.renameProject}
    onCloseProject={module.closeProject} onRegisterBeforeLeave={module.registerBeforeLeave}
    onImportMedia={noOp} onUpdateOcrSettings={saved} onPreviewOcr={noOp} onStartOcr={noOp}
    onStartTranslation={noOp} onInstallTranslationRuntime={noOp} onStartVoice={noOp} onInstallVoiceRuntime={noOp}
    onDismissTranslationResourceAlert={noOp} onContinueTranslationAfterResourceWarning={noOp}
    onPauseJob={noOp} onResumeJob={noOp} onRetryJob={noOp} onCancelJob={noOp} onActivateOcrTrack={noOp}
    onImportSrt={noOp} onActivateSubtitleTrack={noOp} onLoadSubtitlePage={module.loadSubtitlePage}
    onLoadTimelineWindow={noOp} onRequestTimelineThumbnails={noOp} onRequestTimelineWaveform={noOp}
    onUpdateSubtitleCue={module.updateSubtitleCue} onUpdateSubtitleStyle={saved} onUpdateTimelineCue={saved}
    onSplitSubtitleCue={noOp} onAlignSubtitleCue={noOp} onDuplicateSubtitleCue={noOp} onDeleteSubtitleCue={noOp}
    onExportSrt={noOp} onExportVideo={module.exportVideo} onCancelOperation={noOp}
  />;
}

const project = {
  projectId: 'project', name: 'Dự án đang biên tập', status: 'READY', sourceLanguageCode: 'en',
  targetLanguageCode: 'vi', updatedAtUtc: new Date(0).toISOString(), needsRecovery: false, serverSynchronized: true
};
const workspace = {
  activeTrackId: 'track', tracks: [{ trackId: 'track', revision: 1, source: 'IMPORTED_SRT', languageCode: 'en',
    displayName: 'Translated', cueCount: 1, translatedCueCount: 1, warningCueCount: 0, updatedAtUtc: '' }]
};
const stateMessage = (requestId?: string, open = true, busy = false): HostMessage => ({
  type: 'vietsub.state', requestId, payload: {
    enabled: true, busy, activeOperationRequestId: busy ? requestId : null,
    selectedProject: open ? project : null, projects: [project], subtitleWorkspace: open ? workspace : null, jobs: []
  }
});
let root: Root;
let container: HTMLDivElement;
const backButton = () => container.querySelector<HTMLButtonElement>('.vietsub-new-project-button')!;
const requests = (type: string) => bridge.posts.filter(post => post.type === type);
async function emit(message: HostMessage) {
  await act(async () => { bridge.listener!(message); });
}
async function finishOperation(requestId: string, open = true) {
  await emit(stateMessage(requestId, open));
  await emit({ type: 'vietsub.operation.completed', requestId, payload: { completed: true } });
}
async function write(field: HTMLInputElement | HTMLTextAreaElement, value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(Object.getPrototypeOf(field), 'value')!.set!.call(field, value);
    field.dispatchEvent(new Event('input', { bubbles: true }));
  });
}
beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('AudioContext', undefined);
  vi.stubGlobal('ResizeObserver', class { observe() { } disconnect() { } });
  vi.spyOn(window, 'requestAnimationFrame').mockImplementation(callback => window.setTimeout(() => callback(0), 16));
  vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(id => window.clearTimeout(id));
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', { configurable: true, value: vi.fn() });
  bridge.posts = []; bridge.sequence = 0;
  container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  await act(async () => root.render(<Harness />));
  await emit(stateMessage());
  const pageRequest = requests('vietsub.subtitle.page.get').at(-1)?.requestId;
  expect(pageRequest).toBeDefined();
  await emit({ type: 'vietsub.subtitle.page', requestId: pageRequest, payload: {
    trackId: 'track', trackRevision: 1, offset: 0, pageSize: 50, totalCount: 1,
    search: '', status: 'ALL', speaker: '', speakers: [], cues: [{ cueId: 'cue', cueIndex: 0,
      startMilliseconds: 0, endMilliseconds: 1000, originalText: 'Hello', translatedText: 'Xin chào',
      speaker: 'speaker_1', originalLocked: false, translationLocked: false, warnings: [], updatedAtUtc: '' }]
  } });
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals();
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollTo');
});

describe('returning from the editor to project creation', () => {
  it('saves the draft once, closes the project, then shows the creation form without automatically creating a project', async () => {
    await write(container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!, 'Bản dịch mới nhất');
    expect(backButton().textContent?.trim()).toBe('Tạo dự án mới');
    await act(async () => { backButton().click(); backButton().click(); });
    expect(backButton().disabled).toBe(true);
    expect(backButton().textContent).toContain('Đang quay về');
    expect(requests('vietsub.subtitle.cue.update')).toHaveLength(1);
    expect(requests('vietsub.project.close')).toHaveLength(0);
    const save = requests('vietsub.subtitle.cue.update')[0];
    expect(save.payload).toEqual(expect.objectContaining({ translatedText: 'Bản dịch mới nhất' }));
    await finishOperation(save.requestId);
    expect(requests('vietsub.project.close')).toHaveLength(1);
    await finishOperation(requests('vietsub.project.close')[0].requestId, false);
    expect(container.querySelector('.vietsub-editor-workspace')).toBeNull();
    expect(container.querySelector('.vietsub-create-card')).not.toBeNull();
    expect(container.textContent).toContain('Dự án đang biên tập');
    expect(requests('vietsub.project.create')).toHaveLength(0);

    await write(container.querySelector<HTMLInputElement>('.vietsub-create-card input')!, 'Dự án tiếp theo');
    await act(async () => container.querySelector<HTMLButtonElement>('.vietsub-create-card button')!.click());
    expect(requests('vietsub.project.create')).toHaveLength(1);
    expect(requests('vietsub.project.create')[0].payload).toEqual({ name: 'Dự án tiếp theo' });
  });

  it('keeps the draft after a save error and permits retry after a close error', async () => {
    await write(container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!, 'Bản nháp cần giữ');
    await act(async () => backButton().click());
    await emit({ type: 'vietsub.error', requestId: requests('vietsub.subtitle.cue.update')[0].requestId,
      error: { code: 'vietsub_operation_failed', message: 'Không thể lưu bản nháp.' } });
    expect(backButton().disabled).toBe(false);
    expect(requests('vietsub.project.close')).toHaveLength(0);
    expect(container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!.value).toBe('Bản nháp cần giữ');
    await act(async () => backButton().click());
    await finishOperation(requests('vietsub.subtitle.cue.update').at(-1)!.requestId);
    await emit({ type: 'vietsub.error', requestId: requests('vietsub.project.close')[0].requestId,
      error: { code: 'vietsub_operation_failed', message: 'Chưa đóng được dự án.' } });
    expect(backButton().disabled).toBe(false);
    expect(container.textContent).toContain('Chưa thể quay về màn hình tạo dự án. Hãy thử lại.');
    await act(async () => backButton().click());
    expect(requests('vietsub.project.close')).toHaveLength(2);
    await finishOperation(requests('vietsub.project.close').at(-1)!.requestId, false);
    expect(container.querySelector('.vietsub-create-card')).not.toBeNull();
  });

  it('blocks navigation while the desktop is processing another operation', async () => {
    await emit(stateMessage('export-in-progress', true, true));
    expect(backButton().disabled).toBe(true);
    await act(async () => backButton().click());
    expect(requests('vietsub.project.close')).toHaveLength(0);
    await emit(stateMessage('export-in-progress'));
    expect(backButton().disabled).toBe(false);
  });
});
