import type {
  VietsubSubtitleWorkspace,
  VietsubTranslationResourceAlert,
  VietsubTranslationRuntimeInstallProgress,
  VietsubTranslationRuntimeStatus
} from './types';

export const VIETSUB_TRANSLATION_OCR_REQUIRED_MESSAGE =
  'Bạn cần quét OCR nhận dạng phụ đề trước khi dịch.';

export const VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR =
  'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED';

export function createVietsubTranslationResourceAlert(
  errorCode?: string | null,
  message?: string | null,
  action: 'TRANSLATE' | 'INSTALL' = 'TRANSLATE',
  runMode: VietsubTranslationRunMode = 'CONTINUE'
): VietsubTranslationResourceAlert | null {
  if (errorCode !== VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR) return null;

  return {
    errorCode: VIETSUB_TRANSLATION_RESOURCE_CONFIRMATION_ERROR,
    title: 'Tài nguyên máy thấp hơn mức khuyến nghị',
    message: message?.trim()
      || 'RAM hoặc commit khả dụng đang thấp. Bạn vẫn có thể tiếp tục nhưng engine có thể chạy chậm, treo hoặc hết bộ nhớ.',
    action,
    ...(action === 'TRANSLATE' ? { runMode } : {})
  };
}

export type VietsubTranslationRunMode = 'CONTINUE' | 'RETRY_FAILED' | 'RESTART_UNLOCKED';

export type VietsubTranslationStartPayload = {
  runMode: VietsubTranslationRunMode;
  expectedTrackId: string;
  expectedTrackRevision: number;
  confirmResourceWarning: boolean;
};

export type VietsubTranslationInstallPayload = {
  confirmResourceWarning: boolean;
};

export type VietsubTranslationInstallProgressTransition = {
  terminal: boolean;
  clearBusy: boolean;
  progress: VietsubTranslationRuntimeInstallProgress | null;
  runtime: VietsubTranslationRuntimeStatus | null | undefined;
};

export function reduceVietsubTranslationInstallProgress(
  currentRuntime: VietsubTranslationRuntimeStatus | null | undefined,
  latestAuthoritativeRuntime: VietsubTranslationRuntimeStatus | null | undefined,
  progress: VietsubTranslationRuntimeInstallProgress
): VietsubTranslationInstallProgressTransition {
  if (progress.stage.toUpperCase() === 'READY') {
    return {
      terminal: true,
      clearBusy: Boolean(latestAuthoritativeRuntime?.ready),
      progress: null,
      runtime: latestAuthoritativeRuntime?.ready
        ? latestAuthoritativeRuntime
        : currentRuntime
    };
  }

  return {
    terminal: false,
    clearBusy: false,
    progress,
    runtime: currentRuntime
      ? { ...currentRuntime, status: 'BUSY', ready: false, message: progress.message }
      : currentRuntime
  };
}

export type VietsubTranslationRuntimeView = {
  tone: 'ready' | 'info' | 'warning';
  title: string;
  badge: string | null;
  canInstall: boolean;
  canTranslate: boolean;
  translationActionLabel: string;
};

export function getVietsubTranslationRuntimeView(
  runtime?: VietsubTranslationRuntimeStatus | null
): VietsubTranslationRuntimeView {
  if (!runtime) {
    return {
      tone: 'info',
      title: 'Đang kiểm tra dịch local',
      badge: null,
      canInstall: false,
      canTranslate: false,
      translationActionLabel: 'Đang kiểm tra'
    };
  }

  if (runtime.status === 'DISABLED' || runtime.errorCode === 'TRANSLATION_FEATURE_DISABLED') {
    return {
      tone: 'info',
      title: 'Dịch local chưa được bật',
      badge: 'Chưa khả dụng trên bản này',
      canInstall: false,
      canTranslate: false,
      translationActionLabel: 'Chưa khả dụng'
    };
  }

  if (runtime.ready || runtime.status === 'READY') {
    const lowMemoryMode = Boolean(runtime.lowMemoryMode);
    const requiresConfirmation = Boolean(runtime.requiresResourceConfirmation);
    return {
      tone: requiresConfirmation ? 'warning' : 'ready',
      title: requiresConfirmation
        ? 'Engine sẵn sàng nhưng RAM đang thấp'
        : lowMemoryMode
        ? 'Engine sẵn sàng ở chế độ tiết kiệm RAM'
        : 'Engine dịch local sẵn sàng',
      badge: requiresConfirmation ? 'Cần xác nhận' : lowMemoryMode ? 'Tiết kiệm RAM' : 'Sẵn sàng',
      canInstall: false,
      canTranslate: true,
      translationActionLabel: 'Dịch tiếng Việt'
    };
  }

  if (runtime.status === 'BUSY') {
    return {
      tone: 'info',
      title: 'Đang chuẩn bị engine dịch local',
      badge: 'Đang xử lý',
      canInstall: false,
      canTranslate: false,
      translationActionLabel: 'Đang chuẩn bị'
    };
  }

  if (runtime.status === 'UNSUPPORTED_HARDWARE') {
    return {
      tone: 'warning',
      title: 'Máy không hỗ trợ engine dịch local',
      badge: 'Không hỗ trợ',
      canInstall: false,
      canTranslate: false,
      translationActionLabel: 'Không hỗ trợ trên máy này'
    };
  }

  if (runtime.status === 'NOT_INSTALLED') {
    return {
      tone: 'info',
      title: 'Chưa cài engine dịch local',
      badge: null,
      canInstall: true,
      canTranslate: false,
      translationActionLabel: 'Cài engine trước'
    };
  }

  return {
    tone: 'warning',
    title: 'Engine cần được kiểm tra lại',
    badge: 'Cần xử lý',
    canInstall: true,
    canTranslate: false,
    translationActionLabel: 'Kiểm tra engine trước'
  };
}

export function getVietsubTranslationRuntimeActionLabel(
  runtime?: VietsubTranslationRuntimeStatus | null
): string {
  if (runtime?.status === 'DISABLED' || runtime?.errorCode === 'TRANSLATION_FEATURE_DISABLED') return 'Chưa khả dụng';
  if (runtime?.errorCode === 'TRANSLATION_RUNTIME_UNSUPPORTED_PLATFORM') return 'Không hỗ trợ trên máy này';
  if (runtime?.errorCode === 'TRANSLATION_RUNTIME_INSUFFICIENT_DISK') return 'Không đủ dung lượng đĩa';
  if (runtime?.errorCode === 'TRANSLATION_PROCESS_CRASHED'
    || runtime?.errorCode === 'TRANSLATION_PROCESS_TIMEOUT'
    || runtime?.errorCode === 'TRANSLATION_RUNTIME_PROBE_FAILED'
    || runtime?.errorCode === 'TRANSLATION_EN_PROBE_FAILED'
    || runtime?.errorCode === 'TRANSLATION_ZH_PROBE_FAILED'
    || runtime?.errorCode === 'TRANSLATION_MODEL_NOT_READY') return 'Kiểm tra lại engine';
  if (runtime?.status === 'INVALID') return 'Kiểm tra lại engine';
  return 'Cài engine · 2,50 GB';
}

export function getVietsubTranslationRuntimeConfirmation(
  runtime?: VietsubTranslationRuntimeStatus | null
): string {
  if (runtime?.status === 'NOT_INSTALLED') {
    return 'Cài engine dịch local Qwen3 4B?\n\nỨng dụng sẽ ưu tiên tái sử dụng model 2,50 GB hợp lệ đã có trên máy. Chỉ khi chưa có model hợp lệ ứng dụng mới tải từ nguồn đã duyệt. Model được kiểm tra SHA-256 rồi probe Runtime/English/Chinese trong worker cô lập. Phụ đề không được gửi lên Cloud.';
  }
  return 'Kiểm tra lại engine dịch local?\n\nModel đúng SHA-256 đang có sẽ được giữ nguyên và tái sử dụng. Ứng dụng chỉ khởi động lại worker cô lập rồi chạy riêng probe Runtime/English/Chinese; không gửi phụ đề lên Cloud.';
}

export function getVietsubTranslationInstallStageLabel(stage: string): string {
  switch (stage) {
    case 'REUSING_LOCAL': return 'Tái sử dụng model local';
    case 'DOWNLOADING': return 'Tải model';
    case 'VERIFYING': return 'Kiểm tra model';
    case 'LOW_MEMORY_FALLBACK': return 'Chuyển sang chế độ tiết kiệm RAM';
    case 'LOADING': return 'Nạp model trong worker';
    case 'PROBING_RUNTIME': return 'Probe runtime';
    case 'PROBING_EN': return 'Probe English → Vietnamese';
    case 'PROBING_ZH': return 'Probe Chinese → Vietnamese';
    case 'READY': return 'Engine sẵn sàng';
    default: return 'Chuẩn bị engine dịch local';
  }
}

export function createVietsubTranslationStartPayload(
  workspace: VietsubSubtitleWorkspace | null | undefined,
  runMode: VietsubTranslationRunMode = 'CONTINUE',
  confirmResourceWarning = false
): VietsubTranslationStartPayload | null {
  const activeTrack = workspace?.tracks.find((track) => track.trackId === workspace.activeTrackId);
  if (!workspace?.activeTrackId
    || !activeTrack
    || activeTrack.source !== 'PADDLE_OCR_LOCAL'
    || activeTrack.cueCount <= 0
    || !['en', 'zh'].includes(activeTrack.languageCode.toLowerCase())
    || activeTrack.revision < 1) {
    return null;
  }

  return {
    runMode,
    expectedTrackId: activeTrack.trackId,
    expectedTrackRevision: activeTrack.revision,
    confirmResourceWarning
  };
}
