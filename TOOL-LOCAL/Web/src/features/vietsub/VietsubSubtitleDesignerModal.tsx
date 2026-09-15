import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  AlignCenter,
  AlignLeft,
  AlignRight,
  Captions,
  Check,
  Eye,
  EyeOff,
  FlipHorizontal2,
  FlipVertical2,
  Italic,
  Move,
  Pause,
  Play,
  RotateCcw,
  Save,
  SlidersHorizontal,
  Video,
  Volume2,
  VolumeX,
  TriangleAlert,
  Type,
  X
} from 'lucide-react';
import type {
  VietsubAudioMixSettings,
  VietsubMediaSummary,
  VietsubSubtitleStyle,
  VietsubVideoTransformSettings
} from './types';
import {
  cloneAudioMixSettings,
  defaultVietsubAudioMixSettings,
  effectiveOriginalVolume,
  effectiveVoiceVolume
} from './vietsubAudioMix';
import {
  cloneSubtitleStyle,
  defaultVietsubSubtitleStyle,
  hasSubtitleContrastWarning,
  vietsubSubtitlePresets
} from './vietsubSubtitleStyle';
import {
  cloneVideoTransformSettings,
  defaultVietsubVideoTransformSettings
} from './vietsubVideoTransform';
import { VietsubSubtitleOverlay, useVideoContentRect } from './VietsubSubtitleOverlay';
import { useSynchronizedVoice } from './useSynchronizedVoice';
import { VietsubNotice } from './VietsubNotice';

type DesignerTab = 'TEXT' | 'EFFECTS' | 'POSITION' | 'AUDIO';
type PreviewMode = 'FIT' | 'FILL';

export function VietsubSubtitleDesignerModal({
  media,
  style,
  audioMixSettings,
  videoTransformSettings,
  voicePlaybackUrl,
  previewText,
  hasTranslatedSubtitles,
  initialTimeMilliseconds,
  busy,
  notice,
  onPreviewTimeChange,
  onSave,
  onExportVideo,
  onCancelOperation,
  onClose
}: {
  media: VietsubMediaSummary;
  style: VietsubSubtitleStyle;
  audioMixSettings: VietsubAudioMixSettings;
  videoTransformSettings: VietsubVideoTransformSettings;
  voicePlaybackUrl?: string | null;
  previewText?: string | null;
  hasTranslatedSubtitles: boolean;
  initialTimeMilliseconds: number;
  busy: boolean;
  notice?: string | null;
  onPreviewTimeChange: (milliseconds: number) => void;
  onSave: (
    style: VietsubSubtitleStyle,
    audioMixSettings: VietsubAudioMixSettings,
    videoTransformSettings: VietsubVideoTransformSettings
  ) => Promise<boolean>;
  onExportVideo: () => void;
  onCancelOperation: () => void;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState(() => cloneSubtitleStyle(style));
  const [baseline, setBaseline] = useState(() => cloneSubtitleStyle(style));
  const [audioDraft, setAudioDraft] = useState(() => cloneAudioMixSettings(audioMixSettings));
  const [audioBaseline, setAudioBaseline] = useState(() => cloneAudioMixSettings(audioMixSettings));
  const [videoTransformDraft, setVideoTransformDraft] = useState(
    () => cloneVideoTransformSettings(videoTransformSettings)
  );
  const [videoTransformBaseline, setVideoTransformBaseline] = useState(
    () => cloneVideoTransformSettings(videoTransformSettings)
  );
  const [activeTab, setActiveTab] = useState<DesignerTab>('TEXT');
  const [playing, setPlaying] = useState(false);
  const [saving, setSaving] = useState(false);
  const [discardRequested, setDiscardRequested] = useState(false);
  const [subtitlesVisible, setSubtitlesVisible] = useState(true);
  const [previewMode, setPreviewMode] = useState<PreviewMode>('FIT');
  const [previewZoom, setPreviewZoom] = useState(100);
  const [exportRequested, setExportRequested] = useState(false);
  const [currentTime, setCurrentTime] = useState(Math.max(0, initialTimeMilliseconds));
  const [duration, setDuration] = useState(Math.max(1, Math.round(media.durationSeconds * 1000)));
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const voiceAudioRef = useRef<HTMLAudioElement | null>(null);
  const stageRef = useRef<HTMLDivElement | null>(null);
  const initialTimeRef = useRef(Math.max(0, initialTimeMilliseconds));
  const closeButtonRef = useRef<HTMLButtonElement | null>(null);
  const dirtyRef = useRef(false);
  const savingRef = useRef(false);
  const exportBusyObservedRef = useRef(false);
  const dirty = useMemo(
    () => JSON.stringify(draft) !== JSON.stringify(baseline)
      || JSON.stringify(audioDraft) !== JSON.stringify(audioBaseline)
      || JSON.stringify(videoTransformDraft) !== JSON.stringify(videoTransformBaseline),
    [audioBaseline, audioDraft, baseline, draft, videoTransformBaseline, videoTransformDraft]
  );
  dirtyRef.current = dirty;
  savingRef.current = saving;
  const subtitleText = previewText?.trim() || 'Phụ đề tiếng Việt hiển thị tại đây';
  const voiceAvailable = Boolean(voicePlaybackUrl);
  const [voiceSignalActive, setVoiceSignalActive] = useState(false);
  const voiceActive = voiceAvailable
    && !audioDraft.translatedVoiceMuted
    && audioDraft.translatedVoiceVolume > 0
    && voiceSignalActive;
  const originalPreviewVolume = effectiveOriginalVolume(audioDraft, voiceActive);
  const voicePreviewVolume = effectiveVoiceVolume(audioDraft, voiceAvailable);
  const voicePlayback = useSynchronizedVoice(videoRef, voiceAudioRef, media.playbackUrl,
    voicePlaybackUrl, voicePreviewVolume, setVoiceSignalActive);
  const normalizedRotation = ((media.rotationDegrees % 360) + 360) % 360;
  const displayWidth = normalizedRotation === 90 || normalizedRotation === 270
    ? media.height
    : media.width;
  const displayHeight = normalizedRotation === 90 || normalizedRotation === 270
    ? media.width
    : media.height;
  const displayAspectRatio = displayWidth > 0 && displayHeight > 0
    ? displayWidth / displayHeight
    : 16 / 9;
  const contentRect = useVideoContentRect(
    stageRef,
    videoRef,
    displayWidth,
    displayHeight,
    previewMode === 'FILL' ? 'cover' : 'contain'
  );
  const displayRatioLabel = formatAspectRatio(displayWidth, displayHeight);

  const requestClose = useCallback(() => {
    if (savingRef.current || (busy && exportRequested)) return;
    if (dirtyRef.current) {
      setDiscardRequested(true);
      return;
    }
    onClose();
  }, [busy, exportRequested, onClose]);

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeButtonRef.current?.focus();
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      event.preventDefault();
      if (discardRequested) setDiscardRequested(false);
      else requestClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
      previousFocus?.focus();
    };
  }, [discardRequested, requestClose]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const initialSeconds = initialTimeRef.current / 1000;
    if (video.readyState >= HTMLMediaElement.HAVE_METADATA) {
      video.currentTime = initialSeconds;
    }
  }, [media.mediaId]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    return transitionMediaVolume(video, originalPreviewVolume);
  }, [originalPreviewVolume]);

  useEffect(() => {
    if (!exportRequested) return;
    if (busy) {
      exportBusyObservedRef.current = true;
      return;
    }
    if (exportBusyObservedRef.current) {
      exportBusyObservedRef.current = false;
      setExportRequested(false);
    }
  }, [busy, exportRequested]);

  const change = <K extends keyof VietsubSubtitleStyle>(key: K, value: VietsubSubtitleStyle[K]) => {
    setDraft((current) => ({ ...current, [key]: value, presetId: 'CUSTOM' }));
  };

  const changePosition = (xPercent: number, yPercent: number) => {
    setDraft((current) => ({
      ...current,
      presetId: 'CUSTOM',
      verticalPosition: 'CUSTOM',
      positionXPercent: xPercent,
      positionYPercent: yPercent,
      bottomMarginPercent: Math.min(30, Math.max(0, 100 - yPercent))
    }));
  };

  const setVerticalPosition = (verticalPosition: VietsubSubtitleStyle['verticalPosition']) => {
    const positionYPercent = verticalPosition === 'TOP' ? 7 : verticalPosition === 'MIDDLE' ? 50 : 93;
    setDraft((current) => ({
      ...current,
      presetId: 'CUSTOM',
      verticalPosition,
      positionYPercent,
      bottomMarginPercent: verticalPosition === 'BOTTOM' ? 7 : current.bottomMarginPercent
    }));
  };

  const setAlignment = (alignment: VietsubSubtitleStyle['alignment']) => {
    setDraft((current) => ({
      ...current,
      presetId: 'CUSTOM',
      alignment,
      positionXPercent: alignment === 'BOTTOM_LEFT'
        ? Math.max(2, current.horizontalMarginPercent)
        : alignment === 'BOTTOM_RIGHT'
          ? Math.min(98, 100 - current.horizontalMarginPercent)
          : 50
    }));
  };

  const seek = (milliseconds: number) => {
    const next = Math.max(0, Math.min(duration, Math.round(milliseconds)));
    setCurrentTime(next);
    if (videoRef.current) videoRef.current.currentTime = next / 1000;
    voicePlayback.sync(true);
    onPreviewTimeChange(next);
  };

  const togglePlaying = async () => {
    const video = videoRef.current;
    if (!video) return;
    if (video.paused) {
      try {
        if (video.ended) video.currentTime = 0;
        await video.play();
      } catch {
        voicePlayback.stop();
        setPlaying(false);
      }
    } else {
      video.pause();
      voicePlayback.stop();
    }
  };

  const save = async () => {
    if (!dirty || saving || busy) return;
    setSaving(true);
    const saved = await onSave(
      cloneSubtitleStyle(draft),
      cloneAudioMixSettings(audioDraft),
      cloneVideoTransformSettings(videoTransformDraft)
    );
    setSaving(false);
    if (!saved) return;
    setBaseline(cloneSubtitleStyle(draft));
    setAudioBaseline(cloneAudioMixSettings(audioDraft));
    setVideoTransformBaseline(cloneVideoTransformSettings(videoTransformDraft));
    onClose();
  };

  const exportVideo = async () => {
    if (saving || busy || !hasTranslatedSubtitles) return;
    if (dirty) {
      setSaving(true);
      const saved = await onSave(
        cloneSubtitleStyle(draft),
        cloneAudioMixSettings(audioDraft),
        cloneVideoTransformSettings(videoTransformDraft)
      );
      setSaving(false);
      if (!saved) return;
      setBaseline(cloneSubtitleStyle(draft));
      setAudioBaseline(cloneAudioMixSettings(audioDraft));
      setVideoTransformBaseline(cloneVideoTransformSettings(videoTransformDraft));
    }
    setExportRequested(true);
    onExportVideo();
  };

  return (
    <div
      className="confirmation-overlay vietsub-subtitle-designer-overlay"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) requestClose();
      }}
    >
      <section
        className="vietsub-subtitle-designer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="vietsub-subtitle-designer-title"
      >
        <header className="vietsub-subtitle-designer-header">
          <div className="vietsub-subtitle-designer-icon"><Captions size={23} /></div>
          <div>
            <span>THIẾT KẾ THÀNH PHẨM</span>
            <h2 id="vietsub-subtitle-designer-title">Phụ đề, hình ảnh và âm thanh</h2>
            <p>Xem trước phụ đề, lật hình video và cân bằng hai lớp âm thanh của dự án.</p>
          </div>
          <button ref={closeButtonRef} type="button" disabled={busy && exportRequested} onClick={requestClose} aria-label="Đóng thiết kế thành phẩm">
            <X size={19} />
          </button>
        </header>

        <div className="vietsub-subtitle-designer-body">
          <div className="vietsub-subtitle-designer-preview">
            {voicePlaybackUrl && (
              <audio
                ref={voiceAudioRef}
                className="vietsub-voice-playback-engine"
                preload="auto"
                crossOrigin="anonymous"
                src={voicePlaybackUrl}
                aria-hidden="true"
              />
            )}
            {voicePlayback.notice && <VietsubNotice eventId={voicePlayback.noticeId} className="vietsub-media-warning"
              actions={<button type="button" onClick={voicePlayback.retry}>Thử lại giọng</button>}>
              {voicePlayback.notice}
            </VietsubNotice>}
            <div className="vietsub-subtitle-preview-heading">
              <span>KHUNG HÌNH THÀNH PHẨM</span>
              <div className="vietsub-subtitle-preview-tools">
                <button type="button" className={subtitlesVisible ? 'is-active' : ''} onClick={() => setSubtitlesVisible((current) => !current)}>
                  {subtitlesVisible ? <Eye size={13} /> : <EyeOff size={13} />}
                  {subtitlesVisible ? 'Đang hiện' : 'Đang ẩn'}
                </button>
                <button type="button" className={previewMode === 'FIT' ? 'is-active' : ''} onClick={() => { setPreviewMode('FIT'); setPreviewZoom(100); }}>Vừa khung</button>
                <button type="button" className={previewMode === 'FILL' ? 'is-active' : ''} onClick={() => { setPreviewMode('FILL'); setPreviewZoom(100); }}>Lấp đầy</button>
                <button
                  type="button"
                  className={videoTransformDraft.flipHorizontal ? 'is-active' : ''}
                  aria-pressed={videoTransformDraft.flipHorizontal}
                  onClick={() => setVideoTransformDraft((current) => ({
                    ...current,
                    flipHorizontal: !current.flipHorizontal
                  }))}
                >
                  <FlipHorizontal2 size={13} /> Lật trái–phải
                </button>
                <button
                  type="button"
                  className={videoTransformDraft.flipVertical ? 'is-active' : ''}
                  aria-pressed={videoTransformDraft.flipVertical}
                  onClick={() => setVideoTransformDraft((current) => ({
                    ...current,
                    flipVertical: !current.flipVertical
                  }))}
                >
                  <FlipVertical2 size={13} /> Lật trên–dưới
                </button>
                <strong>{displayRatioLabel}</strong><small>{displayWidth} × {displayHeight}</small>
              </div>
            </div>
            <div className="vietsub-subtitle-designer-viewport">
              <div
                ref={stageRef}
                className={`vietsub-subtitle-designer-stage ${previewMode === 'FILL' ? 'is-fill' : displayAspectRatio < 1 ? 'is-portrait' : 'is-landscape'}`}
                style={{
                  aspectRatio: previewMode === 'FILL' ? undefined : displayAspectRatio,
                  transform: `scale(${previewZoom / 100})`
                }}
              >
                <video
                  ref={videoRef}
                  src={media.playbackUrl}
                  preload="metadata"
                  playsInline
                  style={{
                    objectFit: previewMode === 'FILL' ? 'cover' : 'contain',
                    transform: `scale(${videoTransformDraft.flipHorizontal ? -1 : 1}, ${videoTransformDraft.flipVertical ? -1 : 1})`
                  }}
                  onClick={togglePlaying}
                  onLoadedMetadata={(event) => {
                    const video = event.currentTarget;
                    const nextDuration = Number.isFinite(video.duration)
                      ? Math.max(1, Math.round(video.duration * 1000))
                      : duration;
                    setDuration(nextDuration);
                    video.currentTime = Math.min(nextDuration, initialTimeRef.current) / 1000;
                    video.volume = originalPreviewVolume;
                  }}
                  onTimeUpdate={(event) => {
                    const next = Math.round(event.currentTarget.currentTime * 1000);
                    setCurrentTime(next);
                    onPreviewTimeChange(next);
                  }}
                  onPlay={() => setPlaying(true)}
                  onPause={() => {
                    setPlaying(false);
                    voiceAudioRef.current?.pause();
                  }}
                  onEnded={() => {
                    setPlaying(false);
                    voiceAudioRef.current?.pause();
                  }}
                />
                {subtitlesVisible && (
                  <VietsubSubtitleOverlay
                    text={subtitleText}
                    style={draft}
                    contentRect={contentRect}
                    interactive
                    interactionScale={previewZoom / 100}
                    onPositionChange={changePosition}
                  />
                )}
                {hasTranslatedSubtitles && !previewText?.trim() && (
                  <span className="vietsub-subtitle-empty-at-time">Không có phụ đề tại thời điểm này</span>
                )}
                <button className="vietsub-subtitle-stage-play" type="button" onClick={togglePlaying} aria-label={playing ? 'Tạm dừng' : 'Phát video'}>
                  {playing ? <Pause size={20} fill="currentColor" /> : <Play size={20} fill="currentColor" />}
                </button>
              </div>
            </div>
            <div className="vietsub-subtitle-designer-playback">
              <button type="button" onClick={togglePlaying} aria-label={playing ? 'Tạm dừng' : 'Phát video'}>
                {playing ? <Pause size={16} fill="currentColor" /> : <Play size={16} fill="currentColor" />}
              </button>
              <span>{formatTime(currentTime)}</span>
              <input
                type="range"
                min={0}
                max={Math.max(1, duration)}
                step={50}
                value={Math.min(currentTime, duration)}
                aria-label="Tua video xem trước phụ đề"
                onChange={(event) => seek(Number(event.target.value))}
              />
              <span>{formatTime(duration)}</span>
            </div>
            <div className="vietsub-subtitle-preview-zoom">
              <span>Thu phóng</span>
              <input type="range" min={75} max={160} step={5} value={previewZoom} aria-label="Thu phóng khung xem trước" onChange={(event) => setPreviewZoom(Number(event.target.value))} />
              <output>{previewZoom}%</output>
              <button type="button" disabled={previewZoom === 100} onClick={() => setPreviewZoom(100)}>100%</button>
            </div>
            <p className="vietsub-subtitle-preview-note">
              <Move size={12} /> Kéo trực tiếp phụ đề để đặt vị trí; dùng phím mũi tên để tinh chỉnh 0,5% (giữ Shift: 2%).
            </p>
          </div>

          <div className={`vietsub-subtitle-designer-controls ${busy || saving ? 'is-busy' : ''}`} aria-busy={busy || saving}>
            <div className="vietsub-subtitle-presets" aria-label="Mẫu thiết kế phụ đề">
              {vietsubSubtitlePresets.map((preset) => (
                <button
                  type="button"
                  className={draft.presetId === preset.id ? 'is-active' : ''}
                  onClick={() => setDraft(cloneSubtitleStyle(preset.style))}
                  key={preset.id}
                  title={preset.description}
                >
                  {draft.presetId === preset.id && <Check size={13} />}
                  <span>{preset.label}</span>
                </button>
              ))}
            </div>

            <div className="vietsub-subtitle-designer-tabs" role="tablist" aria-label="Nhóm tùy chỉnh">
              <button type="button" role="tab" aria-selected={activeTab === 'TEXT'} className={activeTab === 'TEXT' ? 'is-active' : ''} onClick={() => setActiveTab('TEXT')}>Chữ</button>
              <button type="button" role="tab" aria-selected={activeTab === 'EFFECTS'} className={activeTab === 'EFFECTS' ? 'is-active' : ''} onClick={() => setActiveTab('EFFECTS')}>Hiệu ứng</button>
              <button type="button" role="tab" aria-selected={activeTab === 'POSITION'} className={activeTab === 'POSITION' ? 'is-active' : ''} onClick={() => setActiveTab('POSITION')}>Vị trí</button>
              <button type="button" role="tab" aria-selected={activeTab === 'AUDIO'} className={activeTab === 'AUDIO' ? 'is-active' : ''} onClick={() => setActiveTab('AUDIO')}>Âm thanh</button>
            </div>

            {activeTab === 'TEXT' && (
              <div className="vietsub-subtitle-control-section" role="tabpanel">
                <label className="vietsub-subtitle-wide-field">
                  <span>Phông chữ</span>
                  <select value={draft.fontFamily} onChange={(event) => change('fontFamily', event.target.value as VietsubSubtitleStyle['fontFamily'])}>
                    <option value="Arial">Arial</option>
                    <option value="Segoe UI">Segoe UI</option>
                    <option value="Tahoma">Tahoma</option>
                    <option value="Verdana">Verdana</option>
                    <option value="Times New Roman">Times New Roman</option>
                  </select>
                </label>
                <RangeField label="Kích thước" value={draft.fontSizePercent} min={2} max={10} step={0.1} suffix="% chiều cao" onChange={(value) => change('fontSizePercent', value)} />
                <div className="vietsub-subtitle-format-buttons">
                  <button type="button" className={draft.bold ? 'is-active' : ''} aria-pressed={draft.bold} onClick={() => change('bold', !draft.bold)}><Type size={16} /><strong>B</strong> Đậm</button>
                  <button type="button" className={draft.italic ? 'is-active' : ''} aria-pressed={draft.italic} onClick={() => change('italic', !draft.italic)}><Italic size={16} /> Nghiêng</button>
                </div>
                <ColorField label="Màu chữ" color={draft.textColor} opacity={draft.textOpacity} onColor={(value) => change('textColor', value)} onOpacity={(value) => change('textOpacity', value)} />
                <RangeField label="Khoảng cách dòng" value={draft.lineHeight} min={1} max={2} step={0.05} suffix="×" onChange={(value) => change('lineHeight', value)} />
              </div>
            )}

            {activeTab === 'EFFECTS' && (
              <div className="vietsub-subtitle-control-section" role="tabpanel">
                <ColorField label="Màu viền" color={draft.outlineColor} opacity={draft.outlineOpacity} onColor={(value) => change('outlineColor', value)} onOpacity={(value) => change('outlineOpacity', value)} />
                <RangeField label="Độ dày viền" value={draft.outlineWidthPercent} min={0} max={1} step={0.02} suffix="%" onChange={(value) => change('outlineWidthPercent', value)} />
                <ColorField label="Màu bóng" color={draft.shadowColor} opacity={draft.shadowOpacity} onColor={(value) => change('shadowColor', value)} onOpacity={(value) => change('shadowOpacity', value)} />
                <RangeField label="Độ lệch bóng" value={draft.shadowOffsetPercent} min={0} max={1.5} step={0.02} suffix="%" onChange={(value) => change('shadowOffsetPercent', value)} />
                <label className="vietsub-subtitle-toggle-field">
                  <span><strong>Hộp nền</strong><small>Tăng độ tương phản trên cảnh phức tạp</small></span>
                  <input type="checkbox" checked={draft.backgroundEnabled} onChange={(event) => change('backgroundEnabled', event.target.checked)} />
                </label>
                {draft.backgroundEnabled && (
                  <ColorField label="Màu nền" color={draft.backgroundColor} opacity={draft.backgroundOpacity} onColor={(value) => change('backgroundColor', value)} onOpacity={(value) => change('backgroundOpacity', value)} />
                )}
                {hasSubtitleContrastWarning(draft) && (
                  <div className="vietsub-subtitle-contrast-warning" role="status">
                    <TriangleAlert size={15} />
                    <span><strong>Phụ đề có thể khó đọc</strong><small>Hãy tăng viền hoặc bật hộp nền trước khi xuất video.</small></span>
                  </div>
                )}
              </div>
            )}

            {activeTab === 'POSITION' && (
              <div className="vietsub-subtitle-control-section" role="tabpanel">
                <div className="vietsub-subtitle-alignment-field">
                  <span>Căn chữ</span>
                  <div>
                    <button type="button" className={draft.alignment === 'BOTTOM_LEFT' ? 'is-active' : ''} aria-label="Căn trái" onClick={() => setAlignment('BOTTOM_LEFT')}><AlignLeft size={17} /></button>
                    <button type="button" className={draft.alignment === 'BOTTOM_CENTER' ? 'is-active' : ''} aria-label="Căn giữa" onClick={() => setAlignment('BOTTOM_CENTER')}><AlignCenter size={17} /></button>
                    <button type="button" className={draft.alignment === 'BOTTOM_RIGHT' ? 'is-active' : ''} aria-label="Căn phải" onClick={() => setAlignment('BOTTOM_RIGHT')}><AlignRight size={17} /></button>
                  </div>
                </div>
                <div className="vietsub-subtitle-position-presets">
                  <span>Vị trí nhanh</span>
                  <div>
                    <button type="button" className={draft.verticalPosition === 'TOP' ? 'is-active' : ''} onClick={() => setVerticalPosition('TOP')}>Trên</button>
                    <button type="button" className={draft.verticalPosition === 'MIDDLE' ? 'is-active' : ''} onClick={() => setVerticalPosition('MIDDLE')}>Giữa</button>
                    <button type="button" className={draft.verticalPosition === 'BOTTOM' ? 'is-active' : ''} onClick={() => setVerticalPosition('BOTTOM')}>Dưới</button>
                  </div>
                </div>
                <div className="vietsub-subtitle-position-values">
                  <RangeField label="Tọa độ X" value={draft.positionXPercent} min={2} max={98} step={0.5} suffix="%" onChange={(value) => changePosition(value, draft.positionYPercent)} />
                  <RangeField label="Tọa độ Y" value={draft.positionYPercent} min={2} max={98} step={0.5} suffix="%" onChange={(value) => changePosition(draft.positionXPercent, value)} />
                </div>
                <RangeField label="Chiều rộng tối đa" value={draft.maxWidthPercent} min={40} max={100} step={1} suffix="%" onChange={(value) => setDraft((current) => ({ ...current, presetId: 'CUSTOM', maxWidthPercent: value, horizontalMarginPercent: Math.min(current.horizontalMarginPercent, (100 - value) / 2) }))} />
                <RangeField label="Số dòng mục tiêu" value={draft.maxLines} min={1} max={3} step={1} suffix="dòng" onChange={(value) => change('maxLines', value as VietsubSubtitleStyle['maxLines'])} />
              </div>
            )}

            {activeTab === 'AUDIO' && (
              <div className="vietsub-subtitle-control-section vietsub-audio-mixer" role="tabpanel">
                <div className="vietsub-audio-mixer-heading">
                  <span><SlidersHorizontal size={17} /></span>
                  <div>
                    <strong>Cân bằng hai lớp âm thanh</strong>
                    <small>Nghe thử trực tiếp và dùng đúng mức này khi xuất MP4.</small>
                  </div>
                </div>
                <AudioChannel
                  label="Âm thanh gốc"
                  description={media.hasAudio ? 'Âm thanh đang có trong video nguồn' : 'Video nguồn không có âm thanh'}
                  value={audioDraft.originalVolume}
                  max={1}
                  muted={audioDraft.originalMuted}
                  disabled={!media.hasAudio}
                  onVolume={(value) => setAudioDraft((current) => ({ ...current, originalVolume: value, originalMuted: value > 0 ? false : current.originalMuted }))}
                  onMute={() => setAudioDraft((current) => ({ ...current, originalMuted: !current.originalMuted }))}
                />
                <AudioChannel
                  label="Giọng dịch tiếng Việt"
                  description={voiceAvailable ? 'Timeline giọng Việt đang sẵn sàng' : 'Hãy tạo giọng Việt để nghe và trộn kênh này'}
                  value={audioDraft.translatedVoiceVolume}
                  max={1.5}
                  muted={audioDraft.translatedVoiceMuted}
                  disabled={!voiceAvailable}
                  onVolume={(value) => setAudioDraft((current) => ({ ...current, translatedVoiceVolume: value, translatedVoiceMuted: value > 0 ? false : current.translatedVoiceMuted }))}
                  onMute={() => setAudioDraft((current) => ({ ...current, translatedVoiceMuted: !current.translatedVoiceMuted }))}
                />
                <label className={`vietsub-audio-duck-toggle ${!media.hasAudio || !voiceAvailable ? 'is-disabled' : ''}`}>
                  <span>
                    <strong>Tự giảm âm gốc khi có giọng Việt</strong>
                    <small>Âm gốc hạ nhẹ lúc có lời dịch và tự trở lại ở khoảng nghỉ.</small>
                  </span>
                  <input
                    type="checkbox"
                    checked={audioDraft.autoDuckOriginal}
                    disabled={!media.hasAudio || !voiceAvailable}
                    onChange={(event) => setAudioDraft((current) => ({ ...current, autoDuckOriginal: event.target.checked }))}
                  />
                </label>
                <div className="vietsub-audio-mix-presets" aria-label="Thiết lập âm thanh nhanh">
                  <button type="button" onClick={() => setAudioDraft(cloneAudioMixSettings(defaultVietsubAudioMixSettings))}>Cân bằng</button>
                  <button type="button" disabled={!voiceAvailable} onClick={() => setAudioDraft((current) => ({ ...current, originalVolume: 0.15, translatedVoiceVolume: 1.15, originalMuted: false, translatedVoiceMuted: false, autoDuckOriginal: true }))}>Nổi giọng Việt</button>
                  <button type="button" disabled={!voiceAvailable} onClick={() => setAudioDraft((current) => ({ ...current, originalVolume: 0, translatedVoiceVolume: 1, originalMuted: true, translatedVoiceMuted: false, autoDuckOriginal: false }))}>Chỉ giọng Việt</button>
                </div>
                {audioDraft.translatedVoiceVolume > 1.25 && !audioDraft.translatedVoiceMuted && (
                  <div className="vietsub-audio-level-warning" role="status">
                    <TriangleAlert size={15} />
                    <span>Giọng Việt đang được khuếch đại mạnh. Bộ giới hạn sẽ chống vỡ tiếng khi xuất.</span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>

        <footer className="vietsub-subtitle-designer-footer">
          <span>{notice || (dirty ? 'Có thay đổi chưa lưu' : 'Hình ảnh, phụ đề và âm thanh đã đồng bộ với dự án')}</span>
          <div>
            <button type="button" className="is-secondary" disabled={saving || busy} onClick={() => {
              setDraft(cloneSubtitleStyle(defaultVietsubSubtitleStyle));
              setAudioDraft(cloneAudioMixSettings(defaultVietsubAudioMixSettings));
              setVideoTransformDraft(cloneVideoTransformSettings(defaultVietsubVideoTransformSettings));
            }}>
              <RotateCcw size={15} /> Mặc định
            </button>
            <button type="button" className="is-secondary" disabled={saving || (busy && exportRequested)} onClick={requestClose}>Hủy</button>
            <button
              type="button"
              className="is-export"
              disabled={saving || !hasTranslatedSubtitles || (busy && !exportRequested)}
              title={hasTranslatedSubtitles ? 'Lưu MP4 đã chèn phụ đề tiếng Việt' : 'Cần có ít nhất một câu đã dịch tiếng Việt'}
              onClick={busy && exportRequested ? onCancelOperation : exportVideo}
            >
              {busy && exportRequested ? <X size={16} /> : <Video size={16} />}
              {busy && exportRequested ? 'Hủy xuất' : 'Xuất MP4'}
            </button>
            <button type="button" className="is-primary" disabled={!dirty || saving || busy} onClick={save}>
              <Save size={16} /> {saving ? 'Đang lưu...' : 'Lưu thay đổi'}
            </button>
          </div>
        </footer>

        {discardRequested && (
          <div className="vietsub-subtitle-discard-layer" role="presentation">
            <div role="alertdialog" aria-modal="true" aria-labelledby="vietsub-discard-style-title">
              <strong id="vietsub-discard-style-title">Bỏ các thay đổi thiết kế?</strong>
              <p>Thiết kế đang xem trước chưa được lưu vào dự án.</p>
              <div>
                <button type="button" onClick={() => setDiscardRequested(false)}>Tiếp tục chỉnh</button>
                <button type="button" className="is-danger" onClick={onClose}>Bỏ thay đổi</button>
              </div>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}

function AudioChannel({
  label,
  description,
  value,
  max,
  muted,
  disabled,
  onVolume,
  onMute
}: {
  label: string;
  description: string;
  value: number;
  max: number;
  muted: boolean;
  disabled: boolean;
  onVolume: (value: number) => void;
  onMute: () => void;
}) {
  const silent = disabled || muted || value === 0;
  return (
    <div className={`vietsub-audio-channel ${disabled ? 'is-disabled' : ''}`}>
      <div className="vietsub-audio-channel-title">
        <span className={silent ? 'is-muted' : ''}>{silent ? <VolumeX size={16} /> : <Volume2 size={16} />}</span>
        <div><strong>{label}</strong><small>{description}</small></div>
        <button
          type="button"
          disabled={disabled}
          className={muted ? 'is-muted' : ''}
          aria-pressed={muted}
          aria-label={`${muted ? 'Bật' : 'Tắt'} ${label}`}
          onClick={onMute}
        >
          {muted ? 'Bật tiếng' : 'Tắt tiếng'}
        </button>
      </div>
      <label>
        <input
          type="range"
          min={0}
          max={max}
          step={0.05}
          value={value}
          disabled={disabled}
          aria-label={`Âm lượng ${label}`}
          onChange={(event) => onVolume(Number(event.target.value))}
        />
        <output>{Math.round(value * 100)}%</output>
      </label>
    </div>
  );
}

function RangeField({
  label,
  value,
  min,
  max,
  step,
  suffix,
  onChange
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step: number;
  suffix: string;
  onChange: (value: number) => void;
}) {
  return (
    <label className="vietsub-subtitle-range-field">
      <span><strong>{label}</strong><output>{formatValue(value)} {suffix}</output></span>
      <input type="range" min={min} max={max} step={step} value={value} onChange={(event) => onChange(Number(event.target.value))} />
    </label>
  );
}

function ColorField({
  label,
  color,
  opacity,
  onColor,
  onOpacity
}: {
  label: string;
  color: string;
  opacity: number;
  onColor: (value: string) => void;
  onOpacity: (value: number) => void;
}) {
  return (
    <div className="vietsub-subtitle-color-field">
      <label><span>{label}</span><input type="color" value={color} onChange={(event) => onColor(event.target.value.toUpperCase())} /></label>
      <label><span>Độ hiển thị</span><input type="range" min={0} max={1} step={0.05} value={opacity} onChange={(event) => onOpacity(Number(event.target.value))} /><output>{Math.round(opacity * 100)}%</output></label>
    </div>
  );
}

function formatValue(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
}

function formatTime(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function formatAspectRatio(width: number, height: number): string {
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
    return '16:9';
  }
  const knownRatios = [
    { width: 9, height: 16 },
    { width: 16, height: 9 },
    { width: 1, height: 1 },
    { width: 4, height: 5 },
    { width: 4, height: 3 },
    { width: 3, height: 4 },
    { width: 21, height: 9 }
  ];
  const ratio = width / height;
  const known = knownRatios.find((item) => Math.abs(ratio - (item.width / item.height)) < 0.015);
  if (known) return `${known.width}:${known.height}`;
  const divisor = greatestCommonDivisor(Math.round(width), Math.round(height));
  return `${Math.round(width) / divisor}:${Math.round(height) / divisor}`;
}

function greatestCommonDivisor(left: number, right: number): number {
  let a = Math.abs(left);
  let b = Math.abs(right);
  while (b > 0) {
    [a, b] = [b, a % b];
  }
  return Math.max(1, a);
}

function transitionMediaVolume(media: HTMLMediaElement, requestedVolume: number): () => void {
  const target = Math.min(1, Math.max(0, requestedVolume));
  const start = media.volume;
  if (Math.abs(start - target) < 0.005 || typeof window.requestAnimationFrame !== 'function') {
    media.volume = target;
    return () => { };
  }
  const startedAt = performance.now();
  const duration = 140;
  let frame = 0;
  const update = (now: number) => {
    const progress = Math.min(1, (now - startedAt) / duration);
    media.volume = start + ((target - start) * progress);
    if (progress < 1) frame = window.requestAnimationFrame(update);
  };
  frame = window.requestAnimationFrame(update);
  return () => window.cancelAnimationFrame(frame);
}
