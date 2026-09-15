// @vitest-environment jsdom
import { act, createElement, type ComponentProps } from 'react';
import { createRoot } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { VietsubTimeline } from './VietsubTimeline';
import {
  initialTimelineMediaLoadState,
  markTimelineMediaFailed,
  markTimelineMediaLoaded,
  markTimelineMediaReady,
  prioritizeTimelineThumbnailIndices,
  retryTimelineMedia,
  selectTimelineThumbnailIndices,
  shouldResetTimelineMediaState
} from './timelineMediaState';
import type { VietsubMediaSummary, VietsubTimelineWindow, VietsubVoiceWorkspace } from './types';
import { defaultVietsubAudioMixSettings } from './vietsubAudioMix';

const createMedia = (
  waveformStatus: VietsubMediaSummary['waveformStatus'] = 'READY',
  thumbnailCount = 12
): VietsubMediaSummary => ({
  mediaId: 'media-a',
  fileName: 'video.mp4',
  importMode: 'LINK',
  sizeBytes: 1024,
  sha256: 'a'.repeat(64),
  durationSeconds: 12,
  width: 1920,
  height: 1080,
  hasAudio: waveformStatus !== 'NO_AUDIO',
  sourceAvailable: true,
  sourceChanged: false,
  playbackUrl: 'https://vietsub-media.app.local/projects/p/media/m',
  thumbnailUrls: [],
  timelineThumbnails: Array.from({ length: thumbnailCount }, (_, index) => ({
    index,
    profileVersion: 1,
    sourceSha256: 'a'.repeat(64),
    url: `https://vietsub-media.app.local/thumbnail-${index}.jpg`,
    revision: 100 + index,
    timestampMilliseconds: index * 1000 + 500,
    startMilliseconds: index * 1000,
    endMilliseconds: (index + 1) * 1000
  })),
  waveformUrl: waveformStatus === 'READY'
    ? 'https://vietsub-media.app.local/waveform.png'
    : null,
  waveformStatus,
  rotationDegrees: 0,
  thumbnailProfileVersion: 1,
  thumbnailCount: 12,
  waveformProfileVersion: 1,
  waveformRevision: 200
});

const createVoiceWorkspace = (): VietsubVoiceWorkspace => ({
  settings: {
    engineId: 'PIPER_LOCAL',
    modelId: 'piper-vi-vais1000-medium',
    voiceId: 'piper:vi-vn-vais1000',
    maximumPhraseGapMilliseconds: 500,
    maximumPhraseDurationMilliseconds: 8000,
    maximumPhraseCharacters: 4500,
    maximumBorrowedGapMilliseconds: 600,
    preferredMaximumTempo: 1.12,
    maximumTempo: 1.2,
    trimSilence: true
  },
  voices: [],
  timeline: {
    artifactId: 'voice-timeline',
    trackId: 'track',
    trackRevision: 4,
    artifactKind: 'TIMELINE',
    sizeBytes: 4096,
    sha256: 'b'.repeat(64),
    durationMilliseconds: 12_000,
    sampleRate: 48_000,
    channels: 2,
    status: 'READY',
    updatedAtUtc: new Date(0).toISOString()
  },
  timelinePlaybackUrl: 'https://vietsub-media.app.local/projects/p/voice/timeline.wav',
  timingDiagnostics: []
});

const createTimelineWindow = (): VietsubTimelineWindow => ({
  trackId: 'track',
  trackRevision: 4,
  windowStartMilliseconds: 0,
  windowEndMilliseconds: 12_000,
  truncated: false,
  cues: [
    {
      cueId: 'cue-1',
      cueIndex: 0,
      startMilliseconds: 500,
      endMilliseconds: 2_000,
      locked: false,
      hasWarnings: false,
      hasTranslation: true,
      previewText: 'Câu thứ nhất'
    },
    {
      cueId: 'cue-2',
      cueIndex: 1,
      startMilliseconds: 3_200,
      endMilliseconds: 5_000,
      locked: false,
      hasWarnings: false,
      hasTranslation: true,
      previewText: 'Câu thứ hai'
    }
  ]
});

const timelineElement = (
  media: VietsubMediaSummary,
  voiceWorkspace: VietsubVoiceWorkspace | null = null,
  voiceEnabled = false,
  timelineWindow: VietsubTimelineWindow | null = null,
  onUpdateCueVoice?: (ids: string[], enabled: boolean, trackId: string, revision: number) => Promise<boolean>,
  overrides: Partial<ComponentProps<typeof VietsubTimeline>> = {}
) => createElement(
  VietsubTimeline,
  {
    media,
    trackId: timelineWindow?.trackId ?? null,
    window: timelineWindow,
    playheadMilliseconds: 0,
    playing: false,
    voiceWorkspace,
    voiceEnabled,
    audioMixSettings: defaultVietsubAudioMixSettings,
    busy: false,
    canExportVideo: true,
    exporting: false,
    onExportVideo: () => { },
    selectedCueId: null,
    onSeek: () => { },
    onSelectCue: () => { },
    onLoadWindow: () => { },
    onRequestThumbnails: () => { },
    onRequestWaveform: () => { },
    onUpdateCue: async () => true,
    onUpdateCueVoice,
    onToggleVoice: () => { },
    onPreviewAudioMix: () => { },
    onUpdateAudioMix: async () => true,
    ...overrides
  }
);
const renderTimeline = (...args: Parameters<typeof timelineElement>) => renderToStaticMarkup(timelineElement(...args));

describe('VietsubTimeline media artifacts', () => {
  it('opens a text editor on a left click, while drag, resize and right click keep their own actions', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() { } disconnect() { } });
    vi.stubGlobal('PointerEvent', class extends MouseEvent {
      readonly pointerId: number;
      constructor(type: string, init: MouseEventInit & { pointerId?: number } = {}) {
        super(type, init);
        this.pointerId = init.pointerId ?? 1;
      }
    });
    HTMLElement.prototype.setPointerCapture = vi.fn();
    HTMLElement.prototype.hasPointerCapture = vi.fn(() => false);
    HTMLElement.prototype.releasePointerCapture = vi.fn();
    const container = document.createElement('div'); document.body.append(container);
    const root = createRoot(container);
    const open = vi.fn();
    const select = vi.fn();
    const update = vi.fn(async () => true);
    const dispatch = async (target: EventTarget, type: string, clientX: number) => {
      await act(async () => target.dispatchEvent(new PointerEvent(type, {
        bubbles: true, button: 0, clientX, pointerId: 1
      })));
    };
    try {
      await act(async () => root.render(timelineElement(createMedia(), null, false, createTimelineWindow(), undefined,
        { onOpenCueEditor: open, onSelectCue: select, onUpdateCue: update })));
      const cue = container.querySelector<HTMLButtonElement>('[aria-label^="Cue 1,"]')!;
      await dispatch(cue, 'pointerdown', 40);
      await dispatch(window, 'pointerup', 40);
      expect(open).toHaveBeenCalledWith(expect.objectContaining({ cueId: 'cue-1' }), cue);
      expect(select).not.toHaveBeenCalled();

      await dispatch(cue, 'pointerdown', 40);
      await dispatch(window, 'pointermove', 60);
      await dispatch(window, 'pointerup', 60);
      expect(open).toHaveBeenCalledTimes(1);
      expect(update).toHaveBeenCalledTimes(1);

      await dispatch(cue.querySelector('.resize-start')!, 'pointerdown', 40);
      await dispatch(window, 'pointerup', 40);
      expect(open).toHaveBeenCalledTimes(1);
      expect(select).toHaveBeenCalledWith('cue-1', 500, 0);

      await act(async () => cue.click());
      expect(open).toHaveBeenCalledTimes(2);
      await act(async () => cue.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, button: 2 })));
      expect(open).toHaveBeenCalledTimes(2);
    } finally {
      await act(async () => root.unmount());
      container.remove();
      delete (HTMLElement.prototype as Partial<HTMLElement>).setPointerCapture;
      delete (HTMLElement.prototype as Partial<HTMLElement>).hasPointerCapture;
      delete (HTMLElement.prototype as Partial<HTMLElement>).releasePointerCapture;
      vi.unstubAllGlobals();
    }
  });
  it('mở menu chuột phải và gửi lựa chọn giọng cho đúng câu', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() { } disconnect() { } });
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const update = vi.fn(async () => true);
    const timeline = createTimelineWindow();
    try {
      await act(async () => root.render(timelineElement(createMedia(), createVoiceWorkspace(), true, timeline, update)));
      const cue = container.querySelector<HTMLButtonElement>('[aria-label^="Cue 1,"]')!;
      await act(async () => cue.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 50, clientY: 50 })));
      expect(container.querySelector('[role="menuitem"]')?.textContent).toBe('Bỏ qua tạo giọng');
      await act(async () => container.querySelector<HTMLButtonElement>('[role="menuitem"]')!.click());
      expect(update).toHaveBeenCalledWith(['cue-1'], false, timeline.trackId, timeline.trackRevision);
      expect(container.querySelector('[role="menu"]')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
      vi.unstubAllGlobals();
    }
  });
  it('giữ phụ đề và hiển thị khoảng không tạo giọng riêng biệt', () => {
    const window = createTimelineWindow();
    window.cues[0].voiceEnabled = false;
    const html = renderTimeline(createMedia('READY'), createVoiceWorkspace(), true, window);
    expect(html).toContain('Không tạo giọng');
    expect(html).toContain('is-skipped');
    expect(html.match(/data-vietsub-voice-clip="true"/g)).toHaveLength(2);
    expect(html.match(/data-vietsub-voice-waveform="true"/g)).toHaveLength(1);
  });
  it('renders all thumbnail URLs and hides browser alt text', () => {
    const html = renderTimeline(createMedia('READY'));

    expect(html.match(/data-vietsub-thumbnail="true"/g)).toHaveLength(12);
    expect(html).toContain('aria-label="Frame video tại');
    expect(html).toContain('alt=""');
    expect(html).not.toContain('crossorigin="anonymous"');
    expect(html).toContain('referrerPolicy="no-referrer"');
    expect(html).not.toContain('alt="Frame video tại');
  });

  it.each([
    ['PENDING', 'Đang chuẩn bị waveform…'],
    ['FAILED', 'Chưa thể phân tích âm thanh gốc'],
    ['NO_AUDIO', 'Video không có âm thanh gốc']
  ] as const)('renders %s waveform state without a broken image', (status, message) => {
    const html = renderTimeline(createMedia(status));

    expect(html).toContain(message);
    expect(html).not.toContain('aria-label="Dạng sóng âm thanh gốc"');
  });

  it('renders a ready waveform with an empty alt and accessible label', () => {
    const html = renderTimeline(createMedia('READY'));

    expect(html).toContain('aria-label="Dạng sóng âm thanh gốc"');
    expect(html).not.toContain('crossorigin="anonymous"');
    expect(html).not.toContain('alt="Dạng sóng âm thanh gốc"');
  });

  it('renders the generated Vietnamese voice as a timeline track below subtitles', () => {
    const html = renderTimeline(
      createMedia('READY'),
      createVoiceWorkspace(),
      true,
      createTimelineWindow()
    );
    const subtitleTrackPosition = html.indexOf('vietsub-timeline-subtitle-track');
    const voiceTrackPosition = html.indexOf('data-vietsub-generated-voice-track="true"');

    expect(voiceTrackPosition).toBeGreaterThan(subtitleTrackPosition);
    expect(html.match(/data-vietsub-voice-clip="true"/g)).toHaveLength(2);
    expect(html.match(/data-vietsub-voice-waveform="true"/g)).toHaveLength(2);
    expect(html).toContain('data-vietsub-voice-cue-id="cue-1"');
    expect(html).toContain('data-vietsub-voice-cue-id="cue-2"');
    expect(html.match(/style="left:20px;width:60px"/g)).toHaveLength(2);
    expect(html.match(/style="left:128px;width:72px"/g)).toHaveLength(2);
    expect(html).not.toContain('Giọng Việt · Piper local');
    expect(html).toContain('<span class="is-voice">');
    expect(html).toContain('aria-label="Tắt Giọng Việt"');
    expect(html).not.toContain('<audio');
  });

  it('shows original and translated voice mixing controls in the timeline toolbar', () => {
    const html = renderTimeline(createMedia('READY'), createVoiceWorkspace(), true);

    expect(html).toContain('aria-label="Trộn âm thanh trên timeline"');
    expect(html).toContain('aria-label="Âm lượng âm thanh gốc"');
    expect(html).toContain('aria-label="Âm lượng giọng Việt"');
    expect(html).toContain('<b>Âm gốc</b><output>25%</output>');
    expect(html).toContain('<b>Giọng Việt</b><output>100%</output>');
    expect(html).toContain('Tự hạ nền');
  });

  it('uses bounded recovery and keeps a temporary error retryable', () => {
    const initial = markTimelineMediaReady(initialTimelineMediaLoadState(), 1);
    const loaded = markTimelineMediaLoaded(initial);
    const failed = markTimelineMediaFailed(loaded, 'vietsub_media_browser_load_failed');
    const retry = retryTimelineMedia(failed);
    const readyAgain = markTimelineMediaReady(retry, 1);
    const failedAgain = markTimelineMediaFailed(readyAgain, 'vietsub_media_browser_load_failed');
    const secondRetry = retryTimelineMedia(failedAgain);
    const failedTerminal = markTimelineMediaFailed(
      markTimelineMediaReady(secondRetry, 1),
      'vietsub_media_browser_load_failed'
    );

    expect(failed.phase).toBe('retry_wait');
    expect(retry.phase).toBe('requested');
    expect(failedAgain.phase).toBe('retry_wait');
    expect(failedTerminal.phase).toBe('failed_terminal');
    expect(failedTerminal.retryCount).toBe(3);
  });

  it('does not retry context authorization failures', () => {
    const failed = markTimelineMediaFailed(
      initialTimelineMediaLoadState(true, 1),
      'vietsub_media_session_context_mismatch'
    );

    expect(failed.phase).toBe('failed_terminal');
    expect(retryTimelineMedia(failed)).toBe(failed);
  });

  it('resets image error state only when project media changes', () => {
    expect(shouldResetTimelineMediaState('media-a', 'media-a')).toBe(false);
    expect(shouldResetTimelineMediaState('media-a', 'media-a', 'a'.repeat(64), 'b'.repeat(64))).toBe(true);
    expect(shouldResetTimelineMediaState('media-a', 'media-b')).toBe(true);
    expect(shouldResetTimelineMediaState('media-a', null)).toBe(true);
  });

  it('requests only viewport thumbnail indices plus overscan and prioritizes the center', () => {
    const indices = selectTimelineThumbnailIndices(12, 12_000, 5_000, 7_000, 2);
    const prioritized = prioritizeTimelineThumbnailIndices(indices, 12, 12_000, 6_000);

    expect(indices).toEqual([3, 4, 5, 6, 7, 8, 9]);
    expect(prioritized.slice(0, 2).sort((a, b) => a - b)).toEqual([5, 6]);
    expect(prioritized).toHaveLength(indices.length);
  });

  it('keeps thumbnail geometry on a fixed wrapper independent of image load state', () => {
    const html = renderTimeline(createMedia('PENDING', 1));

    expect(html).toMatch(/class="vietsub-timeline-thumbnail is-ready"[^>]*style="left:0px;width:[^"]+"/);
  });
});
