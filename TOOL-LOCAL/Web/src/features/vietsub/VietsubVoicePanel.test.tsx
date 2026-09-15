// @vitest-environment jsdom
import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { VietsubSettingsPanel } from './VietsubSettingsPanel';
import { VietsubVoiceInstallModal } from './VietsubVoiceInstallModal';
import type { VietsubVoiceModelStatus } from './types';

const noOp = () => { };

describe('Vietsub local voice panel', () => {
  it('lists verified, missing, and invalid local model assets without starting synthesis', () => {
    let html = '';
    vi.stubGlobal('document', undefined);
    try {
      html = renderToStaticMarkup(createElement(VietsubVoiceInstallModal, {
      models: [
        { voiceId: 'piper:vi-vn-vais1000', displayName: 'Piper nữ', engineId: 'PIPER_LOCAL',
          modelId: 'piper-model', modelVersion: 'pin', status: 'READY', installedBytes: 1,
          requiredBytes: 1, license: 'CC BY 4.0', message: 'Model đã xác minh.' },
        { voiceId: 'kokoro-vi:hung_thinh', displayName: 'Hưng Thịnh', engineId: 'KOKORO_LOCAL_MODEL',
          modelId: 'kokoro-model', modelVersion: 'pin', status: 'NOT_INSTALLED', installedBytes: 0,
          requiredBytes: 325731953, license: 'Apache-2.0', message: 'Chưa cài.' },
        { voiceId: 'kokoro-vi:mai_linh', displayName: 'Mai Linh', engineId: 'KOKORO_LOCAL_MODEL',
          modelId: 'kokoro-model', modelVersion: 'pin', status: 'INVALID', installedBytes: 10,
          requiredBytes: 325731953, license: 'Apache-2.0', message: 'File sai.' }
      ],
      busy: false,
      onDismiss: noOp,
      onRefresh: noOp,
      onInstall: noOp
      }));
    } finally {
      vi.unstubAllGlobals();
    }

    expect(html).toContain('role="dialog"');
    expect(html).toContain('aria-modal="true"');
    expect(html).toContain('Chọn giọng tạo phụ đề');
    expect(html).toContain('Piper nữ');
    expect(html).toContain('Hưng Thịnh');
    expect(html).toContain('Mai Linh');
    expect(html).toContain('Model đã cài');
    expect(html).toContain('Cài giọng');
    expect(html).toContain('Cần cài lại');
    expect(html.match(/vietsub-voice-model-preview/g)).toHaveLength(3);
    expect(html).toContain('Nghe thử giọng Piper nữ');
    expect(html).toContain('Nghe thử giọng Hưng Thịnh');
    expect(html).toContain('Tạo giọng bằng giọng đã chọn');
    expect(html).toContain('disabled=""');
  });

  it('previews installed and uninstalled voices independently of model installation', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
    const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    const install = vi.fn();
    const dismiss = vi.fn();
    try {
      await act(async () => root.render(createElement(VietsubVoiceInstallModal, {
        models: [
          { voiceId: 'piper:vi-vn-vais1000', displayName: 'Piper nữ', engineId: 'PIPER_LOCAL',
            modelId: 'piper-model', modelVersion: 'pin', status: 'READY', installedBytes: 1,
            requiredBytes: 1, license: 'CC BY 4.0', message: 'Model đã xác minh.' },
          { voiceId: 'kokoro-vi:duc_an', displayName: 'Đức An', engineId: 'KOKORO_LOCAL_MODEL',
            modelId: 'kokoro-model', modelVersion: 'pin', status: 'NOT_INSTALLED', installedBytes: 0,
            requiredBytes: 325731953, license: 'Apache-2.0', message: 'Chưa cài.' }
        ],
        busy: false, onDismiss: dismiss, onRefresh: noOp, onInstall: install
      })));

      const previews = Array.from(document.querySelectorAll<HTMLButtonElement>(
        '#vietsub-voice-model-dialog .vietsub-voice-model-preview'
      ));
      const audio = document.querySelector<HTMLAudioElement>('#vietsub-voice-model-dialog audio')!;
      expect(previews).toHaveLength(2);
      await act(async () => previews[0].click());
      expect(play).toHaveBeenCalledTimes(1);
      expect(audio.src).toContain('/voice-previews/piper-vais1000.wav');
      expect(previews[0].textContent).toContain('Tạm dừng');

      await act(async () => previews[1].click());
      expect(pause).toHaveBeenCalled();
      expect(play).toHaveBeenCalledTimes(2);
      expect(audio.src).toContain('/voice-previews/duc_an.wav');
      expect(previews[0].textContent).toContain('Nghe thử');
      expect(previews[1].textContent).toContain('Tạm dừng');

      await act(async () => previews[1].click());
      expect(previews[1].textContent).toContain('Tiếp tục');
      await act(async () => previews[1].click());
      expect(play).toHaveBeenCalledTimes(3);

      await act(async () => (document.querySelector('#vietsub-voice-model-dialog .confirmation-close') as HTMLButtonElement).click());
      expect(dismiss).toHaveBeenCalledOnce();
      expect(audio.hasAttribute('src')).toBe(false);
      expect(install).not.toHaveBeenCalled();
      expect(document.querySelector('#vietsub-voice-model-dialog .is-ready')?.textContent).toContain('Model đã cài');
      expect(document.querySelector('#vietsub-voice-model-dialog .vietsub-voice-model-install')?.textContent).toContain('Kiểm tra runtime');
      expect(document.querySelectorAll('#vietsub-voice-model-dialog .vietsub-voice-model-install')[1]?.textContent)
        .toContain('Cài giọng');
    } finally {
      await act(async () => root.unmount());
      container.remove();
      vi.restoreAllMocks();
    }
  });

  it('shows a retryable row error when sample playback fails', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play')
      .mockRejectedValueOnce(new Error('audio decode failed')).mockResolvedValue(undefined);
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    const install = vi.fn();
    try {
      await act(async () => root.render(createElement(VietsubVoiceInstallModal, {
        models: [{ voiceId: 'kokoro-vi:duc_duy', displayName: 'Đức Duy', engineId: 'KOKORO_LOCAL_MODEL',
          modelId: 'kokoro-model', modelVersion: 'pin', status: 'NOT_INSTALLED', installedBytes: 0,
          requiredBytes: 325731953, license: 'Apache-2.0', message: 'Chưa cài.' }],
        busy: false, onDismiss: noOp, onRefresh: noOp, onInstall: install
      })));
      const preview = document.querySelector<HTMLButtonElement>('#vietsub-voice-model-dialog .vietsub-voice-model-preview')!;
      await act(async () => preview.click());
      expect(document.querySelector('#vietsub-voice-model-dialog .vietsub-voice-preview-error')?.textContent)
        .toContain('Không thể phát âm thanh mẫu');
      expect(preview.textContent).toContain('Nghe thử');
      await act(async () => preview.click());
      expect(play).toHaveBeenCalledTimes(2);
      expect(document.querySelector('#vietsub-voice-model-dialog .vietsub-voice-preview-error')).toBeNull();
      expect(install).not.toHaveBeenCalled();
    } finally {
      await act(async () => root.unmount());
      container.remove();
      vi.restoreAllMocks();
    }
  });

  it('opens the model modal and installs only the clicked voice without starting a voice job', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const startVoice = vi.fn();
    const refreshModels = vi.fn();
    const installModel = vi.fn();
    try {
      await act(async () => root.render(createElement(VietsubSettingsPanel, {
        project: { projectId: 'project', name: 'Voice project', status: 'READY',
          sourceLanguageCode: 'en', targetLanguageCode: 'vi', updatedAtUtc: new Date(0).toISOString(),
          needsRecovery: false, serverSynchronized: true },
        busy: false,
        ocrSettings: { languageCode: 'en', profile: 'BALANCED',
          region: { x: 0, y: 0.7, width: 1, height: 0.3 } },
        voiceRuntime: { status: 'READY', ready: true, engineId: 'PIPER_LOCAL',
          engineVersion: 'piper-tts-1.6.0', modelId: 'piper-vi-vais1000-medium',
          modelVersion: 'pin', voiceId: 'piper:vi-vn-vais1000', installedBytes: 1,
          requiredBytes: 1, message: 'Ready' },
        voiceModels: [{ voiceId: 'kokoro-vi:hung_thinh', displayName: 'Hưng Thịnh',
          engineId: 'KOKORO_LOCAL_MODEL', modelId: 'kokoro-model', modelVersion: 'pin',
          status: 'NOT_INSTALLED', installedBytes: 0, requiredBytes: 325731953,
          license: 'Apache-2.0', message: 'Chưa cài.' }],
        playheadMilliseconds: 0,
        onSeek: noOp, onImportMedia: noOp, onUpdateOcrSettings: async () => true,
        onPreviewOcr: noOp, onStartOcr: noOp, onStartTranslation: noOp,
        onInstallTranslationRuntime: noOp, onStartVoice: startVoice,
        onInstallVoiceRuntime: noOp, onRefreshVoiceModels: refreshModels,
        onInstallVoiceModel: installModel, onPauseJob: noOp, onResumeJob: noOp,
        onRetryJob: noOp, onCancelJob: noOp, onActivateOcrTrack: noOp
      })));

      await act(async () => (container.querySelector('.vietsub-tool-action.is-voice') as HTMLButtonElement).click());
      expect(refreshModels).toHaveBeenCalledOnce();
      expect(startVoice).not.toHaveBeenCalled();
      expect(document.getElementById('vietsub-voice-model-dialog')).not.toBeNull();

      await act(async () => (document.querySelector('#vietsub-voice-model-dialog .vietsub-voice-model-install') as HTMLButtonElement).click());
      expect(installModel).toHaveBeenCalledExactlyOnceWith('kokoro-vi:hung_thinh');
      expect(startVoice).not.toHaveBeenCalled();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('creates audio only after the selected voice is runtime-ready', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const select = vi.fn();
    const create = vi.fn();
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => { });
    vi.spyOn(HTMLMediaElement.prototype, 'load').mockImplementation(() => { });
    const models: VietsubVoiceModelStatus[] = [
      { voiceId: 'piper:vi-vn-vais1000', displayName: 'Piper nữ', engineId: 'PIPER_LOCAL',
        modelId: 'piper-model', modelVersion: 'pin', status: 'READY', installedBytes: 1,
        requiredBytes: 1, license: 'CC BY 4.0', message: 'Model đã cài', synthesisReady: true },
      { voiceId: 'kokoro-vi:duc_an', displayName: 'Đức An', engineId: 'KOKORO_LOCAL_MODEL',
        modelId: 'kokoro-model', modelVersion: 'pin', status: 'READY', installedBytes: 1,
        requiredBytes: 1, license: 'Apache-2.0', message: 'Model đã cài', synthesisReady: false }
    ];
    try {
      await act(async () => root.render(createElement(VietsubVoiceInstallModal, {
        models, selectedVoiceId: 'kokoro-vi:duc_an', canCreate: true,
        busy: false, onDismiss: noOp, onRefresh: noOp, onInstall: noOp,
        onSelect: select, onCreate: create
      })));
      const createButton = document.querySelector<HTMLButtonElement>(
        '#vietsub-voice-model-dialog .confirmation-submit')!;
      expect(createButton.disabled).toBe(true);
      await act(async () => createButton.click());
      expect(create).not.toHaveBeenCalled();
      await act(async () => root.render(createElement(VietsubVoiceInstallModal, {
        models: models.map(model => model.voiceId === 'kokoro-vi:duc_an'
          ? { ...model, synthesisReady: true } : model),
        selectedVoiceId: 'kokoro-vi:duc_an', canCreate: true,
        busy: false, onDismiss: noOp, onRefresh: noOp, onInstall: noOp,
        onSelect: select, onCreate: create
      })));
      expect(createButton.disabled).toBe(false);
      await act(async () => createButton.click());
      expect(create).toHaveBeenCalledOnce();
      const choosePiper = Array.from(document.querySelectorAll<HTMLButtonElement>(
        '#vietsub-voice-model-dialog .vietsub-voice-model-action button'))
        .find(button => button.textContent === 'Chọn giọng')!;
      await act(async () => choosePiper.click());
      expect(select).toHaveBeenCalledExactlyOnceWith('piper:vi-vn-vais1000');
    } finally {
      await act(async () => root.unmount());
      container.remove();
      vi.restoreAllMocks();
    }
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
    expect(html).toContain('Chọn giọng local và tạo âm thanh cho phụ đề');
    expect(html).toContain('aria-haspopup="dialog"');
    expect(html).toContain('aria-controls="vietsub-voice-model-dialog"');
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
