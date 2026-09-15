import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties, PointerEvent as ReactPointerEvent } from 'react';
import { ArrowLeft, LoaderCircle, TriangleAlert } from 'lucide-react';
import type { VietsubAudioMixSettings, VietsubProjectSummary, VietsubSaveState } from './types';
import type { VietsubPageProps } from './VietsubPage';
import { VietsubPreviewPanel } from './VietsubPreviewPanel';
import { VietsubSubtitleDesignerModal } from './VietsubSubtitleDesignerModal';
import { VietsubSettingsPanel } from './VietsubSettingsPanel';
import {
  VietsubSubtitleEditor,
  type VietsubSubtitleEditorHandle
} from './VietsubSubtitleEditor';
import { VietsubTimeline } from './VietsubTimeline';
import { effectiveOriginalVolume, effectiveVoiceVolume } from './vietsubAudioMix';
import { useSynchronizedVoice } from './useSynchronizedVoice';
import { VietsubNotice } from './VietsubNotice';

type VietsubEditorWorkspaceProps = VietsubPageProps & {
  project: VietsubProjectSummary;
};

type VietsubEditorLayout = {
  settingsWidth: number;
  inspectorWidth: number;
  timelineHeight: number;
};

type VietsubResizeMode = 'settings' | 'inspector' | 'timeline';

type VietsubResizeState = {
  mode: VietsubResizeMode;
  originClientX: number;
  originClientY: number;
  initialSettingsWidth: number;
  initialInspectorWidth: number;
  initialTimelineHeight: number;
};

const defaultVietsubEditorLayout: VietsubEditorLayout = {
  settingsWidth: 280,
  inspectorWidth: 420,
  timelineHeight: 250
};

const vietsubEditorLayoutStoragePrefix = 'videomaker.vietsub.editor-layout';
const settingsWidthBounds = { min: 220, max: 420 };
const inspectorWidthBounds = { min: 320, max: 620 };
const timelineHeightBounds = { min: 235, max: 500 };

function clampVietsubEditorLayout(layout: VietsubEditorLayout): VietsubEditorLayout {
  return {
    settingsWidth: clampNumber(layout.settingsWidth, settingsWidthBounds.min, settingsWidthBounds.max, defaultVietsubEditorLayout.settingsWidth),
    inspectorWidth: clampNumber(layout.inspectorWidth, inspectorWidthBounds.min, inspectorWidthBounds.max, defaultVietsubEditorLayout.inspectorWidth),
    timelineHeight: clampNumber(layout.timelineHeight, timelineHeightBounds.min, timelineHeightBounds.max, defaultVietsubEditorLayout.timelineHeight)
  };
}

function clampNumber(value: number, min: number, max: number, fallback: number): number {
  return Number.isFinite(value) ? Math.min(max, Math.max(min, Math.round(value))) : fallback;
}

function readVietsubEditorLayout(projectId: string): VietsubEditorLayout {
  if (typeof window === 'undefined') return defaultVietsubEditorLayout;
  try {
    const raw = window.localStorage.getItem(`${vietsubEditorLayoutStoragePrefix}.${projectId}`);
    if (!raw) return defaultVietsubEditorLayout;
    return clampVietsubEditorLayout({ ...defaultVietsubEditorLayout, ...JSON.parse(raw) });
  } catch {
    return defaultVietsubEditorLayout;
  }
}

export function VietsubEditorWorkspace({
  state,
  project,
  onCloseProject,
  onImportMedia,
  onUpdateOcrSettings,
  onPreviewOcr,
  onStartOcr,
  onStartTranslation,
  onStartCloudTranslation,
  onRefreshCloudAvailability,
  onInstallTranslationRuntime,
  onStartVoice,
  onInstallVoiceRuntime,
  onRefreshVoiceModels,
  onInstallVoiceModel,
  onSelectVoice,
  onPauseJob,
  onResumeJob,
  onRetryJob,
  onCancelJob,
  onActivateOcrTrack,
  onImportSrt,
  onActivateSubtitleTrack,
  onLoadSubtitlePage,
  onLoadTimelineWindow,
  onRequestTimelineThumbnails,
  onRequestTimelineWaveform,
  onUpdateSubtitleCue,
  onUpdateSubtitleStyle,
  onUpdateCueVoice,
  onUpdateTimelineCue,
  onSplitSubtitleCue,
  onAlignSubtitleCue,
  onDuplicateSubtitleCue,
  onDeleteSubtitleCue,
  onExportSrt,
  onExportVideo,
  onCancelOperation,
  onRegisterBeforeLeave
}: VietsubEditorWorkspaceProps) {
  const [playheadMilliseconds, setPlayheadMilliseconds] = useState(0);
  const [durationMilliseconds, setDurationMilliseconds] = useState(
    Math.max(0, Math.round((project.sourceVideo?.durationSeconds ?? 0) * 1000))
  );
  const [playing, setPlaying] = useState(false);
  const [playbackRate, setPlaybackRate] = useState(1);
  const [audioPreviewSettings, setAudioPreviewSettings] = useState<VietsubAudioMixSettings>(() => ({
    ...state.audioMixSettings
  }));
  const [voiceEnabled, setVoiceEnabled] = useState(Boolean(
    state.voiceWorkspace?.timeline?.status === 'READY'
    && state.voiceWorkspace.timelinePlaybackUrl
    && !state.audioMixSettings.translatedVoiceMuted
    && state.audioMixSettings.translatedVoiceVolume > 0
  ));
  const [subtitlesVisible, setSubtitlesVisible] = useState(true);
  const [subtitleDesignerOpen, setSubtitleDesignerOpen] = useState(false);
  const [selectedCueId, setSelectedCueId] = useState<string | null>(null);
  const [selectedCueIndex, setSelectedCueIndex] = useState<number | null>(null);
  const [navigationVersion, setNavigationVersion] = useState(0);
  const navigationRequest = useRef(0);
  const [, setSaveState] = useState<VietsubSaveState>('saved');
  const [closing, setClosing] = useState(false);
  const closingRef = useRef(false);
  const [closeFailed, setCloseFailed] = useState(false);
  const [videoExporting, setVideoExporting] = useState(false);
  const [settingsDrawerOpen, setSettingsDrawerOpen] = useState(false);
  const [compactPanel, setCompactPanel] = useState<'preview' | 'subtitles' | 'settings'>('preview');
  const [editorLayout, setEditorLayout] = useState<VietsubEditorLayout>(() => readVietsubEditorLayout(project.projectId));
  const [resizeState, setResizeState] = useState<VietsubResizeState | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const voiceAudioRef = useRef<HTMLAudioElement | null>(null);
  const playheadRef = useRef(0);
  const subtitleEditorRef = useRef<VietsubSubtitleEditorHandle | null>(null);
  const layoutHydratedRef = useRef(false);
  const busy = state.loading || state.busy || closing;
  const voiceSelectionBusy = busy || Boolean(state.activeJob);
  const voicePlaybackUrl = state.voiceWorkspace?.timelinePlaybackUrl ?? null;
  const voiceAvailable = Boolean(
    state.voiceWorkspace?.timeline?.status === 'READY'
    && voicePlaybackUrl
  );
  const voiceGain = effectiveVoiceVolume(audioPreviewSettings, voiceEnabled && voiceAvailable);
  const [voiceSignalActive, setVoiceSignalActive] = useState(false);
  const voicePlayback = useSynchronizedVoice(videoRef, voiceAudioRef, project.sourceVideo?.playbackUrl,
    voicePlaybackUrl, voiceGain, setVoiceSignalActive);

  useEffect(() => {
    navigationRequest.current++;
    videoRef.current?.pause();
    voiceAudioRef.current?.pause();
    playheadRef.current = 0;
    setPlayheadMilliseconds(0);
    setDurationMilliseconds(Math.max(0, Math.round((project.sourceVideo?.durationSeconds ?? 0) * 1000)));
    setPlaying(false);
    setPlaybackRate(1);
    setAudioPreviewSettings({ ...state.audioMixSettings });
    setVoiceEnabled(false);
    setSubtitlesVisible(true);
    setSubtitleDesignerOpen(false);
    setSelectedCueId(null);
    setSelectedCueIndex(null);
    setSaveState('saved');
    setClosing(false);
    setSettingsDrawerOpen(false);
    setCompactPanel('preview');
  }, [project.projectId, project.sourceVideo?.mediaId]);

  useEffect(() => {
    setAudioPreviewSettings({ ...state.audioMixSettings });
  }, [
    state.audioMixSettings.autoDuckOriginal,
    state.audioMixSettings.originalMuted,
    state.audioMixSettings.originalVolume,
    state.audioMixSettings.translatedVoiceMuted,
    state.audioMixSettings.translatedVoiceVolume
  ]);

  useEffect(() => {
    const shouldEnableVoice = (
      voiceAvailable
      && !state.audioMixSettings.translatedVoiceMuted
      && state.audioMixSettings.translatedVoiceVolume > 0
    );
    setVoiceEnabled(shouldEnableVoice);
    if (!shouldEnableVoice) voiceAudioRef.current?.pause();
  }, [
    state.audioMixSettings.translatedVoiceMuted,
    state.audioMixSettings.translatedVoiceVolume,
    voiceAvailable,
    voicePlaybackUrl,
    project.projectId,
    project.sourceVideo?.mediaId
  ]);

  useEffect(() => {
    layoutHydratedRef.current = false;
    setEditorLayout(readVietsubEditorLayout(project.projectId));
    setResizeState(null);
  }, [project.projectId]);

  useEffect(() => {
    if (!layoutHydratedRef.current) {
      layoutHydratedRef.current = true;
      return;
    }
    try {
      window.localStorage.setItem(
        `${vietsubEditorLayoutStoragePrefix}.${project.projectId}`,
        JSON.stringify(editorLayout)
      );
    } catch {
      // Layout persistence is optional when local storage is unavailable.
    }
  }, [editorLayout, project.projectId]);

  const beginResize = useCallback((event: ReactPointerEvent<HTMLDivElement>, mode: VietsubResizeMode) => {
    if (event.button !== 0) return;
    event.preventDefault();
    setResizeState({
      mode,
      originClientX: event.clientX,
      originClientY: event.clientY,
      initialSettingsWidth: editorLayout.settingsWidth,
      initialInspectorWidth: editorLayout.inspectorWidth,
      initialTimelineHeight: editorLayout.timelineHeight
    });
  }, [editorLayout]);

  useEffect(() => {
    if (!resizeState) return;
    const onPointerMove = (event: PointerEvent) => {
      const deltaX = event.clientX - resizeState.originClientX;
      const deltaY = event.clientY - resizeState.originClientY;
      setEditorLayout((current) => {
        if (resizeState.mode === 'settings') {
          return {
            ...current,
            settingsWidth: clampNumber(
              resizeState.initialSettingsWidth + deltaX,
              settingsWidthBounds.min,
              settingsWidthBounds.max,
              current.settingsWidth
            )
          };
        }
        if (resizeState.mode === 'inspector') {
          return {
            ...current,
            inspectorWidth: clampNumber(
              resizeState.initialInspectorWidth - deltaX,
              inspectorWidthBounds.min,
              inspectorWidthBounds.max,
              current.inspectorWidth
            )
          };
        }
        return {
          ...current,
          timelineHeight: clampNumber(
            resizeState.initialTimelineHeight - deltaY,
            timelineHeightBounds.min,
            timelineHeightBounds.max,
            current.timelineHeight
          )
        };
      });
    };
    const stopResize = () => setResizeState(null);
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', stopResize, { once: true });
    window.addEventListener('pointercancel', stopResize, { once: true });
    return () => {
      window.removeEventListener('pointermove', onPointerMove);
      window.removeEventListener('pointerup', stopResize);
      window.removeEventListener('pointercancel', stopResize);
    };
  }, [resizeState]);

  const adjustLayoutFromKeyboard = useCallback((event: React.KeyboardEvent<HTMLDivElement>, mode: VietsubResizeMode) => {
    const isHorizontal = mode === 'timeline';
    const isIncrease = isHorizontal ? event.key === 'ArrowUp' : event.key === 'ArrowRight';
    const isDecrease = isHorizontal ? event.key === 'ArrowDown' : event.key === 'ArrowLeft';
    if (!isIncrease && !isDecrease && event.key !== 'Home' && event.key !== 'End') return;
    event.preventDefault();
    const bounds = mode === 'settings'
      ? settingsWidthBounds
      : mode === 'inspector'
        ? inspectorWidthBounds
        : timelineHeightBounds;
    const currentValue = mode === 'settings'
      ? editorLayout.settingsWidth
      : mode === 'inspector'
        ? editorLayout.inspectorWidth
        : editorLayout.timelineHeight;
    const delta = event.shiftKey ? 80 : 16;
    const nextValue = event.key === 'Home'
      ? bounds.min
      : event.key === 'End'
        ? bounds.max
        : currentValue + (isIncrease === (mode !== 'inspector') ? delta : -delta);
    setEditorLayout((current) => {
      const value = clampNumber(nextValue, bounds.min, bounds.max, currentValue);
      if (mode === 'settings') return { ...current, settingsWidth: value };
      if (mode === 'inspector') return { ...current, inspectorWidth: value };
      return { ...current, timelineHeight: value };
    });
  }, [editorLayout]);

  const flushPendingEdits = useCallback(
    () => subtitleEditorRef.current?.flushPendingEdits() ?? Promise.resolve(true),
    []
  );

  const exportTimelineVideo = useCallback(() => {
    void subtitleEditorRef.current?.exportVideo();
  }, []);

  useEffect(
    () => onRegisterBeforeLeave(flushPendingEdits),
    [flushPendingEdits, onRegisterBeforeLeave]
  );

  const closeEditor = useCallback(async () => {
    if (busy || videoExporting || closingRef.current) return;
    closingRef.current = true;
    setClosing(true);
    setCloseFailed(false);
    try {
      if (!await flushPendingEdits()) return;
      if (!await onCloseProject()) setCloseFailed(true);
    } catch {
      setCloseFailed(true);
    } finally {
      closingRef.current = false;
      setClosing(false);
    }
  }, [busy, videoExporting, flushPendingEdits, onCloseProject]);

  const seek = useCallback((positionMilliseconds: number) => {
    const next = Math.max(
      0,
      durationMilliseconds > 0
        ? Math.min(positionMilliseconds, durationMilliseconds)
        : positionMilliseconds
    );
    playheadRef.current = next;
    setPlayheadMilliseconds(next);
    const video = videoRef.current;
    if (video && Math.abs(video.currentTime * 1000 - next) >= 10) {
      video.currentTime = next / 1000;
    }
    voicePlayback.sync(true);
  }, [durationMilliseconds, voicePlayback.sync]);

  const updatePlayhead = useCallback((positionMilliseconds: number) => {
    playheadRef.current = positionMilliseconds;
    setPlayheadMilliseconds(positionMilliseconds);
  }, []);

  const openSubtitleDesigner = useCallback(() => {
    if (!project.sourceVideo?.playbackUrl || busy) return;
    void flushPendingEdits().then((saved) => {
      if (!saved) return;
      videoRef.current?.pause();
      voiceAudioRef.current?.pause();
      setPlaying(false);
      setSubtitleDesignerOpen(true);
    });
  }, [busy, flushPendingEdits, project.sourceVideo?.playbackUrl]);

  const closeSubtitleDesigner = useCallback(() => {
    setSubtitleDesignerOpen(false);
    seek(playheadRef.current);
  }, [seek]);

  const getPlayheadMilliseconds = useCallback(() => playheadRef.current, []);

  const togglePlaying = useCallback(async () => {
    const video = videoRef.current;
    if (!video) return;
    if (!video.paused && !video.ended) {
      video.pause();
      voiceAudioRef.current?.pause();
      return;
    }
    try {
      if (video.ended) video.currentTime = 0;
      await video.play();
    } catch {
      voicePlayback.stop();
      setPlaying(false);
    }
  }, [voicePlayback.stop]);

  const changePlaybackRate = useCallback((value: number) => {
    const next = Math.max(0.5, Math.min(2, value));
    setPlaybackRate(next);
    if (videoRef.current) videoRef.current.playbackRate = next;
    if (voiceAudioRef.current) voiceAudioRef.current.playbackRate = next;
  }, []);

  const changeVolume = useCallback((value: number) => {
    const next = Math.max(0, Math.min(1, value));
    setAudioPreviewSettings((current) => ({
      ...current,
      originalVolume: next,
      originalMuted: next > 0 ? false : current.originalMuted
    }));
  }, []);

  const toggleMuted = useCallback(() => {
    setAudioPreviewSettings((current) => ({ ...current, originalMuted: !current.originalMuted }));
  }, []);

  const toggleVoice = useCallback(() => {
    if (!voiceAvailable) return;
    setVoiceEnabled(current => !current);
  }, [voiceAvailable]);

  const updatePlaying = useCallback((next: boolean) => {
    setPlaying(next);
    if (!next) voiceAudioRef.current?.pause();
  }, []);

  const selectCue = useCallback((cueId: string, positionMilliseconds: number, cueIndex: number) => {
    const request = ++navigationRequest.current;
    void flushPendingEdits().then(saved => {
      if (!saved || request !== navigationRequest.current) return;
      setSelectedCueId(cueId);
      setSelectedCueIndex(cueIndex);
      setNavigationVersion(value => value + 1);
      seek(positionMilliseconds);
    });
  }, [flushPendingEdits, seek]);

  const activateSubtitleTrack = useCallback((trackId: string) => {
    navigationRequest.current++;
    setSelectedCueId(null);
    setSelectedCueIndex(null);
    onActivateSubtitleTrack(trackId);
  }, [onActivateSubtitleTrack]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (subtitleDesignerOpen || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
      const target = event.target;
      if (target instanceof HTMLElement && (
        target.isContentEditable
        || ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].includes(target.tagName)
      )) return;

      if (event.code === 'Space' || event.key.toLowerCase() === 'k') {
        event.preventDefault();
        void togglePlaying();
      } else if (event.key === 'ArrowLeft' || event.key.toLowerCase() === 'j') {
        event.preventDefault();
        seek(playheadMilliseconds - 5000);
      } else if (event.key === 'ArrowRight' || event.key.toLowerCase() === 'l') {
        event.preventDefault();
        seek(playheadMilliseconds + 5000);
      } else if (event.key.toLowerCase() === 'm') {
        event.preventDefault();
        toggleMuted();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [playheadMilliseconds, seek, subtitleDesignerOpen, toggleMuted, togglePlaying]);

  const activePageCue = useMemo(
    () => state.subtitlePage?.cues.find((cue) => (
      playheadMilliseconds >= cue.startMilliseconds
      && playheadMilliseconds < cue.endMilliseconds
    )) ?? null,
    [playheadMilliseconds, state.subtitlePage]
  );

  const activeSubtitleText = useMemo(() => {
    if (activePageCue?.translatedText.trim()) return activePageCue.translatedText;
    const timelineCue = state.timelineWindow?.cues.find((cue) => (
      playheadMilliseconds >= cue.startMilliseconds
      && playheadMilliseconds < cue.endMilliseconds
    ));
    return timelineCue?.hasTranslation ? timelineCue.previewText : '';
  }, [activePageCue, playheadMilliseconds, state.timelineWindow]);

  const originalPlaybackVolume = effectiveOriginalVolume(
    audioPreviewSettings,
    voiceGain > 0 && voiceSignalActive
  );

  const previewTimelineAudioMix = useCallback((next: VietsubAudioMixSettings) => {
    const voiceChanged = next.translatedVoiceMuted !== audioPreviewSettings.translatedVoiceMuted
      || next.translatedVoiceVolume !== audioPreviewSettings.translatedVoiceVolume;
    setAudioPreviewSettings({ ...next });
    if (voiceChanged && voiceAvailable) {
      setVoiceEnabled(!next.translatedVoiceMuted && next.translatedVoiceVolume > 0);
    }
  }, [audioPreviewSettings.translatedVoiceMuted, audioPreviewSettings.translatedVoiceVolume, voiceAvailable]);

  const updateTimelineAudioMix = useCallback(
    (next: VietsubAudioMixSettings) => onUpdateSubtitleStyle(
      state.subtitleStyle,
      next,
      state.videoTransformSettings
    ),
    [onUpdateSubtitleStyle, state.subtitleStyle, state.videoTransformSettings]
  );

  return (
    <div className="vietsub-editor-workspace">
      {closeFailed && (
        <div className="vietsub-inline-error" role="alert">
          Chưa thể quay về màn hình tạo dự án. Hãy thử lại.
        </div>
      )}
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
      {project.needsRecovery && (
        <VietsubNotice eventId={project.projectId} className="vietsub-editor-recovery" icon={<TriangleAlert size={17} />}>
          <div><strong>Dự án được phục hồi sau lần đóng trước.</strong><span>Hãy kiểm tra video và nội dung gần nhất trước khi tiếp tục.</span></div>
        </VietsubNotice>
      )}

      <nav className="vietsub-editor-panel-tabs" aria-label="Chọn bảng biên tập">
        <button type="button" className={compactPanel === 'preview' ? 'is-active' : ''} onClick={() => setCompactPanel('preview')}>Xem trước</button>
        <button type="button" className={compactPanel === 'subtitles' ? 'is-active' : ''} onClick={() => setCompactPanel('subtitles')}>Phụ đề</button>
        <button
          type="button"
          className={compactPanel === 'settings' || settingsDrawerOpen ? 'is-active' : ''}
          onClick={() => {
            setCompactPanel('settings');
            setSettingsDrawerOpen((value) => !value);
          }}
        >Thiết lập</button>
      </nav>

      <div
        className={`vietsub-editor-layout ${resizeState ? 'is-resizing' : ''}`}
        style={{
          '--settings-panel-width': `${editorLayout.settingsWidth}px`,
          '--inspector-panel-width': `${editorLayout.inspectorWidth}px`,
          '--timeline-height': `${editorLayout.timelineHeight}px`
        } as CSSProperties}
      >
      <main className="vietsub-editor-grid">
        <div className={`vietsub-settings-slot ${settingsDrawerOpen ? 'is-drawer-open' : ''} ${compactPanel === 'settings' ? 'is-compact-active' : ''}`}>
          <VietsubSettingsPanel
            project={project}
            headerAction={
              <button
                type="button"
                className="vietsub-new-project-button"
                disabled={busy || videoExporting}
                onClick={() => { void closeEditor(); }}
                title="Quay về màn hình tạo dự án phụ đề"
              >
                {closing ? <LoaderCircle size={14} className="spin" /> : <ArrowLeft size={14} />}
                <span>{closing ? 'Đang quay về…' : 'Tạo dự án mới'}</span>
              </button>
            }
            subtitleWorkspace={state.subtitleWorkspace}
            progress={state.mediaImportProgress}
            busy={busy}
            onImportMedia={onImportMedia}
            ocrSettings={state.ocrSettings}
            ocrRuntime={state.ocrRuntime}
            ocrPreview={state.ocrPreview}
            translationRuntime={state.translationRuntime}
            cloudAvailability={state.cloudAvailability}
            translationInstallProgress={state.translationInstallProgress}
            translationNotice={state.translationNotice}
            translationNoticeId={state.noticeEvents?.translationNotice?.id}
            voiceWorkspace={state.voiceWorkspace}
            voiceRuntime={state.voiceRuntime}
            voiceInstallProgress={state.voiceInstallProgress}
            voiceModels={state.voiceModels}
            voiceModelInstallProgress={state.voiceModelInstallProgress}
            voiceNotice={state.voiceNotice}
            voiceNoticeId={state.noticeEvents?.voiceNotice?.id}
            activeJob={state.activeJob}
            activationRequest={state.ocrActivationRequest}
            playheadMilliseconds={playheadMilliseconds}
            onSeek={seek}
            onUpdateOcrSettings={onUpdateOcrSettings}
            onPreviewOcr={onPreviewOcr}
            onStartOcr={onStartOcr}
            onStartTranslation={onStartTranslation}
            onStartCloudTranslation={onStartCloudTranslation}
            onRefreshCloudAvailability={onRefreshCloudAvailability}
            onInstallTranslationRuntime={onInstallTranslationRuntime}
            onStartVoice={onStartVoice}
            onInstallVoiceRuntime={onInstallVoiceRuntime}
            onRefreshVoiceModels={onRefreshVoiceModels}
            onInstallVoiceModel={onInstallVoiceModel}
            onSelectVoice={onSelectVoice}
            onCancelVoiceModelInstall={onCancelOperation}
            onPauseJob={onPauseJob}
            onResumeJob={onResumeJob}
            onRetryJob={onRetryJob}
            onCancelJob={onCancelJob}
            onActivateOcrTrack={onActivateOcrTrack}
          />
        </div>
        <VietsubLayoutResizeHandle
          mode="settings"
          value={editorLayout.settingsWidth}
          min={settingsWidthBounds.min}
          max={settingsWidthBounds.max}
          active={resizeState?.mode === 'settings'}
          onStart={beginResize}
          onKeyDown={adjustLayoutFromKeyboard}
        />
        <div className={`vietsub-preview-slot ${compactPanel === 'preview' ? 'is-compact-active' : ''}`}>
          <VietsubPreviewPanel
            project={project}
            videoRef={videoRef}
            progress={state.mediaImportProgress}
            busy={busy}
            playheadMilliseconds={playheadMilliseconds}
            durationMilliseconds={durationMilliseconds}
            playing={playing}
            playbackRate={playbackRate}
            volume={audioPreviewSettings.originalVolume}
            playbackVolume={originalPlaybackVolume}
            muted={audioPreviewSettings.originalMuted}
            subtitlesVisible={subtitlesVisible}
            activeSubtitleText={activeSubtitleText}
            subtitleStyle={state.subtitleStyle}
            onImportMedia={onImportMedia}
            onPlayheadChange={updatePlayhead}
            onDurationChange={setDurationMilliseconds}
            onPlayingChange={updatePlaying}
            onTogglePlaying={togglePlaying}
            onSeek={seek}
            onPlaybackRateChange={changePlaybackRate}
            onVolumeChange={changeVolume}
            onToggleMuted={toggleMuted}
            onToggleSubtitles={() => setSubtitlesVisible((current) => !current)}
          />
        </div>
        <VietsubLayoutResizeHandle
          mode="inspector"
          value={editorLayout.inspectorWidth}
          min={inspectorWidthBounds.min}
          max={inspectorWidthBounds.max}
          active={resizeState?.mode === 'inspector'}
          onStart={beginResize}
          onKeyDown={adjustLayoutFromKeyboard}
        />
        <div className={`vietsub-inspector-panel ${compactPanel === 'subtitles' ? 'is-compact-active' : ''}`}>
          <VietsubSubtitleEditor
            onUpdateCueVoice={onUpdateCueVoice}
            voiceSelectionBusy={voiceSelectionBusy}
            ref={subtitleEditorRef}
            workspace={state.subtitleWorkspace}
            page={state.subtitlePage}
            busy={busy}
            notice={state.subtitleNotice}
            noticeId={state.noticeEvents?.subtitleNotice?.id}
            playing={playing}
            navigationVersion={navigationVersion}
            sourceLanguageCode={project.sourceLanguageCode}
            activeCueId={activePageCue?.cueId}
            getPlayheadMilliseconds={getPlayheadMilliseconds}
            selectedCueId={selectedCueId}
            selectedCueIndex={selectedCueIndex}
            onImportSrt={onImportSrt}
            onActivateTrack={activateSubtitleTrack}
            onLoadPage={onLoadSubtitlePage}
            onUpdateCue={onUpdateSubtitleCue}
            onSplitCue={onSplitSubtitleCue}
            onAlignCue={onAlignSubtitleCue}
            onDuplicateCue={onDuplicateSubtitleCue}
            onDeleteCue={onDeleteSubtitleCue}
            onExportSrt={onExportSrt}
            canExportVideo={Boolean(project.sourceVideo?.playbackUrl)}
            onExportVideo={onExportVideo}
            onExportStateChange={setVideoExporting}
            canDesignSubtitle={Boolean(project.sourceVideo?.playbackUrl)}
            onOpenSubtitleDesigner={openSubtitleDesigner}
            onSelectCue={selectCue}
            onSaveStateChange={setSaveState}
          />
        </div>
      </main>

      <VietsubLayoutResizeHandle
        mode="timeline"
        value={editorLayout.timelineHeight}
        min={timelineHeightBounds.min}
        max={timelineHeightBounds.max}
        active={resizeState?.mode === 'timeline'}
        onStart={beginResize}
        onKeyDown={adjustLayoutFromKeyboard}
      />
      <VietsubTimeline
        canExportVideo={Boolean(project.sourceVideo?.playbackUrl && state.subtitleWorkspace?.activeTrackId)}
        exporting={videoExporting}
        onExportVideo={exportTimelineVideo}
        onUpdateCueVoice={onUpdateCueVoice}
        voiceSelectionBusy={voiceSelectionBusy}
        media={project.sourceVideo}
        mediaEvent={state.timelineMediaEvent}
        trackId={state.subtitleWorkspace?.activeTrackId}
        window={state.timelineWindow}
        playheadMilliseconds={playheadMilliseconds}
        playing={playing}
        voiceWorkspace={state.voiceWorkspace}
        voiceEnabled={voiceEnabled}
        audioMixSettings={state.audioMixSettings}
        busy={busy}
        selectedCueId={selectedCueId}
        onSeek={seek}
        onSelectCue={selectCue}
        onLoadWindow={onLoadTimelineWindow}
        onRequestThumbnails={onRequestTimelineThumbnails}
        onRequestWaveform={onRequestTimelineWaveform}
        onUpdateCue={onUpdateTimelineCue}
        onToggleVoice={toggleVoice}
        onPreviewAudioMix={previewTimelineAudioMix}
        onUpdateAudioMix={updateTimelineAudioMix}
      />
      </div>
      {subtitleDesignerOpen && project.sourceVideo?.playbackUrl && (
        <VietsubSubtitleDesignerModal
          media={project.sourceVideo}
          style={state.subtitleStyle}
          audioMixSettings={state.audioMixSettings}
          videoTransformSettings={state.videoTransformSettings}
          voicePlaybackUrl={state.voiceWorkspace?.timelinePlaybackUrl}
          previewText={activeSubtitleText}
          hasTranslatedSubtitles={Boolean(state.subtitleWorkspace?.tracks.some((track) => track.translatedCueCount > 0))}
          initialTimeMilliseconds={playheadMilliseconds}
          busy={busy}
          notice={state.subtitleNotice}
          onPreviewTimeChange={updatePlayhead}
          onSave={onUpdateSubtitleStyle}
          onExportVideo={onExportVideo}
          onCancelOperation={onCancelOperation}
          onClose={closeSubtitleDesigner}
        />
      )}
    </div>
  );
}

type VietsubLayoutResizeHandleProps = {
  mode: VietsubResizeMode;
  value: number;
  min: number;
  max: number;
  active: boolean;
  onStart: (event: ReactPointerEvent<HTMLDivElement>, mode: VietsubResizeMode) => void;
  onKeyDown: (event: React.KeyboardEvent<HTMLDivElement>, mode: VietsubResizeMode) => void;
};

function VietsubLayoutResizeHandle({
  mode,
  value,
  min,
  max,
  active,
  onStart,
  onKeyDown
}: VietsubLayoutResizeHandleProps) {
  const timeline = mode === 'timeline';
  const label = mode === 'settings'
    ? 'Điều chỉnh chiều rộng Thiết lập dự án'
    : mode === 'inspector'
      ? 'Điều chỉnh chiều rộng Biên tập theo từng cue'
      : 'Điều chỉnh chiều cao Timeline';
  return (
    <div
      className={`vietsub-layout-resizer ${timeline ? 'horizontal' : 'vertical'} ${active ? 'is-active' : ''}`}
      role="separator"
      tabIndex={0}
      aria-label={label}
      aria-orientation={timeline ? 'horizontal' : 'vertical'}
      aria-valuemin={min}
      aria-valuemax={max}
      aria-valuenow={value}
      aria-valuetext={`${value}px`}
      onPointerDown={(event) => onStart(event, mode)}
      onKeyDown={(event) => onKeyDown(event, mode)}
    />
  );
}
