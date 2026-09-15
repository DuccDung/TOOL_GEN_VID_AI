import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
  type RefObject
} from 'react';
import { createPortal } from 'react-dom';
import {
  CircleAlert,
  CircleCheck,
  Cloud,
  Cpu,
  Download,
  Info,
  Languages,
  LoaderCircle,
  Pause,
  Play,
  RotateCcw,
  ScanText,
  Square,
  TriangleAlert,
  Volume2,
  X
} from 'lucide-react';
import type {
  VietsubCloudAvailability,
  VietsubJobSummary,
  VietsubMediaImportProgress,
  VietsubOcrActivationRequest,
  VietsubOcrPreviewResult,
  VietsubOcrRegion,
  VietsubOcrRuntimeStatus,
  VietsubOcrSettings,
  VietsubProjectSummary,
  VietsubSubtitleTrackSummary,
  VietsubSubtitleWorkspace,
  VietsubTranslationRuntimeInstallProgress,
  VietsubTranslationRuntimeStatus,
  VietsubVoiceRuntimeInstallProgress,
  VietsubVoiceRuntimeStatus,
  VietsubVoiceWorkspace,
  VietsubVoiceModelStatus,
  VietsubVoiceModelInstallProgress
} from './types';
import {
  getVietsubTranslationInstallStageLabel,
  getVietsubTranslationRuntimeActionLabel,
  getVietsubTranslationRuntimeView,
  type VietsubTranslationRunMode
} from './vietsubTranslation';
import { VietsubVoiceInstallModal } from './VietsubVoiceInstallModal';
import { VietsubNotice } from './VietsubNotice';

type VietsubSettingsPanelProps = {
  headerAction?: ReactNode;
  cloudAvailability?: VietsubCloudAvailability | null;
  onStartCloudTranslation?: () => void;
  onRefreshCloudAvailability?: () => void;
  project: VietsubProjectSummary;
  subtitleWorkspace?: VietsubSubtitleWorkspace | null;
  progress?: VietsubMediaImportProgress | null;
  busy: boolean;
  ocrSettings: VietsubOcrSettings;
  ocrRuntime?: VietsubOcrRuntimeStatus | null;
  ocrPreview?: VietsubOcrPreviewResult | null;
  translationRuntime?: VietsubTranslationRuntimeStatus | null;
  translationInstallProgress?: VietsubTranslationRuntimeInstallProgress | null;
  translationNotice?: string | null;
  translationNoticeId?: number;
  voiceWorkspace?: VietsubVoiceWorkspace | null;
  voiceRuntime?: VietsubVoiceRuntimeStatus | null;
  voiceInstallProgress?: VietsubVoiceRuntimeInstallProgress | null;
  voiceModels?: VietsubVoiceModelStatus[] | null;
  voiceModelInstallProgress?: VietsubVoiceModelInstallProgress | null;
  voiceNotice?: string | null;
  voiceNoticeId?: number;
  activeJob?: VietsubJobSummary | null;
  activationRequest?: VietsubOcrActivationRequest | null;
  playheadMilliseconds: number;
  onSeek: (milliseconds: number) => void;
  onImportMedia: (mode: 'COPY' | 'LINK') => void;
  onUpdateOcrSettings: (settings: VietsubOcrSettings) => Promise<boolean>;
  onPreviewOcr: (settings: VietsubOcrSettings, timestampMilliseconds: number) => void;
  onStartOcr: (settings: VietsubOcrSettings) => void;
  onStartTranslation: (runMode?: VietsubTranslationRunMode) => void;
  onInstallTranslationRuntime: () => void;
  onStartVoice: () => void;
  onInstallVoiceRuntime: () => void;
  onRefreshVoiceModels?: () => void;
  onInstallVoiceModel?: (voiceId: string) => void;
  onCancelVoiceModelInstall?: () => void;
  onPauseJob: (jobId: string) => void;
  onResumeJob: (jobId: string) => void;
  onRetryJob: (jobId: string) => void;
  onCancelJob: (jobId: string) => void;
  onActivateOcrTrack: (jobId: string, confirmImpact: boolean) => void;
};

export function VietsubSettingsPanel({
  project,
  headerAction,
  subtitleWorkspace,
  busy,
  ocrSettings,
  ocrRuntime,
  ocrPreview,
  translationRuntime,
  cloudAvailability,
  onStartCloudTranslation,
  onRefreshCloudAvailability,
  translationInstallProgress,
  translationNotice,
  translationNoticeId = 0,
  voiceWorkspace,
  voiceRuntime,
  voiceInstallProgress,
  voiceModels,
  voiceModelInstallProgress,
  voiceNotice,
  voiceNoticeId = 0,
  activeJob,
  activationRequest,
  playheadMilliseconds,
  onSeek,
  onUpdateOcrSettings,
  onPreviewOcr,
  onStartOcr,
  onStartTranslation,
  onInstallTranslationRuntime,
  onRefreshVoiceModels,
  onInstallVoiceModel,
  onCancelVoiceModelInstall,
  onPauseJob,
  onResumeJob,
  onRetryJob,
  onCancelJob,
  onActivateOcrTrack
}: VietsubSettingsPanelProps) {
  const sourceReady = Boolean(project.sourceVideo?.sourceAvailable && !project.sourceVideo.sourceChanged);
  const activeTrack = subtitleWorkspace?.tracks.find((track) => track.trackId === subtitleWorkspace.activeTrackId);
  const translationSucceeded = translationNotice === 'Đã hoàn thành dịch tiếng Việt.';
  const voiceSucceeded = voiceNotice === 'Đã hoàn thành timeline giọng Việt.'
    || voiceNotice === 'Piper local đã được cài đặt và kiểm tra thành công.';
  const [draft, setDraft] = useState<VietsubOcrSettings>(ocrSettings);
  const [savingOcr, setSavingOcr] = useState(false);
  const [ocrDialogOpen, setOcrDialogOpen] = useState(false);
  const [translationDialogOpen, setTranslationDialogOpen] = useState(false);
  const [voiceInstallDialogOpen, setVoiceInstallDialogOpen] = useState(false);

  const closeOcrDialog = useCallback(() => setOcrDialogOpen(false), []);
  const closeTranslationDialog = useCallback(() => setTranslationDialogOpen(false), []);
  const closeVoiceInstallDialog = useCallback(() => setVoiceInstallDialogOpen(false), []);

  useEffect(() => setDraft(ocrSettings), [ocrSettings]);

  const updateRegion = (key: keyof VietsubOcrSettings['region'], rawValue: number) => {
    setDraft((current) => {
      const region = { ...current.region };
      if (key === 'x') region.x = clamp(rawValue, 0, 1 - region.width);
      if (key === 'y') region.y = clamp(rawValue, 0, 1 - region.height);
      if (key === 'width') region.width = clamp(rawValue, 0.05, 1 - region.x);
      if (key === 'height') region.height = clamp(rawValue, 0.04, 1 - region.y);
      return { ...current, region };
    });
  };

  const moveRegionFromKeyboard = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const step = event.shiftKey ? 0.05 : 0.01;
    const deltaX = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
    const deltaY = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
    if (deltaX === 0 && deltaY === 0) return;
    event.preventDefault();
    setDraft((current) => ({
      ...current,
      region: {
        ...current.region,
        x: clamp(current.region.x + deltaX, 0, 1 - current.region.width),
        y: clamp(current.region.y + deltaY, 0, 1 - current.region.height)
      }
    }));
  };

  const startOcrFromDialog = async () => {
    setSavingOcr(true);
    try {
      const saved = await onUpdateOcrSettings(draft);
      if (!saved) return;
      setOcrDialogOpen(false);
      onStartOcr(draft);
    } finally {
      setSavingOcr(false);
    }
  };

  const runtimeReady = Boolean(ocrRuntime?.ready);
  const ocrJob = activeJob?.type === 'OCR_LOCAL' ? activeJob : null;
  const translationJob = activeJob && ['TRANSLATE_LOCAL', 'TRANSLATE_CLOUD'].includes(activeJob.type) ? activeJob : null;
  const voiceJob = activeJob?.type === 'SYNTHESIZE_VOICE_LOCAL' ? activeJob : null;
  const translationRuntimeView = getVietsubTranslationRuntimeView(translationRuntime);
  const reviewTimingCount = voiceWorkspace?.timingDiagnostics.filter(
    (item) => item.status === 'REVIEW_REQUIRED'
  ).length ?? 0;
  const thumbnailUrls = project.sourceVideo?.thumbnailUrls ?? [];
  const durationMilliseconds = (project.sourceVideo?.durationSeconds ?? 0) * 1000;
  const previewThumbnailIndex = thumbnailUrls.length > 1 && durationMilliseconds > 0
    ? Math.round(clamp(playheadMilliseconds / durationMilliseconds, 0, 1) * (thumbnailUrls.length - 1))
    : 0;
  const previewImage = thumbnailUrls[previewThumbnailIndex];
  const sourceRotation = project.sourceVideo?.rotationDegrees ?? 0;
  const sourceWidth = project.sourceVideo?.width ?? 16;
  const sourceHeight = project.sourceVideo?.height ?? 9;
  const previewAspectRatio = sourceRotation === 90 || sourceRotation === 270
    ? sourceHeight / sourceWidth
    : sourceWidth / sourceHeight;
  const voiceComplete = Boolean(
    activeTrack
    && voiceWorkspace?.timeline
    && voiceWorkspace.timeline.trackId === activeTrack.trackId
    && voiceWorkspace.timeline.trackRevision === activeTrack.revision
  );

  return (
    <aside className="card vietsub-editor-panel vietsub-settings-panel">
      <div className="vietsub-panel-heading vietsub-tools-heading">
        <h3>Thiết lập dự án</h3>
        {headerAction}
      </div>

      <div className="vietsub-tool-actions" aria-label="Công cụ xử lý phụ đề và giọng đọc">
        <button
          type="button"
          className="vietsub-tool-action is-ocr"
          aria-haspopup="dialog"
          aria-controls="vietsub-ocr-scan-dialog"
          disabled={busy || Boolean(activeJob)}
          onClick={() => setOcrDialogOpen(true)}
        >
          <span className="vietsub-tool-action-icon"><ScanText size={20} /></span>
          <span className="vietsub-tool-action-copy">
            <strong>Quét OCR</strong>
            <small>Chọn vùng phụ đề cứng trực tiếp trên video.</small>
          </span>
          {ocrJob
            ? <span className="vietsub-tool-action-badge is-running">{ocrJob.progressPercent.toFixed(0)}%</span>
            : !runtimeReady && ocrRuntime?.errorCode
              ? <span className="vietsub-tool-action-badge is-warning">Cần kiểm tra</span>
              : <Play className="vietsub-tool-action-arrow" size={16} />}
        </button>

        <button
          type="button"
          className="vietsub-tool-action is-translation"
          aria-haspopup="dialog"
          aria-controls="vietsub-translation-mode-dialog"
          disabled={busy || Boolean(activeJob)}
          onClick={() => { setTranslationDialogOpen(true); onRefreshCloudAvailability?.(); }}
        >
          <span className="vietsub-tool-action-icon"><Languages size={20} /></span>
          <span className="vietsub-tool-action-copy">
            <strong>Dịch tiếng Việt</strong>
            <small>Chọn dịch Local hoặc Cloud.</small>
          </span>
          {translationInstallProgress
            ? <span className="vietsub-tool-action-badge is-running">{translationInstallProgress.percent.toFixed(0)}%</span>
            : translationJob
              ? <span className="vietsub-tool-action-badge is-running">{translationJob.progressPercent.toFixed(0)}%</span>
              : cloudAvailability?.available
                ? <Play className="vietsub-tool-action-arrow" size={16} />
                : translationRuntimeView.canInstall
                ? <span className="vietsub-tool-action-badge is-warning">
                    {translationRuntime?.status === 'NOT_INSTALLED'
                      ? 'Cần cài model'
                      : translationRuntimeView.badge ?? 'Cần kiểm tra'}
                  </span>
                : translationRuntime?.status === 'DISABLED'
                  ? <span className="vietsub-tool-action-badge">Chưa khả dụng</span>
                  : <Play className="vietsub-tool-action-arrow" size={16} />}
        </button>

        <button
          type="button"
          className="vietsub-tool-action is-voice"
          aria-haspopup="dialog"
          aria-controls="vietsub-voice-model-dialog"
          disabled={busy || Boolean(activeJob) || voiceRuntime?.status === 'DISABLED'}
          onClick={() => {
            setVoiceInstallDialogOpen(true);
            onRefreshVoiceModels?.();
          }}
        >
          <span className="vietsub-tool-action-icon"><Volume2 size={20} /></span>
          <span className="vietsub-tool-action-copy">
            <strong>{voiceWorkspace?.requiresRebuild ? 'Cập nhật giọng Việt' : voiceComplete ? 'Tạo lại giọng Việt' : 'Tạo giọng Việt'}</strong>
            <small>Mở danh sách giọng local và cài tài nguyên model.</small>
          </span>
          {voiceModelInstallProgress
            ? <span className="vietsub-tool-action-badge is-running">{voiceModelInstallProgress.percent.toFixed(0)}%</span>
            : voiceInstallProgress
            ? <span className="vietsub-tool-action-badge is-running">{voiceInstallProgress.percent.toFixed(0)}%</span>
            : voiceJob
              ? <span className="vietsub-tool-action-badge is-running">{voiceJob.progressPercent.toFixed(0)}%</span>
              : voiceComplete
                ? <span className="vietsub-tool-action-badge is-complete">Đã tạo</span>
                : <Play className="vietsub-tool-action-arrow" size={16} />}
        </button>
      </div>

      <div className="vietsub-settings-live-status" aria-live="polite">
        {translationInstallProgress && (
          <VietsubInstallProgress
            title={getVietsubTranslationInstallStageLabel(translationInstallProgress.stage)}
            percent={translationInstallProgress.percent}
            message={translationInstallProgress.message}
            bytesProcessed={translationInstallProgress.bytesProcessed}
            totalBytes={translationInstallProgress.totalBytes}
          />
        )}

        {voiceInstallProgress && (
          <VietsubInstallProgress
            title={voiceInstallProgress.stage}
            percent={voiceInstallProgress.percent}
            message={voiceInstallProgress.message}
            bytesProcessed={voiceInstallProgress.bytesProcessed}
            totalBytes={voiceInstallProgress.totalBytes}
          />
        )}

        {ocrJob && <VietsubLocalJobStatus
          busy={busy}
          job={ocrJob}
          title="Nhận dạng OCR"
          onPause={onPauseJob}
          onResume={onResumeJob}
          onRetry={onRetryJob}
          onCancel={onCancelJob}
        />}

        {translationJob && <VietsubLocalJobStatus
          busy={busy}
          job={translationJob}
          title="Dịch tiếng Việt"
          onPause={onPauseJob}
          onResume={onResumeJob}
          onRetry={onRetryJob}
          onCancel={onCancelJob}
        />}

        {voiceJob && <VietsubLocalJobStatus
          busy={busy}
          job={voiceJob}
          title="Tạo giọng tiếng Việt"
          onPause={onPauseJob}
          onResume={onResumeJob}
          onRetry={onRetryJob}
          onCancel={onCancelJob}
        />}

        {activationRequest && (
          <div className="vietsub-ocr-activation" role="alert">
            <strong>Track cũ đang có dữ liệu phụ thuộc</strong>
            {activationRequest.reasons.map((reason) => <small key={reason}>{reason}</small>)}
            <button type="button" onClick={() => onActivateOcrTrack(activationRequest.jobId, true)}>
              Dùng track OCR mới
            </button>
          </div>
        )}

        {translationNotice && (
          <VietsubNotice eventId={translationNoticeId}
            className={`vietsub-translation-notice${translationSucceeded ? ' is-success' : ''}`}
            role={translationSucceeded ? 'status' : 'alert'}
            icon={translationSucceeded ? <CircleCheck size={14} /> : <TriangleAlert size={14} />}
          >
            {translationNotice}
          </VietsubNotice>
        )}

        {reviewTimingCount > 0 && (
          <VietsubNotice className="vietsub-translation-notice" icon={<TriangleAlert size={14} />}
            eventId={voiceWorkspace?.timeline?.artifactId ?? 'timing'}>
            <span>{reviewTimingCount} phrase dài đã được giới hạn ở tốc độ 1,20x; timeline vẫn được tạo và một số đoạn có thể chồng âm.</span>
          </VietsubNotice>
        )}

        {voiceNotice && (
          <VietsubNotice eventId={voiceNoticeId} className={`vietsub-translation-notice${voiceSucceeded ? ' is-success' : ''}`}
            icon={voiceSucceeded ? <CircleCheck size={14} /> : <Info size={14} />}>
            {voiceNotice}
          </VietsubNotice>
        )}
      </div>

      <VietsubProjectWorkflowProgress
        activeTrack={activeTrack}
        ocrJob={ocrJob}
        translationJob={translationJob}
        voiceJob={voiceJob}
        voiceComplete={voiceComplete}
      />

      {ocrDialogOpen && (
        <VietsubOcrScanModal
          fileName={project.sourceVideo?.fileName}
          videoUrl={project.sourceVideo?.playbackUrl}
          imageUrl={previewImage}
          timestampMilliseconds={playheadMilliseconds}
          durationMilliseconds={durationMilliseconds}
          aspectRatio={previewAspectRatio}
          sourceReady={sourceReady}
          runtime={ocrRuntime}
          settings={draft}
          preview={ocrPreview}
          busy={busy || Boolean(activeJob)}
          saving={savingOcr}
          onSettingsChange={setDraft}
          onRegionValueChange={updateRegion}
          onRegionKeyDown={moveRegionFromKeyboard}
          onPreview={() => onPreviewOcr(draft, playheadMilliseconds)}
          onSeek={onSeek}
          onStart={() => void startOcrFromDialog()}
          onDismiss={closeOcrDialog}
        />
      )}

      {translationDialogOpen && (
        <VietsubTranslationModeModal
          runtime={translationRuntime}
          cloudAvailability={cloudAvailability}
          onStartCloud={() => {
            setTranslationDialogOpen(false);
            onStartCloudTranslation?.();
          }}
          installProgress={translationInstallProgress}
          busy={busy || Boolean(activeJob)}
          onDismiss={closeTranslationDialog}
          onStartLocal={() => {
            setTranslationDialogOpen(false);
            onStartTranslation('CONTINUE');
          }}
          onInstallLocal={() => {
            setTranslationDialogOpen(false);
            onInstallTranslationRuntime();
          }}
        />
      )}

      {voiceInstallDialogOpen && (
        <VietsubVoiceInstallModal
          models={voiceModels}
          installProgress={voiceModelInstallProgress}
          errorMessage={voiceNotice}
          busy={busy || Boolean(activeJob)}
          onDismiss={closeVoiceInstallDialog}
          onRefresh={() => onRefreshVoiceModels?.()}
          onInstall={(voiceId) => onInstallVoiceModel?.(voiceId)}
          onCancelInstall={onCancelVoiceModelInstall}
        />
      )}
    </aside>
  );
}

export function VietsubOcrScanModal({
  fileName,
  videoUrl,
  imageUrl,
  timestampMilliseconds,
  durationMilliseconds,
  aspectRatio,
  sourceReady,
  runtime,
  settings,
  preview,
  busy,
  saving,
  onSettingsChange,
  onRegionValueChange,
  onRegionKeyDown,
  onPreview,
  onSeek,
  onStart,
  onDismiss
}: {
  fileName?: string | null;
  videoUrl?: string | null;
  imageUrl?: string | null;
  timestampMilliseconds: number;
  durationMilliseconds: number;
  aspectRatio: number;
  sourceReady: boolean;
  runtime?: VietsubOcrRuntimeStatus | null;
  settings: VietsubOcrSettings;
  preview?: VietsubOcrPreviewResult | null;
  busy: boolean;
  saving: boolean;
  onSettingsChange: (settings: VietsubOcrSettings) => void;
  onRegionValueChange: (key: keyof VietsubOcrSettings['region'], value: number) => void;
  onRegionKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => void;
  onPreview: () => void;
  onSeek: (milliseconds: number) => void;
  onStart: () => void;
  onDismiss: () => void;
}) {
  const { dialogRef, keepFocusInside } = useVietsubModalAccessibility(onDismiss);
  const previewVideoRef = useRef<HTMLVideoElement | null>(null);
  const [previewPlaying, setPreviewPlaying] = useState(false);
  const [knownDurationMilliseconds, setKnownDurationMilliseconds] = useState(durationMilliseconds);
  const runtimeReady = Boolean(runtime?.ready);
  const actionDisabled = busy || saving || !sourceReady || !runtimeReady;
  const previewDurationMilliseconds = Math.max(1, knownDurationMilliseconds || durationMilliseconds);

  useEffect(() => {
    if (durationMilliseconds > 0) setKnownDurationMilliseconds(durationMilliseconds);
  }, [durationMilliseconds]);

  const seekPreview = (requestedMilliseconds: number) => {
    const nextMilliseconds = clamp(requestedMilliseconds, 0, previewDurationMilliseconds);
    const video = previewVideoRef.current;
    if (video && Math.abs(video.currentTime * 1000 - nextMilliseconds) >= 10) {
      try {
        video.currentTime = nextMilliseconds / 1000;
      } catch {
        // Metadata may still be loading; the controlled timestamp effect will seek again.
      }
    }
    onSeek(nextMilliseconds);
  };

  const togglePreviewPlayback = () => {
    const video = previewVideoRef.current;
    if (!video) return;
    if (!video.paused && !video.ended) {
      video.pause();
      return;
    }
    if (video.ended) seekPreview(0);
    void video.play().catch(() => setPreviewPlaying(false));
  };

  const modal = (
    <div
      className="confirmation-overlay vietsub-task-modal-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onDismiss();
      }}
    >
      <section
        ref={dialogRef}
        id="vietsub-ocr-scan-dialog"
        className="confirmation-card vietsub-task-modal vietsub-ocr-scan-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="vietsub-ocr-scan-title"
        aria-describedby="vietsub-ocr-scan-description"
        onKeyDown={keepFocusInside}
      >
        <button className="confirmation-close" type="button" onClick={onDismiss} aria-label="Đóng thiết lập quét OCR">
          <X size={18} />
        </button>

        <div className="vietsub-task-modal-heading">
          <span className="vietsub-task-modal-icon"><ScanText size={22} /></span>
          <div>
            <span className="confirmation-eyebrow">NHẬN DẠNG PHỤ ĐỀ CỨNG</span>
            <h2 id="vietsub-ocr-scan-title">Chọn vùng quét OCR</h2>
            <p id="vietsub-ocr-scan-description">Kéo và đổi kích thước khung để phủ đúng vùng phụ đề trên video.</p>
          </div>
        </div>

        {!sourceReady && (
          <div className="vietsub-step-alert warning" role="alert">
            <TriangleAlert size={16} />
            <div><strong>Video chưa sẵn sàng</strong><span>Hãy kiểm tra lại video nguồn trước khi quét OCR.</span></div>
          </div>
        )}

        {!runtimeReady && (
          <div className="vietsub-step-alert warning" role="alert">
            <TriangleAlert size={16} />
            <div><strong>OCR chưa sẵn sàng</strong><span>{runtime?.message ?? 'Đang kiểm tra thành phần OCR…'}</span></div>
          </div>
        )}

        <div className="vietsub-ocr-modal-layout">
          <div className="vietsub-ocr-modal-stage">
            <div className="vietsub-ocr-modal-stage-heading">
              <div><small>VIDEO HIỆN TẠI</small><strong>{fileName ?? 'Chưa có video'}</strong></div>
              <span>{formatDuration(timestampMilliseconds)}</span>
            </div>
            <VietsubOcrRegionSelector
              videoRef={previewVideoRef}
              region={settings.region}
              videoUrl={videoUrl ?? undefined}
              imageUrl={imageUrl ?? undefined}
              timestampMilliseconds={timestampMilliseconds}
              aspectRatio={aspectRatio}
              enabled={sourceReady && !busy}
              onChange={(region) => onSettingsChange({ ...settings, region })}
              onKeyDown={onRegionKeyDown}
              onVideoTimeUpdate={onSeek}
              onVideoDurationChange={setKnownDurationMilliseconds}
              onVideoPlayingChange={setPreviewPlaying}
            />
            <div className="vietsub-ocr-preview-playback" aria-label="Điều khiển tua video OCR">
              <button
                type="button"
                disabled={!videoUrl || !sourceReady}
                onClick={togglePreviewPlayback}
                aria-label={previewPlaying ? 'Tạm dừng video' : 'Phát video'}
              >
                {previewPlaying ? <Pause size={14} fill="currentColor" /> : <Play size={14} fill="currentColor" />}
              </button>
              <span>{formatDuration(timestampMilliseconds)}</span>
              <input
                type="range"
                min={0}
                max={previewDurationMilliseconds}
                step={50}
                value={Math.min(timestampMilliseconds, previewDurationMilliseconds)}
                disabled={!videoUrl || !sourceReady}
                aria-label="Tua đến vị trí cần kiểm tra phụ đề"
                onChange={(event) => seekPreview(Number(event.target.value))}
              />
              <span>{formatDuration(previewDurationMilliseconds)}</span>
            </div>
            <p>Kéo khung xanh đến vị trí subtitle cứng. Có thể kéo các chấm ở mép để thay đổi kích thước.</p>
          </div>

          <div className="vietsub-ocr-modal-controls">
            <div className="vietsub-ocr-fields">
              <label>Ngôn ngữ video
                <select
                  value={settings.languageCode}
                  disabled={!sourceReady || busy}
                  onChange={(event) => onSettingsChange({ ...settings, languageCode: event.target.value as 'en' | 'zh' })}
                >
                  <option value="en">English</option>
                  <option value="zh">中文</option>
                </select>
              </label>
              <label>Chế độ quét
                <select
                  value={settings.profile}
                  disabled={!sourceReady || busy}
                  onChange={(event) => onSettingsChange({ ...settings, profile: event.target.value as VietsubOcrSettings['profile'] })}
                >
                  <option value="FAST">Nhanh</option>
                  <option value="BALANCED">Cân bằng</option>
                  <option value="ACCURATE">Chính xác</option>
                </select>
              </label>
            </div>

            <div className="vietsub-ocr-region-fields">
              {(['x', 'y', 'width', 'height'] as const).map((key) => (
                <label key={key}>{key.toUpperCase()} {(settings.region[key] * 100).toFixed(0)}%
                  <input
                    type="range"
                    min={0}
                    max={1}
                    step={0.01}
                    value={settings.region[key]}
                    disabled={!sourceReady || busy}
                    onChange={(event) => onRegionValueChange(key, Number(event.target.value))}
                  />
                </label>
              ))}
            </div>

            {preview && (
              <div className="vietsub-ocr-result" role="status">
                <strong>Quét thử · {(preview.confidence * 100).toFixed(1)}%</strong>
                <p>{preview.text || 'Không phát hiện chữ.'}</p>
              </div>
            )}
          </div>
        </div>

        <div className="confirmation-actions vietsub-task-modal-actions">
          <button className="confirmation-cancel" type="button" onClick={onDismiss}>Hủy</button>
          <button type="button" className="vietsub-modal-secondary" disabled={actionDisabled} onClick={onPreview}>
            <ScanText size={16} /> Quét thử
          </button>
          <button
            type="button"
            className="confirmation-submit"
            data-autofocus="true"
            disabled={actionDisabled}
            onClick={onStart}
          >
            <Play size={16} /> {saving ? 'Đang lưu…' : 'Bắt đầu OCR'}
          </button>
        </div>
      </section>
    </div>
  );

  return typeof document === 'undefined' ? modal : createPortal(modal, document.body);
}

export function VietsubTranslationModeModal({
  runtime,
  cloudAvailability,
  onStartCloud,
  installProgress,
  busy,
  onDismiss,
  onStartLocal,
  onInstallLocal
}: {
  cloudAvailability?: VietsubCloudAvailability | null;
  onStartCloud?: () => void;
  runtime?: VietsubTranslationRuntimeStatus | null;
  installProgress?: VietsubTranslationRuntimeInstallProgress | null;
  busy: boolean;
  onDismiss: () => void;
  onStartLocal: () => void;
  onInstallLocal: () => void;
}) {
  const { dialogRef, keepFocusInside } = useVietsubModalAccessibility(onDismiss);
  const runtimeView = getVietsubTranslationRuntimeView(runtime);
  const localDisabled = busy || (!runtimeView.canTranslate && !runtimeView.canInstall);
  const localActionLabel = runtimeView.canTranslate
    ? 'Dịch bằng Local'
    : runtimeView.canInstall
      ? getVietsubTranslationRuntimeActionLabel(runtime)
      : 'Local chưa khả dụng';
  const localBadge = runtimeView.canTranslate
    ? runtimeView.badge ?? 'Sẵn sàng'
    : runtime?.status === 'NOT_INSTALLED'
      ? 'Chưa cài'
      : runtimeView.badge ?? 'Chưa khả dụng';
  const localBadgeClass = runtimeView.tone === 'ready'
    ? 'is-ready'
    : runtimeView.tone === 'warning'
      ? 'is-warning'
      : undefined;

  const modal = (
    <div
      className="confirmation-overlay vietsub-task-modal-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onDismiss();
      }}
    >
      <section
        ref={dialogRef}
        id="vietsub-translation-mode-dialog"
        className="confirmation-card vietsub-task-modal vietsub-translation-mode-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="vietsub-translation-mode-title"
        aria-describedby="vietsub-translation-mode-description"
        onKeyDown={keepFocusInside}
      >
        <button className="confirmation-close" type="button" onClick={onDismiss} aria-label="Đóng lựa chọn phương thức dịch">
          <X size={18} />
        </button>

        <div className="vietsub-task-modal-heading">
          <span className="vietsub-task-modal-icon"><Languages size={22} /></span>
          <div>
            <span className="confirmation-eyebrow">PHƯƠNG THỨC DỊCH</span>
            <h2 id="vietsub-translation-mode-title">Bạn muốn dịch bằng cách nào?</h2>
            <p id="vietsub-translation-mode-description">Chọn phương thức phù hợp cho phụ đề của video hiện tại.</p>
          </div>
        </div>

        <div className="vietsub-translation-mode-grid" role="group" aria-label="Phương thức dịch">
          <section className="vietsub-translation-mode-option is-local">
            <div className="vietsub-translation-mode-option-heading">
              <span><Cpu size={21} /></span>
              <div><strong>Dịch Local</strong><small>Xử lý trực tiếp trên máy</small></div>
              <em className={localBadgeClass}>{localBadge}</em>
            </div>
            <p>Nội dung phụ đề được xử lý trong worker local và không gửi lên dịch vụ bên ngoài.</p>
            {!runtime?.ready && (
              <div className="vietsub-mode-status">
                <Info size={15} />
                <span>{runtime?.message ?? 'Đang kiểm tra thành phần dịch local…'}</span>
              </div>
            )}
            {runtime?.requiresResourceConfirmation && (
              <div className="vietsub-mode-status is-warning">
                <TriangleAlert size={15} />
                <span>{runtime.resourceWarningMessage
                  ?? 'Tài nguyên máy thấp hơn mức khuyến nghị. Bạn vẫn có thể xác nhận để tiếp tục.'}</span>
              </div>
            )}
            {installProgress && (
              <VietsubInstallProgress
                title={getVietsubTranslationInstallStageLabel(installProgress.stage)}
                percent={installProgress.percent}
                message={installProgress.message}
                bytesProcessed={installProgress.bytesProcessed}
                totalBytes={installProgress.totalBytes}
              />
            )}
            <button
              type="button"
              data-autofocus="true"
              disabled={localDisabled}
              onClick={runtimeView.canTranslate ? onStartLocal : onInstallLocal}
            >
              {runtimeView.canTranslate ? <Languages size={16} /> : <Download size={16} />}
              {localActionLabel}
            </button>
          </section>

          <section className={`vietsub-translation-mode-option is-cloud${cloudAvailability?.available ? ' is-available' : ''}`} aria-disabled={!cloudAvailability?.available}>
            <div className="vietsub-translation-mode-option-heading">
              <span><Cloud size={21} /></span>
              <div><strong>Dịch Cloud</strong><small>Dịch vụ trực tuyến</small></div>
              <em className={cloudAvailability?.available ? 'is-ready' : undefined}>{cloudAvailability?.available ? 'Sẵn sàng' : cloudAvailability ? 'Chưa khả dụng' : 'Đang kiểm tra'}</em>
            </div>
            <p>Phụ đề và ngữ cảnh được gửi qua dịch vụ trực tuyến để dịch sang tiếng Việt. Không tải video lên.</p>
            {!cloudAvailability?.available && <div className="vietsub-mode-status" role="status"><Info size={15} /><span>{cloudAvailability?.message ?? 'Đang kiểm tra dịch vụ Dịch Cloud…'}</span></div>}
            <button type="button" disabled={busy || !cloudAvailability?.available || !onStartCloud} onClick={onStartCloud}>
              <Cloud size={16} /> Dịch Cloud
            </button>
          </section>
        </div>

        <div className="confirmation-actions vietsub-task-modal-actions is-compact">
          <button className="confirmation-cancel" type="button" onClick={onDismiss}>Đóng</button>
        </div>
      </section>
    </div>
  );

  return typeof document === 'undefined' ? modal : createPortal(modal, document.body);
}

function VietsubInstallProgress({
  title,
  percent,
  message,
  bytesProcessed,
  totalBytes
}: {
  title: string;
  percent: number;
  message: string;
  bytesProcessed: number;
  totalBytes: number;
}) {
  return (
    <div className="vietsub-import-progress compact" role="status">
      <div><strong>{title}</strong><span>{percent.toFixed(0)}%</span></div>
      <div className="vietsub-progress-track"><span style={{ width: `${percent}%` }} /></div>
      <small>{message}</small>
      {totalBytes > 1 && <small>{formatBytes(bytesProcessed)} / {formatBytes(totalBytes)}</small>}
    </div>
  );
}

type VietsubWorkflowStageStatus = 'complete' | 'active' | 'ready' | 'locked' | 'attention' | 'error';

type VietsubWorkflowStage = {
  key: 'ocr' | 'translation' | 'voice';
  label: string;
  detail: string;
  status: VietsubWorkflowStageStatus;
  ratio: number;
};

function VietsubProjectWorkflowProgress({
  activeTrack,
  ocrJob,
  translationJob,
  voiceJob,
  voiceComplete
}: {
  activeTrack?: VietsubSubtitleTrackSummary | null;
  ocrJob?: VietsubJobSummary | null;
  translationJob?: VietsubJobSummary | null;
  voiceJob?: VietsubJobSummary | null;
  voiceComplete: boolean;
}) {
  const hasOcr = Boolean(activeTrack && activeTrack.cueCount > 0);
  const translationRatio = activeTrack && activeTrack.cueCount > 0
    ? clamp(activeTrack.translatedCueCount / activeTrack.cueCount, 0, 1)
    : 0;
  const translationComplete = hasOcr && translationRatio >= 1;
  const selectedVoiceCount = activeTrack?.voiceEnabledCueCount ?? activeTrack?.cueCount ?? 0;
  const noVoiceSelected = hasOcr && selectedVoiceCount === 0;
  const selectedVoiceTranslated = hasOcr && (activeTrack?.voiceTranslatedCueCount ?? activeTrack?.translatedCueCount ?? 0) === selectedVoiceCount;
  const stages: VietsubWorkflowStage[] = [
    resolveWorkflowStage({
      key: 'ocr',
      label: 'OCR',
      job: ocrJob,
      complete: hasOcr,
      unlocked: true,
      ratio: hasOcr ? 1 : 0,
      completeDetail: `${activeTrack?.cueCount ?? 0} câu đã nhận dạng`,
      readyDetail: 'Bắt đầu từ video nguồn',
      lockedDetail: ''
    }),
    resolveWorkflowStage({
      key: 'translation',
      label: 'Dịch',
      job: translationJob,
      complete: translationComplete,
      unlocked: hasOcr,
      ratio: translationRatio,
      completeDetail: `Đã dịch ${activeTrack?.translatedCueCount ?? 0} câu`,
      readyDetail: translationRatio > 0
        ? `Còn ${(activeTrack?.cueCount ?? 0) - (activeTrack?.translatedCueCount ?? 0)} câu`
        : 'Sẵn sàng dịch tiếng Việt',
      lockedDetail: 'Hoàn thành OCR trước'
    }),
    resolveWorkflowStage({
      key: 'voice',
      label: 'Giọng',
      job: voiceJob,
      complete: voiceComplete || noVoiceSelected,
      unlocked: selectedVoiceTranslated,
      ratio: voiceComplete || noVoiceSelected ? 1 : 0,
      completeDetail: noVoiceSelected ? 'Đã bỏ qua tạo giọng' : 'Timeline giọng đã sẵn sàng',
      readyDetail: 'Sẵn sàng tạo giọng Việt',
      lockedDetail: 'Dịch đủ các câu đã chọn tạo giọng'
    })
  ];
  const targetProgress = Math.round(stages.reduce((total, stage) => total + stage.ratio, 0) / stages.length * 100);
  const animatedProgress = useAnimatedWorkflowProgress(targetProgress);
  const moving = stages.some((stage) => stage.status === 'active');
  const attentionStage = stages.find((stage) => stage.status === 'error' || stage.status === 'attention');
  const nextStage = stages.find((stage) => stage.status !== 'complete');
  const guidance = attentionStage
    ? `Cần kiểm tra bước ${attentionStage.label}`
    : !nextStage
      ? 'Quy trình đã hoàn thành'
      : nextStage.status === 'active'
        ? `Đang xử lý bước ${nextStage.label}`
        : `Tiếp theo: ${nextStage.label}`;

  return (
    <section className="vietsub-project-workflow" aria-label="Tiến độ xử lý dự án">
      <div className="vietsub-project-workflow-heading">
        <div><span>LUỒNG XỬ LÝ</span><strong>{guidance}</strong></div>
        <b>{animatedProgress}%</b>
      </div>
      <div
        className={`vietsub-project-workflow-track${moving ? ' is-moving' : ''}`}
        role="progressbar"
        aria-label="Tiến độ OCR, dịch và tạo giọng"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={animatedProgress}
      >
        <span style={{ width: `${animatedProgress}%` }}><i /></span>
      </div>
      <ol className="vietsub-project-workflow-steps">
        {stages.map((stage, index) => (
          <li className={`is-${stage.status}`} key={stage.key} aria-current={stage.status === 'active' ? 'step' : undefined}>
            <span className="vietsub-project-workflow-marker">
              {stage.status === 'complete'
                ? <CircleCheck size={14} />
                : stage.status === 'active'
                  ? <LoaderCircle size={14} />
                  : stage.status === 'error' || stage.status === 'attention'
                    ? <CircleAlert size={14} />
                    : index + 1}
            </span>
            <span><strong>{stage.label}</strong><small>{stage.detail}</small></span>
          </li>
        ))}
      </ol>
    </section>
  );
}

function resolveWorkflowStage({
  key,
  label,
  job,
  complete,
  unlocked,
  ratio,
  completeDetail,
  readyDetail,
  lockedDetail
}: {
  key: VietsubWorkflowStage['key'];
  label: string;
  job?: VietsubJobSummary | null;
  complete: boolean;
  unlocked: boolean;
  ratio: number;
  completeDetail: string;
  readyDetail: string;
  lockedDetail: string;
}): VietsubWorkflowStage {
  if (job && !['COMPLETED', 'CANCELLED'].includes(job.status)) {
    const jobRatio = clamp(job.progressPercent / 100, 0, 1);
    if (job.status === 'FAILED') {
      return { key, label, detail: job.errorCode === 'CLOUD_UNKNOWN' ? 'Cần quản trị viên đối soát' : 'Thất bại · có thể thử lại', status: 'error', ratio: jobRatio };
    }
    if (job.status === 'PAUSED' || job.status === 'INTERRUPTED') {
      return {
        key,
        label,
        detail: job.status === 'PAUSED' ? 'Đang tạm dừng' : 'Bị gián đoạn · có thể tiếp tục',
        status: 'attention',
        ratio: jobRatio
      };
    }
    return { key, label, detail: `${Math.round(job.progressPercent)}% đang xử lý`, status: 'active', ratio: jobRatio };
  }
  if (complete) return { key, label, detail: completeDetail, status: 'complete', ratio: 1 };
  if (!unlocked) return { key, label, detail: lockedDetail, status: 'locked', ratio: 0 };
  if (job?.status === 'CANCELLED') return { key, label, detail: 'Đã hủy · có thể chạy lại', status: 'ready', ratio };
  return { key, label, detail: readyDetail, status: 'ready', ratio };
}

function useAnimatedWorkflowProgress(target: number): number {
  const clampedTarget = Math.round(clamp(target, 0, 100));
  const [displayed, setDisplayed] = useState(clampedTarget);
  const displayedRef = useRef(clampedTarget);

  useEffect(() => {
    const from = displayedRef.current;
    const distance = clampedTarget - from;
    if (distance === 0 || typeof window === 'undefined') return;
    const startedAt = window.performance.now();
    let frame = 0;
    const tick = (now: number) => {
      const elapsed = Math.min(1, (now - startedAt) / 420);
      const eased = 1 - Math.pow(1 - elapsed, 3);
      const next = Math.round(from + distance * eased);
      displayedRef.current = next;
      setDisplayed(next);
      if (elapsed < 1) frame = window.requestAnimationFrame(tick);
    };
    frame = window.requestAnimationFrame(tick);
    return () => window.cancelAnimationFrame(frame);
  }, [clampedTarget]);

  return displayed;
}

function useVietsubModalAccessibility(onDismiss: () => void) {
  const dialogRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (typeof document === 'undefined') return;
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    dialogRef.current?.querySelector<HTMLElement>('[data-autofocus="true"],button:not(:disabled)')?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onDismiss();
      }
    };
    window.addEventListener('keydown', handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
      previousFocus?.focus();
    };
  }, [onDismiss]);

  const keepFocusInside = (event: ReactKeyboardEvent<HTMLElement>) => {
    if (event.key !== 'Tab') return;
    const focusable = Array.from(
      dialogRef.current?.querySelectorAll<HTMLElement>(
        'button:not(:disabled),select:not(:disabled),input:not(:disabled),[tabindex]:not([tabindex="-1"])'
      ) ?? []
    );
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  return { dialogRef, keepFocusInside };
}

function VietsubLocalJobStatus({
  job,
  busy,
  title,
  onPause,
  onResume,
  onRetry,
  onCancel
}: {
  job: VietsubJobSummary;
  busy: boolean;
  title: string;
  onPause: (jobId: string) => void;
  onResume: (jobId: string) => void;
  onRetry: (jobId: string) => void;
  onCancel: (jobId: string) => void;
}) {
  return (
    <div className="vietsub-local-job" role="status">
      <div><strong>{title} · {formatJobStatus(job.status, job.type)}</strong><span>{job.progressPercent.toFixed(0)}%</span></div>
      <div className="vietsub-progress-track"><span style={{ width: `${job.progressPercent}%` }} /></div>
      <small>{job.statusMessage ?? job.errorMessage ?? `Lần chạy ${job.attemptCount}/${job.maxAttempts}`}</small>
      <div className="vietsub-settings-actions">
        {job.status === 'RUNNING' && <button type="button" disabled={busy} onClick={() => onPause(job.id)}><Pause size={13} /> Tạm dừng</button>}
        {(job.status === 'PAUSED' || job.status === 'INTERRUPTED') && <button type="button" disabled={busy} onClick={() => onResume(job.id)}><Play size={13} /> Tiếp tục</button>}
        {job.status === 'FAILED' && job.errorCode !== 'CLOUD_UNKNOWN' && job.attemptCount < job.maxAttempts && <button type="button" disabled={busy} onClick={() => onRetry(job.id)}><RotateCcw size={13} /> Thử lại</button>}
        {!['COMPLETED', 'CANCELLED'].includes(job.status) && job.errorCode !== 'CLOUD_UNKNOWN' && <button type="button" disabled={busy} onClick={() => onCancel(job.id)}><Square size={13} /> Hủy</button>}
      </div>
    </div>
  );
}

type RegionDragMode = 'move' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'nw';

function VietsubOcrRegionSelector({
  videoRef: providedVideoRef,
  region,
  videoUrl,
  imageUrl,
  timestampMilliseconds,
  aspectRatio,
  enabled,
  onChange,
  onKeyDown,
  onVideoTimeUpdate,
  onVideoDurationChange,
  onVideoPlayingChange
}: {
  videoRef?: RefObject<HTMLVideoElement | null>;
  region: VietsubOcrRegion;
  videoUrl?: string;
  imageUrl?: string;
  timestampMilliseconds: number;
  aspectRatio: number;
  enabled: boolean;
  onChange: (region: VietsubOcrRegion) => void;
  onKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => void;
  onVideoTimeUpdate?: (milliseconds: number) => void;
  onVideoDurationChange?: (milliseconds: number) => void;
  onVideoPlayingChange?: (playing: boolean) => void;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const internalVideoRef = useRef<HTMLVideoElement | null>(null);
  const videoRef = providedVideoRef ?? internalVideoRef;
  const dragRef = useRef<{
    mode: RegionDragMode;
    pointerId: number;
    startX: number;
    startY: number;
    origin: VietsubOcrRegion;
  } | null>(null);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !Number.isFinite(timestampMilliseconds)) return;
    const seekToPlayhead = () => {
      const requestedSeconds = Math.max(0, timestampMilliseconds / 1000);
      const targetSeconds = Number.isFinite(video.duration)
        ? Math.min(requestedSeconds, Math.max(0, video.duration))
        : requestedSeconds;
      if (Math.abs(video.currentTime - targetSeconds) > 0.04) video.currentTime = targetSeconds;
    };
    if (video.readyState >= HTMLMediaElement.HAVE_METADATA) seekToPlayhead();
    else video.addEventListener('loadedmetadata', seekToPlayhead, { once: true });
    return () => video.removeEventListener('loadedmetadata', seekToPlayhead);
  }, [videoUrl, timestampMilliseconds]);

  const beginDrag = (event: ReactPointerEvent<HTMLElement>, mode: RegionDragMode) => {
    if (!enabled || event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    dragRef.current = {
      mode,
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      origin: { ...region }
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const continueDrag = (event: ReactPointerEvent<HTMLElement>) => {
    const drag = dragRef.current;
    const bounds = containerRef.current?.getBoundingClientRect();
    if (!drag || drag.pointerId !== event.pointerId || !bounds || bounds.width <= 0 || bounds.height <= 0) return;
    event.preventDefault();
    const dx = (event.clientX - drag.startX) / bounds.width;
    const dy = (event.clientY - drag.startY) / bounds.height;
    const minimumWidth = 0.05;
    const minimumHeight = 0.04;
    let left = drag.origin.x;
    let top = drag.origin.y;
    let right = drag.origin.x + drag.origin.width;
    let bottom = drag.origin.y + drag.origin.height;
    if (drag.mode === 'move') {
      left = clamp(drag.origin.x + dx, 0, 1 - drag.origin.width);
      top = clamp(drag.origin.y + dy, 0, 1 - drag.origin.height);
      right = left + drag.origin.width;
      bottom = top + drag.origin.height;
    } else {
      if (drag.mode.includes('w')) left = clamp(drag.origin.x + dx, 0, right - minimumWidth);
      if (drag.mode.includes('e')) right = clamp(drag.origin.x + drag.origin.width + dx, left + minimumWidth, 1);
      if (drag.mode.includes('n')) top = clamp(drag.origin.y + dy, 0, bottom - minimumHeight);
      if (drag.mode.includes('s')) bottom = clamp(drag.origin.y + drag.origin.height + dy, top + minimumHeight, 1);
    }
    onChange({ x: left, y: top, width: right - left, height: bottom - top });
  };

  const endDrag = (event: ReactPointerEvent<HTMLElement>) => {
    if (dragRef.current?.pointerId !== event.pointerId) return;
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const handles: RegionDragMode[] = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'];
  return (
    <div
      ref={containerRef}
      className={`vietsub-ocr-region-preview ${aspectRatio < 1 ? 'is-portrait' : 'is-landscape'}`}
      style={{ aspectRatio: Number.isFinite(aspectRatio) && aspectRatio > 0 ? aspectRatio : 16 / 9 }}
      tabIndex={enabled ? 0 : -1}
      role="application"
      aria-label="Vùng OCR; kéo để di chuyển, dùng tám nút để đổi kích thước, hoặc dùng phím mũi tên"
      onKeyDown={onKeyDown}
    >
      {videoUrl ? (
        <video
          ref={videoRef}
          src={videoUrl}
          muted
          playsInline
          preload="metadata"
          aria-label="Frame video dùng chọn vùng OCR"
          onLoadedMetadata={(event) => {
            const duration = event.currentTarget.duration;
            if (Number.isFinite(duration) && duration > 0) {
              onVideoDurationChange?.(Math.round(duration * 1000));
            }
          }}
          onTimeUpdate={(event) => onVideoTimeUpdate?.(Math.round(event.currentTarget.currentTime * 1000))}
          onSeeked={(event) => onVideoTimeUpdate?.(Math.round(event.currentTarget.currentTime * 1000))}
          onPlay={() => onVideoPlayingChange?.(true)}
          onPause={() => onVideoPlayingChange?.(false)}
          onEnded={() => onVideoPlayingChange?.(false)}
        />
      ) : imageUrl ? (
        <img
          src={imageUrl}
          alt="Frame video dùng chọn vùng OCR"
          crossOrigin="anonymous"
          referrerPolicy="no-referrer"
        />
      ) : (
        <span>Frame video</span>
      )}
      <div
        className="vietsub-ocr-region-box"
        style={{
          left: `${region.x * 100}%`,
          top: `${region.y * 100}%`,
          width: `${region.width * 100}%`,
          height: `${region.height * 100}%`
        }}
        onPointerDown={(event) => beginDrag(event, 'move')}
        onPointerMove={continueDrag}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
      >
        {handles.map((handle) => (
          <button
            key={handle}
            type="button"
            className={`vietsub-ocr-region-handle ${handle}`}
            aria-label={`Đổi kích thước vùng OCR hướng ${handle}`}
            onPointerDown={(event) => beginDrag(event, handle)}
            onPointerMove={continueDrag}
            onPointerUp={endDrag}
            onPointerCancel={endDrag}
          />
        ))}
      </div>
    </div>
  );
}

function formatJobStatus(status: VietsubJobSummary['status'], jobType: string): string {
  return ({
    PENDING: 'Đang chờ',
    RUNNING: ['TRANSLATE_LOCAL', 'TRANSLATE_CLOUD'].includes(jobType)
      ? 'Đang dịch'
      : jobType === 'SYNTHESIZE_VOICE_LOCAL'
        ? 'Đang tạo giọng'
        : 'Đang OCR',
    PAUSING: 'Đang tạm dừng',
    PAUSED: 'Đã tạm dừng',
    INTERRUPTED: 'Bị gián đoạn',
    COMPLETED: 'Hoàn thành',
    FAILED: 'Thất bại',
    CANCELLED: 'Đã hủy'
  } as const)[status];
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, Number.isFinite(value) ? value : minimum));
}

function formatDuration(milliseconds: number): string {
  if (!Number.isFinite(milliseconds) || milliseconds <= 0) return '0:00';
  const totalSeconds = Math.round(milliseconds / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return hours > 0
    ? `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
    : `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}
