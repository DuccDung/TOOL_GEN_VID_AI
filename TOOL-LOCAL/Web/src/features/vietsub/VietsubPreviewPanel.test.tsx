import { createElement, createRef } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { VietsubPreviewPanel } from './VietsubPreviewPanel';

const noOp = () => { };

describe('Vietsub preview panel', () => {
  it('ưu tiên video và control, không lặp thông tin file hoặc metadata ở đáy card', () => {
    const html = renderToStaticMarkup(createElement(VietsubPreviewPanel, {
      project: {
        projectId: 'project',
        name: 'Preview project',
        status: 'READY',
        sourceLanguageCode: 'zh',
        targetLanguageCode: 'vi',
        updatedAtUtc: new Date(0).toISOString(),
        needsRecovery: false,
        serverSynchronized: true,
        sourceVideo: {
          mediaId: 'media',
          fileName: 'HEADER-MUST-BE-HIDDEN.mp4',
          importMode: 'COPY',
          sizeBytes: 24_000_000,
          sha256: 'a'.repeat(64),
          durationSeconds: 9,
          width: 720,
          height: 1280,
          framesPerSecond: 30,
          videoCodec: 'h264',
          audioCodec: 'aac',
          hasAudio: true,
          sourceAvailable: true,
          sourceChanged: false,
          playbackUrl: 'https://vietsub-media.app.local/projects/project/media/media/video',
          thumbnailUrls: [],
          timelineThumbnails: [],
          waveformStatus: 'READY',
          rotationDegrees: 0,
          thumbnailProfileVersion: 1,
          thumbnailCount: 0,
          waveformProfileVersion: 1,
          waveformRevision: 1
        }
      },
      videoRef: createRef<HTMLVideoElement>(),
      busy: false,
      playheadMilliseconds: 0,
      durationMilliseconds: 9_000,
      playing: false,
      playbackRate: 1,
      volume: 0.8,
      muted: false,
      subtitlesVisible: true,
      onImportMedia: noOp,
      onPlayheadChange: noOp,
      onDurationChange: noOp,
      onPlayingChange: noOp,
      onTogglePlaying: noOp,
      onSeek: noOp,
      onPlaybackRateChange: noOp,
      onVolumeChange: noOp,
      onToggleMuted: noOp,
      onToggleSubtitles: noOp
    }));

    expect(html).toContain('class="vietsub-editor-video-stage"');
    expect(html).toContain('class="vietsub-playback-controls"');
    expect(html).toContain('class="vietsub-playback-rate"');
    expect(html).toContain('aria-label="Tốc độ phát"');
    expect(html).toContain('<option value="1" selected="">1×</option>');
    expect(html).not.toContain('XEM TRƯỚC');
    expect(html).not.toContain('HEADER-MUST-BE-HIDDEN.mp4');
    expect(html).not.toContain('Đã sao chép');
    expect(html).not.toContain('vietsub-preview-meta');
    expect(html).not.toContain('Video H264');
    expect(html).not.toContain('30.00 fps');
  });
});
