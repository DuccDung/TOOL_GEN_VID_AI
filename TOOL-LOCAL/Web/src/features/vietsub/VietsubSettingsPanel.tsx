import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type PointerEvent as ReactPointerEvent
} from 'react';
import {
  Captions,
  CheckCircle2,
  Copy,
  Download,
  FileVideo2,
  Info,
  Languages,
  Link2,
  Pause,
  Play,
  RotateCcw,
  ScanText,
  Square,
  TriangleAlert,
  Volume2
} from 'lucide-react';
import type {
  VietsubJobSummary,
  VietsubMediaImportProgress,
  VietsubOcrActivationRequest,
  VietsubOcrPreviewResult,
  VietsubOcrRegion,
  VietsubOcrRuntimeStatus,
  VietsubOcrSettings,
  VietsubProjectSummary,
  VietsubSubtitleWorkspace,
  VietsubTranslationRuntimeInstallProgress,
  VietsubTranslationRuntimeStatus,
  VietsubVoiceRuntimeInstallProgress,
  VietsubVoiceRuntimeStatus,
  VietsubVoiceWorkspace
} from './types';
import {
  getVietsubTranslationInstallStageLabel,
  getVietsubTranslationRuntimeActionLabel,
  getVietsubTranslationRuntimeConfirmation,
  getVietsubTranslationRuntimeView,
  type VietsubTranslationRunMode
} from './vietsubTranslation';
import { VietsubVoiceInstallModal } from './VietsubVoiceInstallModal';

type VietsubSettingsPanelProps = {
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
  voiceWorkspace?: VietsubVoiceWorkspace | null;
  voiceRuntime?: VietsubVoiceRuntimeStatus | null;
  voiceInstallProgress?: VietsubVoiceRuntimeInstallProgress | null;
  voiceNotice?: string | null;
  activeJob?: VietsubJobSummary | null;
  activationRequest?: VietsubOcrActivationRequest | null;
  playheadMilliseconds: number;
  onImportMedia: (mode: 'COPY' | 'LINK') => void;
  onUpdateOcrSettings: (settings: VietsubOcrSettings) => Promise<boolean>;
  onPreviewOcr: (settings: VietsubOcrSettings, timestampMilliseconds: number) => void;
  onStartOcr: (settings: VietsubOcrSettings) => void;
  onStartTranslation: (runMode?: VietsubTranslationRunMode) => void;
  onInstallTranslationRuntime: () => void;
  onStartVoice: () => void;
  onInstallVoiceRuntime: () => void;
  onPauseJob: (jobId: string) => void;
  onResumeJob: (jobId: string) => void;
  onRetryJob: (jobId: string) => void;
  onCancelJob: (jobId: string) => void;
  onActivateOcrTrack: (jobId: string, confirmImpact: boolean) => void;
};

export function VietsubSettingsPanel({
  project,
  subtitleWorkspace,
  progress,
  busy,
  ocrSettings,
  ocrRuntime,
  ocrPreview,
  translationRuntime,
  translationInstallProgress,
  translationNotice,
  voiceWorkspace,
  voiceRuntime,
  voiceInstallProgress,
  voiceNotice,
  activeJob,
  activationRequest,
  playheadMilliseconds,
  onImportMedia,
  onUpdateOcrSettings,
  onPreviewOcr,
  onStartOcr,
  onStartTranslation,
  onInstallTranslationRuntime,
  onStartVoice,
  onInstallVoiceRuntime,
  onPauseJob,
  onResumeJob,
  onRetryJob,
  onCancelJob,
  onActivateOcrTrack
}: VietsubSettingsPanelProps) {
  const sourceReady = Boolean(project.sourceVideo?.sourceAvailable && !project.sourceVideo.sourceChanged);
  const activeTrack = subtitleWorkspace?.tracks.find((track) => track.trackId === subtitleWorkspace.activeTrackId);
  const [draft, setDraft] = useState<VietsubOcrSettings>(ocrSettings);
  const [savingOcr, setSavingOcr] = useState(false);
  const [voiceInstallDialogOpen, setVoiceInstallDialogOpen] = useState(false);

  const closeVoiceInstallDialog = useCallback(() => setVoiceInstallDialogOpen(false), []);
  const confirmVoiceRuntimeInstall = useCallback(() => {
    setVoiceInstallDialogOpen(false);
    onInstallVoiceRuntime();
  }, [onInstallVoiceRuntime]);

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

  const saveOcrSettings = async () => {
    setSavingOcr(true);
    try {
      await onUpdateOcrSettings(draft);
    } finally {
      setSavingOcr(false);
    }
  };

  const runtimeReady = Boolean(ocrRuntime?.ready);
  const ocrDisabled = busy || savingOcr || !sourceReady || !runtimeReady || Boolean(activeJob);
  const ocrJob = activeJob?.type === 'OCR_LOCAL' ? activeJob : null;
  const translationJob = activeJob?.type === 'TRANSLATE_LOCAL' ? activeJob : null;
  const voiceJob = activeJob?.type === 'SYNTHESIZE_VOICE_LOCAL' ? activeJob : null;
  const translationRuntimeView = getVietsubTranslationRuntimeView(translationRuntime);
  const translationDisabled = busy || Boolean(activeJob) || !translationRuntimeView.canTranslate;
  const voiceReady = Boolean(voiceRuntime?.ready);
  const voiceEligible = Boolean(
    activeTrack
    && activeTrack.cueCount > 0
    && activeTrack.translatedCueCount === activeTrack.cueCount
  );
  const voiceDisabled = busy || Boolean(activeJob) || !voiceReady || !voiceEligible;
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

  return (
    <aside className="card vietsub-editor-panel vietsub-settings-panel">
      <div className="vietsub-panel-heading">
        <span className="vietsub-eyebrow">QUY TRÌNH</span>
        <h3>Thiết lập dự án</h3>
      </div>

      <section className="vietsub-settings-section">
        <div className="vietsub-settings-section-title"><FileVideo2 size={16} /><strong>Video nguồn</strong></div>
        {project.sourceVideo ? (
          <div className={`vietsub-settings-state ${sourceReady ? 'ready' : 'warning'}`}>
            {sourceReady ? <CheckCircle2 size={15} /> : <FileVideo2 size={15} />}
            <div><strong>{project.sourceVideo.fileName}</strong><small>{sourceReady ? 'Sẵn sàng phát và xử lý' : 'Tệp nguồn cần được kiểm tra lại'}</small></div>
          </div>
        ) : (
          <>
            <p>Thêm video trước khi nhận dạng, quét OCR hoặc dựng thành phẩm.</p>
            <div className="vietsub-settings-actions">
              <button type="button" disabled={busy} onClick={() => onImportMedia('COPY')}><Copy size={14} /> Sao chép</button>
              <button type="button" disabled={busy} onClick={() => onImportMedia('LINK')}><Link2 size={14} /> Liên kết</button>
            </div>
          </>
        )}
        {progress && (
          <div className="vietsub-import-progress compact">
            <div><strong>Đang nhập video</strong><span>{progress.percent.toFixed(0)}%</span></div>
            <div className="vietsub-progress-track"><span style={{ width: `${progress.percent}%` }} /></div>
            <small>{formatBytes(progress.bytesProcessed)} / {formatBytes(progress.totalBytes)}</small>
          </div>
        )}
      </section>

      <section className="vietsub-settings-section">
        <div className="vietsub-settings-section-title"><Captions size={16} /><strong>Phụ đề nguồn</strong></div>
        <div className={`vietsub-settings-state ${activeTrack ? 'ready' : ''}`}>
          <Captions size={15} />
          <div>
            <strong>{activeTrack ? activeTrack.displayName : 'Chưa có track phụ đề'}</strong>
            <small>{activeTrack ? `${activeTrack.cueCount} cue · revision ${activeTrack.revision}` : 'Nhập SRT hoặc chạy OCR để bắt đầu'}</small>
          </div>
        </div>
      </section>

      <section className="vietsub-settings-section vietsub-ocr-section">
        <div className="vietsub-settings-section-title"><ScanText size={16} /><strong>Nhận dạng OCR</strong></div>
        <div className={`vietsub-settings-state ${runtimeReady ? 'ready' : 'warning'}`}>
          {runtimeReady ? <CheckCircle2 size={15} /> : <TriangleAlert size={15} />}
          <div>
            <strong>{runtimeReady ? 'Runtime OCR sẵn sàng' : 'Runtime OCR chưa sẵn sàng'}</strong>
            <small>{ocrRuntime?.message ?? 'Đang kiểm tra component OCR local…'}</small>
          </div>
        </div>

        <div className="vietsub-ocr-fields">
          <label>Ngôn ngữ
            <select
              value={draft.languageCode}
              disabled={!sourceReady || busy}
              onChange={(event) => setDraft((current) => ({ ...current, languageCode: event.target.value as 'en' | 'zh' }))}
            >
              <option value="en">English</option>
              <option value="zh">中文</option>
            </select>
          </label>
          <label>Profile
            <select
              value={draft.profile}
              disabled={!sourceReady || busy}
              onChange={(event) => setDraft((current) => ({ ...current, profile: event.target.value as VietsubOcrSettings['profile'] }))}
            >
              <option value="FAST">Nhanh</option>
              <option value="BALANCED">Cân bằng</option>
              <option value="ACCURATE">Chính xác</option>
            </select>
          </label>
        </div>

        <VietsubOcrRegionSelector
          region={draft.region}
          videoUrl={project.sourceVideo?.playbackUrl}
          imageUrl={previewImage}
          timestampMilliseconds={playheadMilliseconds}
          aspectRatio={previewAspectRatio}
          enabled={sourceReady && !busy}
          onChange={(region) => setDraft((current) => ({ ...current, region }))}
          onKeyDown={moveRegionFromKeyboard}
        />

        <div className="vietsub-ocr-region-fields">
          {(['x', 'y', 'width', 'height'] as const).map((key) => (
            <label key={key}>{key.toUpperCase()} {(draft.region[key] * 100).toFixed(0)}%
              <input
                type="range"
                min={0}
                max={1}
                step={0.01}
                value={draft.region[key]}
                disabled={!sourceReady || busy}
                onChange={(event) => updateRegion(key, Number(event.target.value))}
              />
            </label>
          ))}
        </div>

        {ocrPreview && (
          <div className="vietsub-ocr-result" role="status">
            <strong>Quét thử · {(ocrPreview.confidence * 100).toFixed(1)}%</strong>
            <p>{ocrPreview.text || 'Không phát hiện chữ.'}</p>
          </div>
        )}

        {ocrJob && <VietsubLocalJobStatus
          job={ocrJob}
          title="Nhận dạng OCR"
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

        <div className="vietsub-settings-actions vietsub-ocr-actions">
          <button type="button" disabled={!sourceReady || busy || savingOcr} onClick={() => void saveOcrSettings()}>
            Lưu vùng
          </button>
          <button type="button" disabled={ocrDisabled} onClick={() => onPreviewOcr(draft, playheadMilliseconds)}>
            <ScanText size={14} /> Quét thử
          </button>
          <button type="button" disabled={ocrDisabled} onClick={() => onStartOcr(draft)}>
            <Play size={14} /> Bắt đầu OCR
          </button>
        </div>
      </section>

      <section className="vietsub-settings-section vietsub-translation-section" aria-label="Dịch phụ đề sang tiếng Việt">
        <div className="vietsub-settings-section-title"><Languages size={16} /><strong>Dịch tiếng Việt</strong></div>
        <div className={`vietsub-settings-state ${translationRuntimeView.tone}`}>
          {translationRuntimeView.tone === 'ready'
            ? <CheckCircle2 size={15} />
            : translationRuntimeView.tone === 'info'
              ? <Info size={15} />
              : <TriangleAlert size={15} />}
          <div>
            <strong>{translationRuntimeView.title}</strong>
            {translationRuntimeView.badge && (
              <span className="vietsub-translation-availability-badge">{translationRuntimeView.badge}</span>
            )}
            <small>
              {translationRuntime?.requiresResourceConfirmation
                ? translationRuntime.resourceWarningMessage
                : translationRuntime?.message ?? 'Đang kiểm tra engine dịch local…'}
            </small>
          </div>
        </div>

        {translationRuntime?.supportsSceneContext && (
          <small className="vietsub-translation-helper">
            Qwen3 dịch local theo scene · English/Chinese → Vietnamese · không gửi phụ đề lên Cloud.
          </small>
        )}

        {translationInstallProgress && (
          <div className="vietsub-import-progress compact" role="status" aria-live="polite">
            <div><strong>{getVietsubTranslationInstallStageLabel(translationInstallProgress.stage)}</strong><span>{translationInstallProgress.percent.toFixed(0)}%</span></div>
            <div className="vietsub-progress-track"><span style={{ width: `${translationInstallProgress.percent}%` }} /></div>
            <small>{translationInstallProgress.message}</small>
            {translationInstallProgress.totalBytes > 2 && (
              <small>{formatBytes(translationInstallProgress.bytesProcessed)} / {formatBytes(translationInstallProgress.totalBytes)}</small>
            )}
          </div>
        )}

        {translationRuntimeView.canInstall && !translationInstallProgress && (
          <div className="vietsub-settings-actions vietsub-translation-install-actions">
            <button
              type="button"
              disabled={busy || Boolean(activeJob)}
              aria-label="Tìm, kiểm tra và cài engine dịch local Qwen3 dung lượng 2,50 GB"
              onClick={() => {
                if (window.confirm(getVietsubTranslationRuntimeConfirmation(translationRuntime))) {
                  onInstallTranslationRuntime();
                }
              }}
            >
              <Download size={14} /> {getVietsubTranslationRuntimeActionLabel(translationRuntime)}
            </button>
          </div>
        )}

        {translationJob && <VietsubLocalJobStatus
          job={translationJob}
          title="Dịch tiếng Việt"
          onPause={onPauseJob}
          onResume={onResumeJob}
          onRetry={onRetryJob}
          onCancel={onCancelJob}
        />}

        {translationNotice && (
          <div
            className="vietsub-translation-notice"
            role={translationNotice === 'Đã hoàn thành dịch tiếng Việt.' ? 'status' : 'alert'}
            aria-live="polite"
          >
            <TriangleAlert size={14} />
            <span>{translationNotice}</span>
          </div>
        )}

        <div className="vietsub-settings-actions vietsub-translation-actions">
          <button
            type="button"
            className={`vietsub-translation-action ${translationRuntimeView.canTranslate ? '' : 'unavailable'}`}
            disabled={translationDisabled}
            aria-label={translationRuntimeView.canTranslate
              ? translationDisabled
                ? 'Dịch tiếng Việt đang tạm khóa vì dự án có tác vụ khác đang xử lý'
                : 'Bắt đầu dịch phụ đề OCR sang tiếng Việt'
              : translationRuntimeView.translationActionLabel}
            onClick={() => onStartTranslation('CONTINUE')}
          >
            <Languages size={14} /> {translationRuntimeView.translationActionLabel}
          </button>
        </div>
        {translationRuntimeView.canTranslate && translationDisabled && (
          <small className="vietsub-translation-helper">Hoàn tất hoặc dừng tác vụ hiện tại trước khi dịch.</small>
        )}
      </section>

      <section className="vietsub-settings-section" aria-label="Tạo giọng tiếng Việt local">
        <div className="vietsub-settings-section-title"><Volume2 size={16} /><strong>Giọng đọc tiếng Việt</strong></div>
        <div className={`vietsub-settings-state ${voiceReady ? 'ready' : 'warning'}`}>
          {voiceReady ? <CheckCircle2 size={15} /> : <TriangleAlert size={15} />}
          <div>
            <strong>{voiceReady ? 'Piper local sẵn sàng' : voiceRuntime?.status === 'DISABLED' ? 'Tính năng đang tắt' : 'Chưa cài Piper local'}</strong>
            <small>{voiceRuntime?.message ?? 'Đang kiểm tra runtime tạo giọng…'}</small>
          </div>
        </div>

        <small className="vietsub-translation-helper">
          Một giọng nữ tiếng Việt · chạy CPU trong worker riêng · không gửi phụ đề lên Cloud.
        </small>

        {voiceInstallProgress && (
          <div className="vietsub-import-progress compact" role="status" aria-live="polite">
            <div><strong>{voiceInstallProgress.stage}</strong><span>{voiceInstallProgress.percent.toFixed(0)}%</span></div>
            <div className="vietsub-progress-track"><span style={{ width: `${voiceInstallProgress.percent}%` }} /></div>
            <small>{voiceInstallProgress.message}</small>
            {voiceInstallProgress.totalBytes > 1 && (
              <small>{formatBytes(voiceInstallProgress.bytesProcessed)} / {formatBytes(voiceInstallProgress.totalBytes)}</small>
            )}
          </div>
        )}

        {!voiceReady && voiceRuntime?.status !== 'DISABLED' && !voiceInstallProgress && (
          <div className="vietsub-settings-actions">
            <button
              type="button"
              disabled={busy || Boolean(activeJob)}
              aria-haspopup="dialog"
              aria-expanded={voiceInstallDialogOpen}
              aria-controls="vietsub-voice-install-dialog"
              onClick={() => setVoiceInstallDialogOpen(true)}
            >
              <Download size={14} /> Cài Piper local
            </button>
          </div>
        )}

        {voiceJob && <VietsubLocalJobStatus
          job={voiceJob}
          title="Tạo giọng tiếng Việt"
          onPause={onPauseJob}
          onResume={onResumeJob}
          onRetry={onRetryJob}
          onCancel={onCancelJob}
        />}

        {voiceWorkspace?.timeline && (
          <div className="vietsub-settings-state ready">
            <CheckCircle2 size={15} />
            <div>
              <strong>Timeline giọng Việt đã sẵn sàng</strong>
              <small>{(voiceWorkspace.timeline.durationMilliseconds / 1000).toFixed(1)} giây · 48 kHz · revision {voiceWorkspace.timeline.trackRevision}</small>
            </div>
          </div>
        )}
        {reviewTimingCount > 0 && (
          <div className="vietsub-translation-notice" role="status">
            <TriangleAlert size={14} />
            <span>{reviewTimingCount} phrase dài đã được giới hạn ở tốc độ 1,20x; timeline vẫn được tạo và một số đoạn có thể chồng âm.</span>
          </div>
        )}

        {voiceNotice && (
          <div className="vietsub-translation-notice" role="status" aria-live="polite">
            <Info size={14} /><span>{voiceNotice}</span>
          </div>
        )}

        <div className="vietsub-settings-actions">
          <button type="button" disabled={voiceDisabled} onClick={onStartVoice}>
            <Volume2 size={14} /> Tạo giọng Việt
          </button>
        </div>
        {!voiceEligible && (
          <small className="vietsub-translation-helper">Hãy hoàn thành bản dịch tiếng Việt của toàn bộ cue trước khi tạo giọng.</small>
        )}
      </section>

      {voiceInstallDialogOpen && (
        <VietsubVoiceInstallModal
          requiredBytes={voiceRuntime?.requiredBytes ?? 0}
          modelVersion={voiceRuntime?.modelVersion}
          onDismiss={closeVoiceInstallDialog}
          onConfirm={confirmVoiceRuntimeInstall}
        />
      )}
    </aside>
  );
}

function VietsubLocalJobStatus({
  job,
  title,
  onPause,
  onResume,
  onRetry,
  onCancel
}: {
  job: VietsubJobSummary;
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
        {job.status === 'RUNNING' && <button type="button" onClick={() => onPause(job.id)}><Pause size={13} /> Tạm dừng</button>}
        {(job.status === 'PAUSED' || job.status === 'INTERRUPTED') && <button type="button" onClick={() => onResume(job.id)}><Play size={13} /> Tiếp tục</button>}
        {job.status === 'FAILED' && <button type="button" onClick={() => onRetry(job.id)}><RotateCcw size={13} /> Thử lại</button>}
        {!['COMPLETED', 'CANCELLED'].includes(job.status) && <button type="button" onClick={() => onCancel(job.id)}><Square size={13} /> Hủy</button>}
      </div>
    </div>
  );
}

type RegionDragMode = 'move' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'nw';

function VietsubOcrRegionSelector({
  region,
  videoUrl,
  imageUrl,
  timestampMilliseconds,
  aspectRatio,
  enabled,
  onChange,
  onKeyDown
}: {
  region: VietsubOcrRegion;
  videoUrl?: string;
  imageUrl?: string;
  timestampMilliseconds: number;
  aspectRatio: number;
  enabled: boolean;
  onChange: (region: VietsubOcrRegion) => void;
  onKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => void;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
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
      className="vietsub-ocr-region-preview"
      style={{ aspectRatio: Number.isFinite(aspectRatio) && aspectRatio > 0 ? aspectRatio : 16 / 9 }}
      tabIndex={enabled ? 0 : -1}
      role="application"
      aria-label="Vùng OCR; kéo để di chuyển, dùng tám nút để đổi kích thước, hoặc dùng phím mũi tên"
      onKeyDown={onKeyDown}
    >
      {videoUrl ? (
        <video ref={videoRef} src={videoUrl} muted playsInline preload="metadata" aria-label="Frame video dùng chọn vùng OCR" />
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
    RUNNING: jobType === 'TRANSLATE_LOCAL'
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

function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1);
  return `${(value / 1024 ** index).toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}
