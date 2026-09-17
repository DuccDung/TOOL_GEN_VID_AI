import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import {
  VietsubOcrScanModal,
  VietsubSettingsPanel,
  VietsubTranslationModeModal
} from './VietsubSettingsPanel';

const noOp = () => { };
type SettingsPanelProps = Parameters<typeof VietsubSettingsPanel>[0];

const sourceVideo = {
  mediaId: 'media',
  fileName: 'video-nguon.mp4',
  importMode: 'COPY' as const,
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
  waveformStatus: 'READY' as const,
  rotationDegrees: 0,
  thumbnailProfileVersion: 1,
  thumbnailCount: 0,
  waveformProfileVersion: 1,
  waveformRevision: 1
};

function renderSettings(
  subtitleWorkspace: SettingsPanelProps['subtitleWorkspace'],
  overrides: Partial<SettingsPanelProps> = {}
) {
  const props: SettingsPanelProps = {
    project: {
      projectId: 'project',
      name: 'UI project',
      status: 'READY',
      sourceLanguageCode: 'zh',
      targetLanguageCode: 'vi',
      updatedAtUtc: new Date(0).toISOString(),
      needsRecovery: false,
      serverSynchronized: true,
      sourceVideo
    },
    subtitleWorkspace,
    busy: false,
    ocrSettings: {
      languageCode: 'zh',
      profile: 'BALANCED',
      region: { x: 0, y: 0.7, width: 1, height: 0.3 }
    },
    ocrRuntime: {
      ready: true,
      message: 'PaddleOCR V5 local đã sẵn sàng.',
      availableLanguages: ['en', 'zh']
    },
    translationRuntime: {
      status: 'READY',
      ready: true,
      engineId: 'QWEN3_LOCAL',
      engineVersion: '1',
      sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true,
      supportsReviewPass: true,
      message: 'Ready'
    },
    voiceRuntime: {
      status: 'NOT_INSTALLED',
      ready: false,
      engineId: 'PIPER_LOCAL',
      engineVersion: '1',
      modelId: 'piper-vi-vais1000-medium',
      modelVersion: '1',
      voiceId: 'piper:vi-vn-vais1000',
      installedBytes: 0,
      requiredBytes: 60_000_000,
      message: 'Chưa cài Piper local.'
    },
    playheadMilliseconds: 0,
    onSeek: noOp,
    onImportMedia: noOp,
    onUpdateOcrSettings: async () => true,
    onPreviewOcr: noOp,
    onStartOcr: noOp,
    onStartTranslation: noOp,
    onInstallTranslationRuntime: noOp,
    onStartVoice: noOp,
    onInstallVoiceRuntime: noOp,
    onPauseJob: noOp,
    onResumeJob: noOp,
    onRetryJob: noOp,
    onCancelJob: noOp,
    onActivateOcrTrack: noOp,
    ...overrides
  };

  return renderToStaticMarkup(createElement(VietsubSettingsPanel, props));
}

describe('Vietsub project tools', () => {
  it('chỉ hiện ba tác vụ chính và ẩn dữ liệu kỹ thuật khỏi phần thiết lập', () => {
    const html = renderSettings({
      activeTrackId: 'track',
      tracks: [{
        trackId: 'track',
        displayName: 'OCR ZH 2026-09-05 05:09',
        languageCode: 'zh',
        source: 'PADDLE_OCR_LOCAL',
        revision: 18,
        cueCount: 9,
        translatedCueCount: 4,
        warningCueCount: 0,
        updatedAtUtc: new Date(0).toISOString()
      }]
    });

    expect(html).toContain('Thiết lập dự án');
    expect(html).toContain('Quét OCR');
    expect(html).toContain('Dịch tiếng Việt');
    expect(html).toContain('Tạo giọng Việt');
    expect(html).toContain('aria-controls="vietsub-ocr-scan-dialog"');
    expect(html).toContain('aria-controls="vietsub-translation-mode-dialog"');
    expect(html).not.toContain('Video nguồn');
    expect(html).not.toContain('Phụ đề nguồn');
    expect(html).not.toContain('OCR ZH 2026-09-05 05:09');
    expect(html).not.toContain('revision 18');
    expect(html).not.toContain('Piper local sẵn sàng');
    expect(html).not.toContain('Một giọng nữ tiếng Việt');
    expect(html).not.toContain('Qwen3 dịch local theo scene');
  });

  it('hướng dẫn luồng OCR, Dịch, Giọng theo tiến độ thật của dự án', () => {
    const html = renderSettings({
      activeTrackId: 'track',
      tracks: [{
        trackId: 'track',
        displayName: 'OCR ZH',
        languageCode: 'zh',
        source: 'PADDLE_OCR_LOCAL',
        revision: 3,
        cueCount: 9,
        translatedCueCount: 4,
        warningCueCount: 0,
        updatedAtUtc: new Date(0).toISOString()
      }]
    });

    expect(html).toContain('aria-label="Tiến độ xử lý dự án"');
    expect(html).toContain('Tiếp theo: Dịch');
    expect(html).toContain('aria-valuenow="48"');
    expect(html).toContain('9 câu đã nhận dạng');
    expect(html).toContain('Còn 5 câu');
    expect(html).toContain('Dịch đủ các câu đã chọn tạo giọng');
    expect(html).toContain('class="is-complete"');
    expect(html).toContain('class="is-ready"');
    expect(html).toContain('class="is-locked"');
  });

  it('bắt đầu ở OCR khi dự án chưa có nguồn phụ đề', () => {
    const html = renderSettings(null);

    expect(html).toContain('Tiếp theo: OCR');
    expect(html).toContain('aria-valuenow="0"');
    expect(html).toContain('Bắt đầu từ video nguồn');
    expect(html.match(/class="is-locked"/g)).toHaveLength(2);
  });

  it('hiển thị thông báo dịch thành công bằng dấu tích và tông màu xanh', () => {
    const html = renderSettings(null, {
      translationNotice: 'Đã hoàn thành dịch tiếng Việt.'
    });

    expect(html).toContain('vietsub-translation-notice is-success');
    expect(html).toContain('lucide-circle-check');
    expect(html).toContain('role="status"');
    expect(html).not.toContain('lucide-triangle-alert');
  });

  it('hiển thị thông báo tạo giọng thành công bằng dấu tích và tông màu xanh', () => {
    const html = renderSettings(null, {
      voiceNotice: 'Đã hoàn thành timeline giọng Việt.'
    });

    expect(html).toContain('vietsub-translation-notice is-success');
    expect(html).toContain('lucide-circle-check');
    expect(html).toContain('Đã hoàn thành timeline giọng Việt.');
    expect(html).not.toContain('lucide-info');
  });

  it('hiện video hiện tại và khung chỉnh vùng trong popup OCR', () => {
    const html = renderToStaticMarkup(createElement(VietsubOcrScanModal, {
      fileName: 'video-nguon.mp4',
      videoUrl: sourceVideo.playbackUrl,
      timestampMilliseconds: 3_000,
      durationMilliseconds: 9_000,
      aspectRatio: 9 / 16,
      sourceReady: true,
      runtime: { ready: true, message: 'Sẵn sàng', availableLanguages: ['en', 'zh'] },
      settings: {
        languageCode: 'zh',
        profile: 'BALANCED',
        region: { x: 0.1, y: 0.7, width: 0.8, height: 0.2 }
      },
      preview: {
        text: '你好',
        confidence: 0.95,
        timestampMilliseconds: 3_000,
        frameWidth: 1280,
        frameHeight: 720
      },
      busy: false,
      saving: false,
      onSettingsChange: noOp,
      onRegionValueChange: noOp,
      onRegionKeyDown: noOp,
      onPreview: noOp,
      onSeek: noOp,
      onStart: noOp,
      onDismiss: noOp
    }));

    expect(html).toContain('role="dialog"');
    expect(html).toContain('id="vietsub-ocr-scan-dialog"');
    expect(html).toContain('video-nguon.mp4');
    expect(html).toContain(`src="${sourceVideo.playbackUrl}"`);
    expect(html).toContain('class="vietsub-ocr-region-preview is-portrait"');
    expect(html).toContain('class="vietsub-ocr-region-box"');
    expect(html).toContain('left:10%');
    expect(html).toContain('top:70%');
    expect(html).toContain('aria-label="Điều khiển tua video OCR"');
    expect(html).toContain('aria-label="Tua đến vị trí cần kiểm tra phụ đề"');
    expect(html).toContain('0:03');
    expect(html).toContain('0:09');
    expect(html).toContain('Quét thử');
    expect(html).toContain('Bắt đầu OCR');
  });

  it('hiện Local và Cloud, khóa Cloud khi chưa nhận được trạng thái sẵn sàng', () => {
    const html = renderToStaticMarkup(createElement(VietsubTranslationModeModal, {
      runtime: {
        status: 'READY',
        ready: true,
        engineId: 'QWEN3_LOCAL',
        engineVersion: '1',
        sourceLanguages: ['en', 'zh'],
        supportsSceneContext: true,
        supportsReviewPass: true,
        message: 'Ready'
      },
      busy: false,
      onDismiss: noOp,
      onStartLocal: noOp,
      onInstallLocal: noOp
    }));

    expect(html).toContain('id="vietsub-translation-mode-dialog"');
    expect(html).toContain('Dịch Local');
    expect(html).toContain('Dịch bằng Local');
    expect(html).toContain('Dịch Cloud');
    expect(html).toMatch(/class="vietsub-translation-mode-option is-cloud" aria-disabled="true"/);
    expect(html).toMatch(/<button type="button" disabled=""><svg[^]*?Dịch Cloud<\/button>/);
  });

  it('phân biệt engine cần kiểm tra với model chưa được cài', () => {
    const html = renderToStaticMarkup(createElement(VietsubTranslationModeModal, {
      runtime: {
        status: 'INVALID',
        ready: false,
        engineId: 'QWEN3_LOCAL',
        engineVersion: '1',
        sourceLanguages: ['en', 'zh'],
        supportsSceneContext: true,
        supportsReviewPass: true,
        message: 'Model đã có nhưng probe tiếng Trung cần chạy lại.',
        errorCode: 'TRANSLATION_ZH_PROBE_FAILED',
        requiresResourceConfirmation: true,
        resourceWarningCode: 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED',
        resourceWarningMessage: 'RAM trống thấp hơn mức khuyến nghị.'
      },
      busy: false,
      onDismiss: noOp,
      onStartLocal: noOp,
      onInstallLocal: noOp
    }));

    expect(html).toContain('Cần kiểm tra');
    expect(html).toContain('Kiểm tra lại engine');
    expect(html).toContain('Model đã có nhưng probe tiếng Trung cần chạy lại.');
    expect(html).toContain('RAM trống thấp hơn mức khuyến nghị.');
    expect(html).not.toContain('Cần cài đặt');
  });

  it('opens the failed job step and keeps retry controls beside its progress', () => {
    const workspace = {
      activeTrackId: 'track',
      tracks: [{
        trackId: 'track',
        displayName: 'OCR EN',
        languageCode: 'en',
        source: 'PADDLE_OCR_LOCAL',
        revision: 3,
        cueCount: 5,
        translatedCueCount: 2,
        warningCueCount: 0,
        updatedAtUtc: new Date(0).toISOString()
      }]
    };
    const html = renderSettings(workspace, {
      activeJob: {
        id: 'job',
        projectId: 'project',
        type: 'TRANSLATE_LOCAL',
        status: 'FAILED',
        progressPercent: 70,
        errorCode: 'TRANSLATION_PROCESS_CRASHED',
        errorMessage: 'Worker bị gián đoạn.',
        attemptCount: 1,
        maxAttempts: 3,
        createdAtUtc: new Date(0).toISOString(),
        updatedAtUtc: new Date(0).toISOString(),
        steps: []
      }
    });

    expect(html).toContain('Dịch tiếng Việt · Thất bại');
    expect(html).toContain('70%');
    expect(html).toContain('Thử lại');
    expect(html).toContain('Cần kiểm tra bước Dịch');
    expect(html).toContain('aria-valuenow="47"');
    expect(html).toContain('Đã dịch 2/5 câu');
    expect(html).toContain('class="is-error"');
  });

  it.each(['PENDING', 'RUNNING', 'PAUSED', 'INTERRUPTED', 'CANCELLED'] as const)(
    'giữ tiến độ tổng theo câu đã lưu khi tác vụ dịch ở trạng thái %s', status => {
      const html = renderSettings({ activeTrackId: 'track', tracks: [{
        trackId: 'track', displayName: 'OCR', languageCode: 'en', source: 'PADDLE_OCR_LOCAL',
        revision: 51, cueCount: 100, translatedCueCount: 50, warningCueCount: 0, updatedAtUtc: ''
      }] }, { activeJob: {
        id: 'job', projectId: 'project', type: 'TRANSLATE_CLOUD', status, progressPercent: 1,
        attemptCount: 3, maxAttempts: 3, createdAtUtc: '', updatedAtUtc: '', steps: []
      } });
      expect(html).toContain('TỔNG TIẾN ĐỘ');
      expect(html).toContain('aria-valuenow="50"');
      expect(html).toContain('Đã dịch 50/100 câu');
      if (status === 'PAUSED' || status === 'INTERRUPTED') expect(html).toContain('Tiếp tục');
    }
  );
});
