// @vitest-environment jsdom
import { act, createElement, type ComponentProps } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VietsubSubtitleDesignerModal } from './VietsubSubtitleDesignerModal';
import { VietsubEditorWorkspace } from './VietsubEditorWorkspace';
import type { VietsubModuleState, VietsubProjectSummary } from './types';
import { defaultVietsubAudioMixSettings } from './vietsubAudioMix';
import { defaultVietsubSubtitleStyle } from './vietsubSubtitleStyle';

let container: HTMLDivElement;
let root: Root;
const paused = new WeakMap<HTMLMediaElement, boolean>();

beforeEach(() => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('AudioContext', undefined);
  vi.spyOn(window, 'requestAnimationFrame').mockImplementation(callback => window.setTimeout(() => callback(performance.now()), 16));
  vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(id => window.clearTimeout(id));
  vi.stubGlobal('ResizeObserver', class { observe() { } disconnect() { } });
  Object.defineProperty(HTMLElement.prototype, 'scrollTo', { configurable: true, value: vi.fn() });
  vi.spyOn(HTMLMediaElement.prototype, 'paused', 'get').mockImplementation(function (this: HTMLMediaElement) {
    return paused.get(this) ?? true;
  });
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(function (this: HTMLMediaElement) {
    paused.set(this, false);
    this.dispatchEvent(new Event('play'));
    return Promise.resolve();
  });
  vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(function (this: HTMLMediaElement) {
    if (paused.get(this) === false) {
      paused.set(this, true);
      this.dispatchEvent(new Event('pause'));
    }
  });
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  Reflect.deleteProperty(HTMLElement.prototype, 'scrollTo');
});

async function click(text: string) {
  const button = [...container.querySelectorAll('button')].find(item => (
    item.getAttribute('aria-label') === text || item.textContent?.trim() === text
  ));
  expect(button, text).toBeDefined();
  await act(async () => button!.click());
}

async function openDesigner(muted: boolean) {
  await act(async () => root.render(createElement(VietsubSubtitleDesignerModal, {
    media: {
      mediaId: 'media', fileName: 'video.mp4', importMode: 'COPY', sizeBytes: 1024,
      sha256: 'a'.repeat(64), durationSeconds: 9, width: 720, height: 1280,
      hasAudio: true, sourceAvailable: true, sourceChanged: false, rotationDegrees: 0,
      playbackUrl: 'https://vietsub-media.app.local/video', thumbnailUrls: [],
      timelineThumbnails: [], thumbnailProfileVersion: 1, thumbnailCount: 0,
      waveformProfileVersion: 1, waveformRevision: 0, waveformStatus: 'READY'
    },
    style: defaultVietsubSubtitleStyle,
    audioMixSettings: { ...defaultVietsubAudioMixSettings, translatedVoiceMuted: muted },
    voicePlaybackUrl: 'https://vietsub-media.app.local/voice.wav',
    previewText: 'Fixture', hasTranslatedSubtitles: true, initialTimeMilliseconds: 0,
    busy: false, onPreviewTimeChange: () => { }, onSave: async () => true,
    onExportVideo: () => { }, onCancelOperation: () => { }, onClose: () => { }
  })));
}

async function openWorkspace(mediaId: string) {
  const project: VietsubProjectSummary = {
    projectId: 'project', name: 'Playback fixture', status: 'READY', sourceLanguageCode: 'en', targetLanguageCode: 'vi',
    updatedAtUtc: new Date(0).toISOString(), needsRecovery: false, serverSynchronized: true,
    sourceVideo: {
      mediaId, fileName: 'video.mp4', importMode: 'COPY', sizeBytes: 1024,
      sha256: 'a'.repeat(64), durationSeconds: 9, width: 720, height: 1280,
      hasAudio: true, sourceAvailable: true, sourceChanged: false, rotationDegrees: 0,
      playbackUrl: `https://vietsub-media.app.local/${mediaId}`, thumbnailUrls: [],
      timelineThumbnails: [], thumbnailProfileVersion: 1, thumbnailCount: 0,
      waveformProfileVersion: 1, waveformRevision: 0, waveformStatus: 'READY'
    }
  };
  const state: VietsubModuleState = {
    enabled: true, initialized: true, loading: false, busy: false, stage: 'shell_ready', projects: [project],
    selectedProject: project, subtitleStyle: defaultVietsubSubtitleStyle, audioMixSettings: defaultVietsubAudioMixSettings,
    ocrSettings: { languageCode: 'en', profile: 'BALANCED', region: { x: 0, y: 0.6, width: 1, height: 0.4 } }, jobs: [],
    voiceWorkspace: {
      settings: { engineId: 'PIPER_LOCAL', modelId: 'model', voiceId: 'voice', maximumPhraseGapMilliseconds: 500,
        maximumPhraseDurationMilliseconds: 8000, maximumPhraseCharacters: 4500, maximumBorrowedGapMilliseconds: 600,
        preferredMaximumTempo: 1.12, maximumTempo: 1.2, trimSilence: true },
      voices: [], timingDiagnostics: [], timelinePlaybackUrl: 'https://vietsub-media.app.local/voice.wav',
      timeline: { artifactId: 'artifact', trackId: 'track', trackRevision: 1, artifactKind: 'TIMELINE', sizeBytes: 1000,
        sha256: 'a'.repeat(64), durationMilliseconds: 9000, sampleRate: 48000, channels: 1, status: 'READY', updatedAtUtc: new Date(0).toISOString() }
    }
  };
  const noOp = () => { };
  const saved = async () => true;
  const props: ComponentProps<typeof VietsubEditorWorkspace> = {
    project, state, onRefresh: noOp, onCreateProject: noOp, onOpenProject: noOp, onRenameProject: noOp,
    onCloseProject: saved, onImportMedia: noOp, onUpdateOcrSettings: saved, onPreviewOcr: noOp, onStartOcr: noOp,
    onStartTranslation: noOp, onInstallTranslationRuntime: noOp, onStartVoice: noOp, onInstallVoiceRuntime: noOp,
    onDismissTranslationResourceAlert: noOp, onContinueTranslationAfterResourceWarning: noOp,
    onPauseJob: noOp, onResumeJob: noOp, onRetryJob: noOp, onCancelJob: noOp, onActivateOcrTrack: noOp,
    onImportSrt: noOp, onActivateSubtitleTrack: noOp, onLoadSubtitlePage: noOp, onLoadTimelineWindow: noOp,
    onRequestTimelineThumbnails: noOp, onRequestTimelineWaveform: noOp, onUpdateSubtitleCue: saved,
    onUpdateSubtitleStyle: saved, onUpdateTimelineCue: saved, onSplitSubtitleCue: noOp, onAlignSubtitleCue: noOp,
    onDuplicateSubtitleCue: noOp, onDeleteSubtitleCue: noOp, onExportSrt: noOp, onExportVideo: async () => true,
    onCancelOperation: noOp, onRegisterBeforeLeave: () => noOp
  };
  await act(async () => root.render(createElement(VietsubEditorWorkspace, props)));
  return props;
}

describe('Vietsub editor export controls', () => {
  it('shares draft saving, progress and duplicate protection between the timeline and subtitle buttons', async () => {
    const props = await openWorkspace('export-video');
    let finishSave!: (saved: boolean) => void;
    let finishExport!: (saved: boolean) => void;
    const save = vi.fn(() => new Promise<boolean>(resolve => { finishSave = resolve; }));
    const exportVideo = vi.fn(() => new Promise<boolean>(resolve => { finishExport = resolve; }));
    const state: VietsubModuleState = { ...props.state,
      subtitleWorkspace: { activeTrackId: 'track', tracks: [{ trackId: 'track', displayName: 'Fixture',
        source: 'PADDLE_OCR_LOCAL', languageCode: 'en', revision: 1, cueCount: 1, translatedCueCount: 1,
        warningCueCount: 0, updatedAtUtc: '' }] },
      subtitlePage: { trackId: 'track', trackRevision: 1, offset: 0, pageSize: 50, totalCount: 1,
        search: '', status: 'ALL', speaker: '', speakers: [], cues: [{ cueId: 'cue', cueIndex: 0,
          startMilliseconds: 0, endMilliseconds: 1000, originalText: 'Hello', translatedText: 'Xin chào',
          speaker: 'speaker_1', originalLocked: false, translationLocked: false, warnings: [], updatedAtUtc: '' }] }
    };
    await act(async () => root.render(createElement(VietsubEditorWorkspace, {
      ...props, state, onUpdateSubtitleCue: save, onExportVideo: exportVideo
    })));
    const field = container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(field, 'Bản dịch mới nhất');
      field.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const timelineButton = container.querySelector<HTMLButtonElement>('.vietsub-timeline-export-trigger')!;
    const editorButton = container.querySelector<HTMLButtonElement>('.vietsub-subtitle-export-trigger')!;
    expect(field.value).toBe('Bản dịch mới nhất');
    expect(container.textContent).toContain('Chưa lưu');
    expect(timelineButton.disabled).toBe(false);
    expect(editorButton.disabled).toBe(false);
    await act(async () => { timelineButton.click(); editorButton.click(); });
    expect(save).toHaveBeenCalledTimes(1);
    expect(exportVideo).not.toHaveBeenCalled();
    expect(timelineButton.disabled).toBe(true);
    expect(editorButton.disabled).toBe(true);
    await act(async () => finishSave(false));
    expect(timelineButton.disabled).toBe(false);
    expect(editorButton.disabled).toBe(false);
    expect(exportVideo).not.toHaveBeenCalled();
    await act(async () => timelineButton.click());
    await act(async () => finishSave(true));
    expect(save).toHaveBeenLastCalledWith(expect.objectContaining({ translatedText: 'Bản dịch mới nhất' }));
    expect(exportVideo).toHaveBeenCalledTimes(1);
    expect(timelineButton.textContent).toBe('Đang xuất…');
    expect(editorButton.textContent).toBe('Đang xuất…');
    await act(async () => { timelineButton.click(); editorButton.click(); });
    expect(exportVideo).toHaveBeenCalledTimes(1);
    await act(async () => finishExport(true));
    expect(timelineButton.disabled).toBe(false);
    await act(async () => editorButton.click());
    expect(exportVideo).toHaveBeenCalledTimes(2);
    expect(timelineButton.disabled).toBe(true);
    await act(async () => finishExport(false));
    expect(timelineButton.disabled).toBe(false);
    expect(editorButton.disabled).toBe(false);
  });
});

describe('Vietsub voice playback while editing the mix', () => {
  it('keeps the saved voice enabled when the editor reloads the source video', async () => {
    await openWorkspace('first-video');
    await click('Phát');
    expect(container.querySelector('audio')!.paused).toBe(false);
    await openWorkspace('reopened-video');
    await click('Phát');
    expect(container.querySelector('audio')!.paused).toBe(false);
    expect(container.querySelector('audio')!.muted).toBe(false);
  });

  it('pauses voice while video buffers and resumes at the video clock', async () => {
    await openDesigner(false);
    await click('Phát video');
    const video = container.querySelector('video')!;
    const audio = container.querySelector('audio')!;
    await act(async () => video.dispatchEvent(new Event('waiting')));
    expect(audio.paused).toBe(true);
    video.currentTime = 2;
    await act(async () => video.dispatchEvent(new Event('playing')));
    expect(audio.paused).toBe(false);
    expect(audio.currentTime).toBe(2);
  });

  it('reports failed voice loading and reloads it without resetting the video', async () => {
    const load = vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    await openDesigner(false);
    await click('Phát video');
    const video = container.querySelector('video')!;
    const audio = container.querySelector('audio')!;
    video.currentTime = 3;
    await act(async () => audio.dispatchEvent(new Event('error')));
    expect(audio.paused).toBe(true);
    expect(container.textContent).toContain('Không tải được giọng đã tạo');
    await click('Thử lại giọng');
    expect(load).toHaveBeenCalledTimes(1);
    await act(async () => audio.dispatchEvent(new Event('canplay')));
    expect(video.currentTime).toBe(3);
    expect(video.paused).toBe(false);
    expect(audio.paused).toBe(false);
    expect(audio.currentTime).toBe(3);
  });
  it('does not block video playback while AudioContext resume is pending', async () => {
    vi.stubGlobal('AudioContext', class {
      state = 'suspended'; currentTime = 0; destination = {};
      createMediaElementSource() { return { connect() { } }; }
      createGain() { return { connect() { }, gain: { cancelScheduledValues() { }, setTargetAtTime() { } } }; }
      createAnalyser() { return { fftSize: 512 }; }
      resume() { return new Promise<void>(() => { }); }
      close() { return Promise.resolve(); }
    });
    await openDesigner(false);
    await click('Phát video');
    expect(container.querySelector('video')!.paused).toBe(false);
    await click('Tạm dừng');
    expect(container.querySelector('audio')!.paused).toBe(true);
  });

  it('restarts a finished voice after seeking back while video is still playing', async () => {
    await openDesigner(false);
    await click('Phát video');
    const video = container.querySelector('video')!;
    const audio = container.querySelector('audio')!;
    paused.set(audio, true);
    video.currentTime = 1;
    await act(async () => video.dispatchEvent(new Event('seeked')));
    expect(audio.paused).toBe(false);
    expect(audio.currentTime).toBe(1);
  });

  it('starts voice when it becomes ready after an interrupted load', async () => {
    await openDesigner(false);
    await click('Phát video');
    const video = container.querySelector('video')!;
    const audio = container.querySelector('audio')!;
    paused.set(audio, true);
    video.currentTime = 2;
    await act(async () => audio.dispatchEvent(new Event('canplay')));
    expect(audio.paused).toBe(false);
    expect(audio.currentTime).toBe(2);
  });
  it('ducks original audio only when the voice contains audible samples', async () => {
    vi.useFakeTimers();
    let amplitude = 0;
    vi.stubGlobal('AudioContext', class {
      state = 'running'; currentTime = 0; destination = {};
      createMediaElementSource() { return { connect() { } }; }
      createGain() {
        const level = { value: 0 };
        return { connect() { }, gain: { cancelScheduledValues() { }, setTargetAtTime(value: number) { level.value = value; } } };
      }
      createAnalyser() { return { fftSize: 512, getFloatTimeDomainData(samples: Float32Array) { samples.fill(amplitude); } }; }
      close() { return Promise.resolve(); }
      resume() { return Promise.resolve(); }
    });
    await openDesigner(false);
    await click('Phát video');
    const video = container.querySelector('video')!;
    await act(async () => video.dispatchEvent(new Event('loadedmetadata')));
    // The production mixer ramps from the element's initial volume over 140 ms.
    // Drive that clock explicitly so assertions do not race a scheduled animation frame.
    await act(async () => vi.advanceTimersByTimeAsync(180));
    // Subtitles remain visible throughout; silence must preserve the original mix level.
    expect(video.volume).toBeCloseTo(0.25);
    amplitude = 0.05;
    await act(async () => vi.advanceTimersByTimeAsync(70));
    await act(async () => vi.advanceTimersByTimeAsync(180));
    expect(video.volume).toBeCloseTo(0.0875);
    amplitude = 0;
    await act(async () => vi.advanceTimersByTimeAsync(70));
    await act(async () => vi.advanceTimersByTimeAsync(180));
    expect(video.volume).toBeCloseTo(0.25);
    expect(container.querySelector('audio')!.paused).toBe(false);
  });
  it('starts the voice at the current video time when unmuted during playback', async () => {
    await openDesigner(true);
    await click('Phát video');
    const video = container.querySelector('video')!;
    const audio = container.querySelector('audio')!;
    expect(video.paused).toBe(false);
    expect(audio.paused).toBe(true);
    video.currentTime = 3;
    await click('Âm thanh');
    await click('Bật Giọng dịch tiếng Việt');
    expect(audio.paused).toBe(false);
    expect(audio.currentTime).toBe(3);
    expect(audio.volume).toBe(1);
    expect(audio.muted).toBe(false);
  });

  it('pauses a muted voice and resumes it at the video playhead without restarting video', async () => {
    await openDesigner(false);
    await click('Phát video');
    const video = container.querySelector('video')!;
    const audio = container.querySelector('audio')!;
    expect(audio.paused).toBe(false);
    await click('Âm thanh');
    await click('Tắt Giọng dịch tiếng Việt');
    expect(audio.paused).toBe(true);
    expect(video.paused).toBe(false);
    video.currentTime = 4;
    await click('Bật Giọng dịch tiếng Việt');
    expect(audio.paused).toBe(false);
    expect(audio.currentTime).toBe(4);
  });
});
