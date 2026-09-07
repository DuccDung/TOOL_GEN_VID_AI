import { describe, expect, it } from 'vitest';
import type { VietsubSubtitleTrackSummary, VietsubSubtitleWorkspace } from './types';
import {
  createVietsubTranslationResourceAlert,
  createVietsubTranslationStartPayload,
  getVietsubTranslationInstallStageLabel,
  getVietsubTranslationRuntimeActionLabel,
  getVietsubTranslationRuntimeConfirmation,
  getVietsubTranslationRuntimeView,
  reduceVietsubTranslationInstallProgress,
  VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR,
  VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE
} from './vietsubTranslation';

function track(overrides: Partial<VietsubSubtitleTrackSummary> = {}): VietsubSubtitleTrackSummary {
  return {
    trackId: 'track-ocr',
    displayName: 'OCR ZH',
    languageCode: 'zh',
    source: 'PADDLE_OCR_LOCAL',
    revision: 3,
    cueCount: 9,
    translatedCueCount: 0,
    warningCueCount: 0,
    updatedAtUtc: '2026-09-05T00:00:00Z',
    ...overrides
  };
}

function workspace(activeTrack: VietsubSubtitleTrackSummary): VietsubSubtitleWorkspace {
  return { activeTrackId: activeTrack.trackId, tracks: [activeTrack] };
}

describe('createVietsubTranslationStartPayload', () => {
  it('requires an active OCR track with cues', () => {
    expect(createVietsubTranslationStartPayload(null)).toBeNull();
    expect(createVietsubTranslationStartPayload(workspace(track({ source: 'IMPORTED_SRT' })))).toBeNull();
    expect(createVietsubTranslationStartPayload(workspace(track({ cueCount: 0 })))).toBeNull();
    expect(createVietsubTranslationStartPayload(workspace(track({ languageCode: 'vi' })))).toBeNull();
    expect(VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE).toBe(
      'Bạn cần quét OCR nhận dạng phụ đề trước khi dịch.'
    );
  });

  it('uses the active OCR track identity and revision', () => {
    expect(createVietsubTranslationStartPayload(workspace(track()))).toEqual({
      runMode: 'CONTINUE',
      expectedTrackId: 'track-ocr',
      expectedTrackRevision: 3,
      confirmResourceWarning: false
    });
    expect(createVietsubTranslationStartPayload(workspace(track()), 'RETRY_FAILED', true))
      .toMatchObject({ runMode: 'RETRY_FAILED', confirmResourceWarning: true });
  });
});

describe('translation runtime recovery copy', () => {
  it('creates a confirmation alert only for the native confirmation-required error', () => {
    expect(createVietsubTranslationResourceAlert(
      VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR,
      'RAM trống hiện chỉ còn 1,17 GB; mức khuyến nghị là 4 GB.'
    )).toEqual({
      errorCode: VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR,
      title: 'Tài nguyên máy thấp hơn mức khuyến nghị',
      message: 'RAM trống hiện chỉ còn 1,17 GB; mức khuyến nghị là 4 GB.',
      action: 'TRANSLATE',
      runMode: 'CONTINUE'
    });
    expect(createVietsubTranslationResourceAlert(
      'TRANSLATION_MODEL_NOT_READY',
      'Model chưa sẵn sàng.'
    )).toBeNull();
  });

  it('presents a disabled feature as informational and not installable', () => {
    const runtime = {
      status: 'DISABLED' as const,
      ready: false,
      sourceLanguages: [],
      supportsSceneContext: false,
      supportsReviewPass: false,
      message: 'Đây không phải lỗi.',
      errorCode: 'TRANSLATION_FEATURE_DISABLED'
    };

    expect(getVietsubTranslationRuntimeView(runtime)).toEqual({
      tone: 'info',
      title: 'Dịch local chưa được bật',
      badge: 'Chưa khả dụng trên bản này',
      canInstall: false,
      canTranslate: false,
      translationActionLabel: 'Chưa khả dụng'
    });
    expect(getVietsubTranslationRuntimeActionLabel(runtime)).toBe('Chưa khả dụng');
  });

  it('only offers installation for actionable runtime states', () => {
    expect(getVietsubTranslationRuntimeView(null).canInstall).toBe(false);
    expect(getVietsubTranslationRuntimeView({
      status: 'NOT_INSTALLED', ready: false, sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true, supportsReviewPass: false, message: 'missing'
    }).canInstall).toBe(true);
    expect(getVietsubTranslationRuntimeView({
      status: 'BUSY', ready: false, sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true, supportsReviewPass: false, message: 'busy'
    }).canInstall).toBe(false);
  });

  it('keeps unsupported platforms blocked and distinguishes probe repair', () => {
    expect(getVietsubTranslationRuntimeActionLabel({
      status: 'UNSUPPORTED_HARDWARE', ready: false, sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true, supportsReviewPass: false, message: 'low',
      errorCode: 'TRANSLATION_RUNTIME_UNSUPPORTED_PLATFORM'
    })).toBe('Không hỗ trợ trên máy này');
    expect(getVietsubTranslationRuntimeActionLabel({
      status: 'INVALID', ready: false, sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true, supportsReviewPass: false, message: 'probe',
      errorCode: 'TRANSLATION_ZH_PROBE_FAILED'
    })).toBe('Kiểm tra lại engine');
    expect(getVietsubTranslationRuntimeActionLabel(null)).toBe('Cài engine · 2,50 GB');
  });

  it('keeps a ready low-memory profile actionable and labels the slower mode', () => {
    expect(getVietsubTranslationRuntimeView({
      status: 'READY', ready: true, sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true, supportsReviewPass: false,
      message: 'ready in low-memory mode',
      runtimeProfileId: 'qwen3-cpu-low-memory-v1',
      lowMemoryMode: true
    })).toEqual({
      tone: 'ready',
      title: 'Engine sẵn sàng ở chế độ tiết kiệm RAM',
      badge: 'Tiết kiệm RAM',
      canInstall: false,
      canTranslate: true,
      translationActionLabel: 'Dịch tiếng Việt'
    });
    expect(getVietsubTranslationInstallStageLabel('LOW_MEMORY_FALLBACK'))
      .toBe('Chuyển sang chế độ tiết kiệm RAM');
  });

  it('keeps translation actionable when RAM is only an acknowledged warning', () => {
    expect(getVietsubTranslationRuntimeView({
      status: 'READY', ready: true, sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true, supportsReviewPass: false,
      message: 'runtime ready',
      lowMemoryMode: true,
      requiresResourceConfirmation: true,
      resourceWarningCode: VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR,
      resourceWarningMessage: 'RAM thấp hơn mức khuyến nghị.'
    })).toEqual({
      tone: 'warning',
      title: 'Engine sẵn sàng nhưng RAM đang thấp',
      badge: 'Cần xác nhận',
      canInstall: false,
      canTranslate: true,
      translationActionLabel: 'Dịch tiếng Việt'
    });
  });

  it('states that repair reuses the verified model and exposes separate stages', () => {
    const confirmation = getVietsubTranslationRuntimeConfirmation({
      status: 'INVALID', ready: false, sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true, supportsReviewPass: false, message: 'worker crash'
    });
    expect(confirmation).toContain('giữ nguyên và tái sử dụng');
    expect(confirmation).toContain('Runtime/English/Chinese');
    expect(getVietsubTranslationInstallStageLabel('PROBING_RUNTIME')).toBe('Probe runtime');
    expect(getVietsubTranslationInstallStageLabel('PROBING_ZH')).toBe('Probe Chinese → Vietnamese');
  });
});

describe('translation install progress ordering', () => {
  it('restores authoritative READY when queued progress arrives after final runtime status', () => {
    const readyRuntime = {
      status: 'READY' as const,
      ready: true,
      sourceLanguages: ['en', 'zh'],
      supportsSceneContext: true,
      supportsReviewPass: false,
      message: 'Engine đã sẵn sàng.',
      requiresResourceConfirmation: true,
      resourceWarningCode: VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR,
      resourceWarningMessage: 'RAM thấp hơn mức khuyến nghị.'
    };
    const delayedLoading = reduceVietsubTranslationInstallProgress(
      readyRuntime,
      readyRuntime,
      {
        stage: 'LOADING',
        percent: 88,
        message: 'Đang nạp model.',
        bytesProcessed: 0,
        totalBytes: 1
      }
    );

    expect(delayedLoading.terminal).toBe(false);
    expect(delayedLoading.runtime?.ready).toBe(false);

    const delayedReady = reduceVietsubTranslationInstallProgress(
      delayedLoading.runtime,
      readyRuntime,
      {
        stage: 'READY',
        percent: 100,
        message: 'Engine đã vượt probe.',
        bytesProcessed: 1,
        totalBytes: 1
      }
    );

    expect(delayedReady).toEqual({
      terminal: true,
      clearBusy: true,
      progress: null,
      runtime: readyRuntime
    });
  });
});
