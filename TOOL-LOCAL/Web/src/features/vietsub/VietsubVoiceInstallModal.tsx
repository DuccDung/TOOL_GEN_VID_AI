import { useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import { CircleCheck, Download, Pause, Play, RefreshCw, TriangleAlert, Volume2, X } from 'lucide-react';
import type { VietsubVoiceModelInstallProgress, VietsubVoiceModelStatus } from './types';
import { vietsubVoicePreviewSamples } from './vietsubVoicePreviewSamples';

export function VietsubVoiceInstallModal({
  models,
  installProgress,
  errorMessage,
  busy,
  onDismiss,
  onRefresh,
  onInstall,
  selectedVoiceId,
  canCreate,
  onSelect,
  onCreate,
  onCancelInstall
}: {
  models?: VietsubVoiceModelStatus[] | null;
  installProgress?: VietsubVoiceModelInstallProgress | null;
  errorMessage?: string | null;
  busy: boolean;
  onDismiss: () => void;
  onRefresh: () => void;
  onInstall: (voiceId: string) => void;
  selectedVoiceId?: string | null;
  canCreate?: boolean;
  onSelect?: (voiceId: string) => void;
  onCreate?: () => void;
  onCancelInstall?: () => void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const audioRef = useRef<HTMLAudioElement>(null);
  const currentPreviewVoiceIdRef = useRef<string | null>(null);
  const previewRequestRef = useRef(0);
  const [playingVoiceId, setPlayingVoiceId] = useState<string | null>(null);
  const [loadingVoiceId, setLoadingVoiceId] = useState<string | null>(null);
  const [previewError, setPreviewError] = useState<{ voiceId: string; message: string } | null>(null);

  const clearAudio = () => {
    previewRequestRef.current += 1;
    currentPreviewVoiceIdRef.current = null;
    const audio = audioRef.current;
    if (audio) {
      audio.pause();
      audio.removeAttribute('src');
      audio.load();
    }
  };

  const stopPreview = () => {
    clearAudio();
    setPlayingVoiceId(null);
    setLoadingVoiceId(null);
    setPreviewError(null);
  };

  const dismiss = () => {
    stopPreview();
    onDismiss();
  };

  const playPreview = (voiceId: string) => {
    const source = vietsubVoicePreviewSamples[voiceId];
    const audio = audioRef.current;
    if (!source || !audio) return;

    if (currentPreviewVoiceIdRef.current === voiceId
      && (playingVoiceId === voiceId || loadingVoiceId === voiceId)) {
      previewRequestRef.current += 1;
      audio.pause();
      setPlayingVoiceId(null);
      setLoadingVoiceId(null);
      return;
    }

    if (currentPreviewVoiceIdRef.current !== voiceId) {
      previewRequestRef.current += 1;
      audio.pause();
      audio.src = source;
      currentPreviewVoiceIdRef.current = voiceId;
      setPlayingVoiceId(null);
    }

    const request = ++previewRequestRef.current;
    setLoadingVoiceId(voiceId);
    setPreviewError(null);
    try {
      void audio.play().then(() => {
        if (request !== previewRequestRef.current || currentPreviewVoiceIdRef.current !== voiceId) return;
        setPlayingVoiceId(voiceId);
        setLoadingVoiceId(null);
      }).catch(() => {
        if (request !== previewRequestRef.current || currentPreviewVoiceIdRef.current !== voiceId) return;
        currentPreviewVoiceIdRef.current = null;
        audio.removeAttribute('src');
        setLoadingVoiceId(null);
        setPlayingVoiceId(null);
        setPreviewError({ voiceId, message: 'Không thể phát âm thanh mẫu. Hãy thử lại.' });
      });
    } catch {
      if (request !== previewRequestRef.current) return;
      currentPreviewVoiceIdRef.current = null;
      audio.removeAttribute('src');
      setLoadingVoiceId(null);
      setPreviewError({ voiceId, message: 'Không thể phát âm thanh mẫu. Hãy thử lại.' });
    }
  };

  useEffect(() => () => clearAudio(), []);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeButtonRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        dismiss();
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
    const buttons = Array.from(
      dialogRef.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []
    );
    if (buttons.length === 0) return;
    const firstButton = buttons[0];
    const lastButton = buttons[buttons.length - 1];
    if (event.shiftKey && document.activeElement === firstButton) {
      event.preventDefault();
      lastButton.focus();
    } else if (!event.shiftKey && document.activeElement === lastButton) {
      event.preventDefault();
      firstButton.focus();
    }
  };

  const modal = (
    <div className="confirmation-overlay vietsub-voice-install-overlay" role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) dismiss();
      }}>
      <section ref={dialogRef} id="vietsub-voice-model-dialog"
        className="confirmation-card confirmation-download vietsub-voice-install-modal"
        role="dialog" aria-modal="true" aria-labelledby="vietsub-voice-model-title"
        aria-describedby="vietsub-voice-model-description" onKeyDown={keepFocusInside}>
        <button ref={closeButtonRef} className="confirmation-close" type="button"
          onClick={dismiss} aria-label="Đóng danh sách giọng local"><X size={18} /></button>

        <div className="confirmation-icon confirmation-icon-download" aria-hidden="true">
          <Volume2 size={25} />
        </div>
        <span className="confirmation-eyebrow vietsub-voice-install-eyebrow">TÀI NGUYÊN GIỌNG LOCAL</span>
        <h2 id="vietsub-voice-model-title">Chọn giọng tạo phụ đề</h2>
        <p id="vietsub-voice-model-description">
          Nghe thử, chọn giọng cho dự án rồi cài nếu cần. Chỉ giọng đã qua kiểm tra runtime
          mới được dùng để tạo âm thanh cho phụ đề.
        </p>

        <div className="vietsub-voice-model-toolbar">
          <span>{models ? `${models.length} giọng local` : 'Đang kiểm tra các giọng trên máy...'}</span>
          <button type="button" onClick={onRefresh} disabled={busy}>
            <RefreshCw size={15} /> Kiểm tra lại
          </button>
        </div>

        {errorMessage && <p className="vietsub-voice-model-error" role="alert">{errorMessage}</p>}

        <audio ref={audioRef} preload="none" aria-hidden="true" style={{ display: 'none' }}
          onPause={(event) => {
            if (event.currentTarget.paused) setPlayingVoiceId(null);
          }}
          onEnded={(event) => {
            if (!event.currentTarget.ended) return;
            clearAudio();
            setPlayingVoiceId(null);
            setLoadingVoiceId(null);
          }}
          onError={(event) => {
            if (!event.currentTarget.error) return;
            const voiceId = currentPreviewVoiceIdRef.current;
            if (!voiceId) return;
            previewRequestRef.current += 1;
            currentPreviewVoiceIdRef.current = null;
            setLoadingVoiceId(null);
            setPlayingVoiceId(null);
            setPreviewError({ voiceId, message: 'Âm thanh mẫu không khả dụng. Hãy thử lại.' });
          }} />

        <div className="vietsub-voice-model-list" aria-label="Danh sách giọng local">
          {models?.map((model) => {
            const installing = installProgress?.voiceId === model.voiceId;
            const canInstall = model.status === 'NOT_INSTALLED' || model.status === 'INVALID'
              || (model.status === 'READY' && !model.synthesisReady);
            const selected = selectedVoiceId === model.voiceId;
            const previewPlaying = playingVoiceId === model.voiceId;
            const previewLoading = loadingVoiceId === model.voiceId;
            const previewActive = currentPreviewVoiceIdRef.current === model.voiceId;
            const previewLabel = previewPlaying || previewLoading ? 'Tạm dừng'
              : previewActive ? 'Tiếp tục' : 'Nghe thử';
            const resourceLabel = `${model.engineId === 'PIPER_LOCAL' ? 'Piper' : 'Kokoro Vietnamese'} · Bộ tài nguyên ${formatDownloadSize(model.requiredBytes)}`;
            const hasPreviewError = !installing && previewError?.voiceId === model.voiceId;
            const detailMessage = installing ? installProgress.message
              : hasPreviewError ? previewError.message : model.synthesisMessage ?? model.message;
            return (
              <div className={`vietsub-voice-model-row${selected ? ' is-selected' : ''}`} key={model.voiceId}>
                <div className="vietsub-voice-model-info">
                  <div className="vietsub-voice-model-heading">
                    <strong>{model.displayName}</strong>
                    {selected && <span className="vietsub-voice-model-status is-selected">Đang chọn</span>}
                    {model.status === 'READY' ? (
                      <span className="vietsub-voice-model-status is-ready"><CircleCheck size={16} />
                        {model.synthesisReady ? 'Sẵn sàng tạo giọng' : 'Model đã cài'}</span>
                    ) : model.status === 'DISABLED' ? (
                      <span className="vietsub-voice-model-status">Chưa khả dụng</span>
                    ) : model.status === 'INVALID' ? (
                      <span className="vietsub-voice-model-status is-invalid"><TriangleAlert size={15} /> Cần cài lại</span>
                    ) : null}
                  </div>
                  <small className="vietsub-voice-model-detail" title={resourceLabel}>{resourceLabel}</small>
                  <small className={`vietsub-voice-model-detail${hasPreviewError ? ' vietsub-voice-preview-error' : ''}`}
                    title={detailMessage} role={hasPreviewError ? 'alert' : undefined}>{detailMessage}</small>
                </div>
                <div className="vietsub-voice-model-action">
                  <button type="button" className="vietsub-voice-model-button vietsub-voice-model-preview"
                    disabled={!vietsubVoicePreviewSamples[model.voiceId]}
                    aria-label={`${previewLabel} giọng ${model.displayName}`}
                    aria-pressed={previewPlaying}
                    onClick={() => playPreview(model.voiceId)}>
                    {previewPlaying || previewLoading ? <Pause size={15} /> : <Play size={15} />}
                    {previewLoading ? 'Đang tải...' : previewLabel}
                  </button>
                  <button type="button" className="vietsub-voice-model-button vietsub-voice-model-select"
                    disabled={busy || model.status === 'DISABLED' || selected}
                    aria-pressed={selected}
                    onClick={() => onSelect?.(model.voiceId)}>
                    {selected ? 'Đang chọn' : 'Chọn giọng'}
                  </button>
                  {canInstall && (
                    <button type="button" className="vietsub-voice-model-button vietsub-voice-model-install"
                      disabled={busy} onClick={() => onInstall(model.voiceId)}>
                      <Download size={16} /> {installing ? `${installProgress.percent.toFixed(0)}%`
                        : model.status === 'INVALID' ? 'Cài lại' : model.status === 'READY' ? 'Kiểm tra runtime' : 'Cài giọng'}
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        <div className="confirmation-actions">
          <button className="confirmation-submit" type="button"
            disabled={busy || !canCreate || !models?.some(model =>
              model.voiceId === selectedVoiceId && model.synthesisReady)}
            onClick={() => onCreate?.()}>Tạo giọng bằng giọng đã chọn</button>
          {busy && installProgress && onCancelInstall && (
            <button className="confirmation-cancel" type="button" onClick={onCancelInstall}>Hủy tải</button>
          )}
          <button className="confirmation-cancel" type="button" onClick={dismiss}>Đóng</button>
        </div>
      </section>
    </div>
  );

  return typeof document === 'undefined' ? modal : createPortal(modal, document.body);
}

function formatDownloadSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return 'chưa xác định';
  const megabytes = bytes / (1024 * 1024);
  return `khoảng ${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 }).format(megabytes)} MB`;
}
