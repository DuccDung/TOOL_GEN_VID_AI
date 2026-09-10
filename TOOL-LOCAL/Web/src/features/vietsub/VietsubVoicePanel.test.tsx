import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { VietsubSettingsPanel } from './VietsubSettingsPanel';
import { VietsubVoiceInstallModal } from './VietsubVoiceInstallModal';

const noOp = () => { };

describe('Vietsub local voice panel', () => {
  it('renders a branded local-install dialog with privacy and integrity guidance', () => {
    const html = renderToStaticMarkup(createElement(VietsubVoiceInstallModal, {
      requiredBytes: 63215234,
      modelVersion: 'vi_VN-vais1000-medium@ea046e8',
      onDismiss: noOp,
      onConfirm: noOp
    }));

    expect(html).toContain('role="dialog"');
    expect(html).toContain('aria-modal="true"');
    expect(html).toContain('Cài Piper và giọng Việt?');
    expect(html).toContain('Khoảng 60,3 MB');
    expect(html).toContain('checksum phải khớp');
    expect(html).toContain('không được gửi lên Cloud');
    expect(html).toContain('Để sau');
    expect(html).toContain('Tải và cài đặt');
  });

  it('asks the user to install Piper when the enabled runtime has no model', () => {
    const html = renderToStaticMarkup(createElement(VietsubSettingsPanel, {
      project: {
        projectId: 'project',
        name: 'Voice project',
        status: 'READY',
        sourceLanguageCode: 'en',
        targetLanguageCode: 'vi',
        updatedAtUtc: new Date(0).toISOString(),
        needsRecovery: false,
        serverSynchronized: true
      },
      subtitleWorkspace: { tracks: [] },
      busy: false,
      ocrSettings: {
        languageCode: 'en',
        profile: 'BALANCED',
        region: { x: 0, y: 0.7, width: 1, height: 0.3 }
      },
      voiceRuntime: {
        status: 'NOT_INSTALLED',
        ready: false,
        engineId: 'PIPER_LOCAL',
        engineVersion: 'piper-tts-1.6.0',
        modelId: 'piper-vi-vais1000-medium',
        modelVersion: 'vi_VN-vais1000-medium@ea046e8',
        voiceId: 'piper:vi-vn-vais1000',
        installedBytes: 0,
        requiredBytes: 63215234,
        message: 'Piper local chưa được cài đầy đủ. Hãy cài runtime và model trước khi tạo giọng.',
        errorCode: 'VOICE_RUNTIME_NOT_INSTALLED'
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
      onActivateOcrTrack: noOp
    }));

    expect(html).toContain('Tạo giọng Việt');
    expect(html).toContain('Cần cài giọng');
    expect(html).toContain('aria-haspopup="dialog"');
    expect(html).toContain('aria-controls="vietsub-voice-install-dialog"');
    expect(html).not.toContain('Piper local chưa được cài đầy đủ');
    expect(html).not.toContain('Tính năng đang tắt');
  });

  it('shows the pinned local voice and exposes only the authorized playback URL', () => {
    const playbackUrl = 'https://vietsub-media.app.local/projects/abc/voice/def/hash.wav';
    const html = renderToStaticMarkup(createElement(VietsubSettingsPanel, {
      project: {
        projectId: 'project',
        name: 'Voice project',
        status: 'COMPLETED',
        sourceLanguageCode: 'en',
        targetLanguageCode: 'vi',
        updatedAtUtc: new Date(0).toISOString(),
        needsRecovery: false,
        serverSynchronized: true
      },
      subtitleWorkspace: {
        activeTrackId: 'track',
        tracks: [{
          trackId: 'track',
          displayName: 'OCR',
          languageCode: 'en',
          source: 'PADDLE_OCR_LOCAL',
          revision: 4,
          cueCount: 2,
          translatedCueCount: 2,
          warningCueCount: 0,
          updatedAtUtc: new Date(0).toISOString()
        }]
      },
      busy: false,
      ocrSettings: {
        languageCode: 'en',
        profile: 'BALANCED',
        region: { x: 0, y: 0.7, width: 1, height: 0.3 }
      },
      voiceRuntime: {
        status: 'READY',
        ready: true,
        engineId: 'PIPER_LOCAL',
        engineVersion: 'piper-tts-1.6.0',
        modelId: 'piper-vi-vais1000-medium',
        modelVersion: 'vi_VN-vais1000-medium@ea046e8',
        voiceId: 'piper:vi-vn-vais1000',
        installedBytes: 1,
        requiredBytes: 1,
        message: 'Ready'
      },
      voiceWorkspace: {
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
          artifactId: 'artifact',
          trackId: 'track',
          trackRevision: 4,
          artifactKind: 'TIMELINE',
          sizeBytes: 1024,
          sha256: '0'.repeat(64),
          durationMilliseconds: 2500,
          sampleRate: 48000,
          channels: 2,
          status: 'READY',
          timingStatus: 'REVIEW_REQUIRED',
          updatedAtUtc: new Date(0).toISOString()
        },
        timelinePlaybackUrl: playbackUrl,
        timingDiagnostics: [{
          phraseId: 'phrase-1',
          naturalDurationMilliseconds: 1500,
          targetDurationMilliseconds: 1200,
          borrowedGapMilliseconds: 0,
          tempo: 1.2,
          status: 'REVIEW_REQUIRED',
          suggestedMaximumCharacters: 24
        }]
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
      onActivateOcrTrack: noOp
    }));

    expect(html).toContain('Tạo lại giọng Việt');
    expect(html).toContain('Đã tạo');
    expect(html).toContain('timeline vẫn được tạo');
    expect(html).not.toContain('Piper local sẵn sàng');
    expect(html).not.toContain('<audio');
    expect(html).not.toContain(playbackUrl);
    expect(html).not.toContain('relativePath');
  });
});
