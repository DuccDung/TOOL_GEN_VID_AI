import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { VietsubSubtitleDesignerModal } from './VietsubSubtitleDesignerModal';
import { defaultVietsubSubtitleStyle } from './vietsubSubtitleStyle';
import { defaultVietsubAudioMixSettings } from './vietsubAudioMix';
import { defaultVietsubVideoTransformSettings } from './vietsubVideoTransform';

const media = {
  mediaId: 'media-1',
  fileName: 'video.mp4',
  importMode: 'COPY' as const,
  sizeBytes: 1024,
  sha256: 'a'.repeat(64),
  durationSeconds: 9,
  width: 720,
  height: 1280,
  hasAudio: true,
  sourceAvailable: true,
  sourceChanged: false,
  playbackUrl: 'https://vietsub-media.app.local/video',
  thumbnailUrls: [],
  timelineThumbnails: [],
  waveformStatus: 'READY' as const,
  rotationDegrees: 0,
  thumbnailProfileVersion: 1,
  thumbnailCount: 0,
  waveformProfileVersion: 1,
  waveformRevision: 0
};

describe('Vietsub subtitle designer modal', () => {
  it('hiển thị video, tua, preset và thao tác lưu rõ ràng', () => {
    const html = renderToStaticMarkup(createElement(VietsubSubtitleDesignerModal, {
      media,
      style: defaultVietsubSubtitleStyle,
      audioMixSettings: defaultVietsubAudioMixSettings,
      videoTransformSettings: defaultVietsubVideoTransformSettings,
      voicePlaybackUrl: 'https://vietsub-media.app.local/voice.wav',
      previewText: 'Phụ đề đang xem trước',
      hasTranslatedSubtitles: true,
      initialTimeMilliseconds: 2_000,
      busy: false,
      onPreviewTimeChange: () => { },
      onSave: async () => true,
      onExportVideo: () => { },
      onCancelOperation: () => { },
      onClose: () => { }
    }));

    expect(html).toContain('Phụ đề, hình ảnh và âm thanh');
    expect(html).toContain('aria-label="Tua video xem trước phụ đề"');
    expect(html).toContain('class="vietsub-subtitle-designer-stage is-portrait"');
    expect(html).toContain('aspect-ratio:0.5625');
    expect(html).toContain('9:16');
    expect(html).toContain('720 × 1280');
    expect(html).toContain('Dễ đọc');
    expect(html).toContain('Video dọc');
    expect(html).toContain('Hiệu ứng');
    expect(html).toContain('Âm thanh');
    expect(html).toContain('crossorigin="anonymous"');
    expect(html).toContain('Kéo trực tiếp phụ đề');
    expect(html).toContain('Xuất MP4');
    expect(html).toContain('Lưu thay đổi');
    expect(html).toContain('Lật trái–phải');
    expect(html).toContain('Lật trên–dưới');
    expect(html).not.toContain('app.local says');
  });
});
