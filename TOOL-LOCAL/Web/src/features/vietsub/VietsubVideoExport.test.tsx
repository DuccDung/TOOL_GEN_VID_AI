// @vitest-environment jsdom
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { HostMessage } from '../../types';
import { VietsubSubtitleEditor } from './VietsubSubtitleEditor';
import { useVietsubModule } from './useVietsubModule';

const bridge = vi.hoisted(() => ({
  listener: null as null | ((message: HostMessage) => void),
  posts: [] as { type: string; requestId: string }[],
  sequence: 0
}));
vi.mock('../../bridge', () => ({
  isHosted: true,
  postToHost(type: string) {
    const requestId = `request-${++bridge.sequence}`;
    bridge.posts.push({ type, requestId });
    return requestId;
  },
  subscribeToHost(listener: (message: HostMessage) => void) {
    bridge.listener = listener;
    return () => { bridge.listener = null; };
  }
}));

const noOp = () => { };
function Harness() {
  const module = useVietsubModule(true, 'organization');
  const { state } = module;
  return <VietsubSubtitleEditor
    workspace={state.subtitleWorkspace}
    busy={state.loading || state.busy}
    notice={state.subtitleNotice}
    noticeId={state.noticeEvents?.subtitleNotice?.id}
    sourceLanguageCode="en"
    getPlayheadMilliseconds={() => 0}
    onImportSrt={noOp} onActivateTrack={noOp} onLoadPage={noOp}
    onUpdateCue={async () => true} onSplitCue={noOp} onAlignCue={noOp}
    onDuplicateCue={noOp} onDeleteCue={noOp} onExportSrt={noOp}
    canExportVideo={true} onExportVideo={module.exportVideo}
    canDesignSubtitle={true} onOpenSubtitleDesigner={noOp}
    onSelectCue={noOp} onSaveStateChange={noOp}
  />;
}

let root: Root;
let container: HTMLDivElement;
const button = () => container.querySelector<HTMLButtonElement>('.vietsub-subtitle-export-trigger')!;
const exports = () => bridge.posts.filter(post => post.type === 'vietsub.video.export');
const stateMessage = (requestId?: string, busy = false): HostMessage => ({
  type: 'vietsub.state', requestId, payload: {
    enabled: true, busy, activeOperationRequestId: busy ? requestId : null, jobs: [],
    selectedProject: { projectId: 'project', name: 'Export fixture' },
    subtitleWorkspace: {
      activeTrackId: 'track', tracks: [{
        trackId: 'track', revision: 1, source: 'IMPORTED_SRT', languageCode: 'en',
        displayName: 'Translated', cueCount: 1, translatedCueCount: 1, warningCueCount: 0,
        updatedAtUtc: new Date(0).toISOString()
      }]
    }
  }
});
async function emit(message: HostMessage) {
  await act(async () => { bridge.listener!(message); });
}
async function startExport() {
  await act(async () => button().click());
  const requestId = exports().at(-1)!.requestId;
  await emit(stateMessage(requestId, true));
  expect(button().disabled).toBe(true);
  expect(button().textContent).toContain('Đang xuất');
  return requestId;
}
beforeEach(async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  Element.prototype.scrollTo = vi.fn();
  bridge.posts = []; bridge.sequence = 0;
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
  await act(async () => root.render(<Harness />));
  await emit(stateMessage());
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe('video export completion through the desktop bridge', () => {
  it('shows the saved filename and releases the spinner only after the matching operation completes', async () => {
    const requestId = await startExport();
    await emit({ type: 'vietsub.video.export.started', requestId });
    expect(container.textContent).toContain('Đang kết xuất MP4');
    await emit({ type: 'vietsub.video.export.completed', requestId, payload: { fileName: 'finished.mp4' } });
    await emit(stateMessage(requestId));
    await emit({ type: 'vietsub.operation.completed', requestId: 'unrelated-request', payload: { completed: true } });
    expect(button().disabled).toBe(true);
    await emit({ type: 'vietsub.operation.completed', requestId, payload: { completed: true } });
    expect(button().disabled).toBe(false);
    expect(button().textContent?.trim()).toBe('Xuất video');
    expect(container.textContent).toContain('Đã xuất video thành công: finished.mp4');
    expect(container.textContent).not.toContain('Đang kết xuất MP4');
    await startExport();
    expect(exports()).toHaveLength(2);
  });

  it('releases the spinner after cancelling the save dialog without reporting success', async () => {
    const requestId = await startExport();
    await emit({ type: 'vietsub.video.export.cancelled', requestId, payload: { cancelled: true } });
    await emit(stateMessage(requestId));
    await emit({ type: 'vietsub.operation.completed', requestId, payload: { completed: true } });
    expect(button().disabled).toBe(false);
    expect(button().textContent?.trim()).toBe('Xuất video');
    expect(container.textContent).not.toContain('Đã xuất video thành công');
    await startExport();
    expect(exports()).toHaveLength(2);
  });

  it('shows render errors and permits retry without needing a success event', async () => {
    const requestId = await startExport();
    await emit({ type: 'vietsub.video.export.started', requestId });
    await emit(stateMessage(requestId));
    await emit({ type: 'vietsub.error', requestId, error: {
      code: 'vietsub_export_render_failed', message: 'Không thể kết xuất video. Hãy thử lại.'
    } });
    expect(button().disabled).toBe(false);
    expect(button().textContent?.trim()).toBe('Xuất video');
    expect(container.textContent).toContain('Không thể kết xuất video. Hãy thử lại.');
    expect(container.textContent).not.toContain('Đã xuất video thành công');
    await startExport();
    expect(exports()).toHaveLength(2);
  });
});
