import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
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
import type { VietsubMediaSummary, VietsubVoiceWorkspace } from './types';

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

const renderTimeline = (
  media: VietsubMediaSummary,
  voiceWorkspace: VietsubVoiceWorkspace | null = null,
  voiceEnabled = false
) => renderToStaticMarkup(createElement(
  VietsubTimeline,
  {
    media,
    trackId: null,
    window: null,
    playheadMilliseconds: 0,
    playing: false,
    voiceWorkspace,
    voiceEnabled,
    busy: false,
    selectedCueId: null,
    onSeek: () => { },
    onSelectCue: () => { },
    onLoadWindow: () => { },
    onRequestThumbnails: () => { },
    onRequestWaveform: () => { },
    onUpdateCue: async () => true,
    onToggleVoice: () => { }
  }
));

describe('VietsubTimeline media artifacts', () => {
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
    const html = renderTimeline(createMedia('READY'), createVoiceWorkspace(), true);
    const subtitleTrackPosition = html.indexOf('vietsub-timeline-subtitle-track');
    const voiceTrackPosition = html.indexOf('data-vietsub-generated-voice-track="true"');

    expect(voiceTrackPosition).toBeGreaterThan(subtitleTrackPosition);
    expect(html).toContain('Giọng Việt · Piper local');
    expect(html).toContain('data-vietsub-voice-waveform="true"');
    expect(html).toContain('aria-label="Tắt Giọng Việt"');
    expect(html).not.toContain('<audio');
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
