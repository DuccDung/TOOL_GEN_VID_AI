import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { CSSProperties, PointerEvent as ReactPointerEvent } from 'react';
import { TriangleAlert } from 'lucide-react';
import type { VietsubProjectSummary, VietsubSaveState } from './types';
import type { VietsubPageProps } from './VietsubPage';
import { VietsubPreviewPanel } from './VietsubPreviewPanel';
import { VietsubSettingsPanel } from './VietsubSettingsPanel';
import {
  VietsubSubtitleEditor,
  type VietsubSubtitleEditorHandle
} from './VietsubSubtitleEditor';
import { VietsubTimeline } from './VietsubTimeline';

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
  timelineHeight: 196
};

const vietsubEditorLayoutStoragePrefix = 'videomaker.vietsub.editor-layout';
const settingsWidthBounds = { min: 220, max: 420 };
const inspectorWidthBounds = { min: 320, max: 620 };
const timelineHeightBounds = { min: 190, max: 500 };

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
  onUpdateTimelineCue,
  onSplitSubtitleCue,
  onAlignSubtitleCue,
  onDuplicateSubtitleCue,
  onDeleteSubtitleCue,
  onExportSrt,
  onRegisterBeforeLeave
}: VietsubEditorWorkspaceProps) {
  const [playheadMilliseconds, setPlayheadMilliseconds] = useState(0);
  const [durationMilliseconds, setDurationMilliseconds] = useState(
    Math.max(0, Math.round((project.sourceVideo?.durationSeconds ?? 0) * 1000))
  );
  const [playing, setPlaying] = useState(false);
  const [playbackRate, setPlaybackRate] = useState(1);
  const [volume, setVolume] = useState(1);
  const [muted, setMuted] = useState(false);
  const [subtitlesVisible, setSubtitlesVisible] = useState(true);
  const [selectedCueId, setSelectedCueId] = useState<string | null>(null);
  const [, setSaveState] = useState<VietsubSaveState>('saved');
  const [closing, setClosing] = useState(false);
  const [settingsDrawerOpen, setSettingsDrawerOpen] = useState(false);
  const [compactPanel, setCompactPanel] = useState<'preview' | 'subtitles' | 'settings'>('preview');
  const [editorLayout, setEditorLayout] = useState<VietsubEditorLayout>(() => readVietsubEditorLayout(project.projectId));
  const [resizeState, setResizeState] = useState<VietsubResizeState | null>(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const playheadRef = useRef(0);
  const subtitleEditorRef = useRef<VietsubSubtitleEditorHandle | null>(null);
  const layoutHydratedRef = useRef(false);
  const busy = state.loading || state.busy || closing;

  useEffect(() => {
    videoRef.current?.pause();
    playheadRef.current = 0;
    setPlayheadMilliseconds(0);
    setDurationMilliseconds(Math.max(0, Math.round((project.sourceVideo?.durationSeconds ?? 0) * 1000)));
    setPlaying(false);
    setPlaybackRate(1);
    setVolume(1);
    setMuted(false);
    setSubtitlesVisible(true);
    setSelectedCueId(null);
    setSaveState('saved');
    setClosing(false);
    setSettingsDrawerOpen(false);
    setCompactPanel('preview');
  }, [project.projectId, project.sourceVideo?.mediaId]);

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

  useEffect(
    () => onRegisterBeforeLeave(flushPendingEdits),
    [flushPendingEdits, onRegisterBeforeLeave]
  );

  const closeEditor = useCallback(async () => {
    if (busy) return;
    setClosing(true);
    const flushed = await flushPendingEdits();
    if (!flushed) {
      setClosing(false);
      return;
    }
    const closed = await onCloseProject();
    if (!closed) setClosing(false);
  }, [busy, flushPendingEdits, onCloseProject]);

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
  }, [durationMilliseconds]);

  const updatePlayhead = useCallback((positionMilliseconds: number) => {
    playheadRef.current = positionMilliseconds;
    setPlayheadMilliseconds(positionMilliseconds);
  }, []);

  const getPlayheadMilliseconds = useCallback(() => playheadRef.current, []);

  const togglePlaying = useCallback(async () => {
    const video = videoRef.current;
    if (!video) return;
    if (!video.paused && !video.ended) {
      video.pause();
      return;
    }
    try {
      await video.play();
    } catch {
      setPlaying(false);
    }
  }, []);

  const changePlaybackRate = useCallback((value: number) => {
    const next = Math.max(0.5, Math.min(2, value));
    setPlaybackRate(next);
    if (videoRef.current) videoRef.current.playbackRate = next;
  }, []);

  const changeVolume = useCallback((value: number) => {
    const next = Math.max(0, Math.min(1, value));
    setVolume(next);
    if (videoRef.current) videoRef.current.volume = next;
    if (next > 0 && muted) {
      setMuted(false);
      if (videoRef.current) videoRef.current.muted = false;
    }
  }, [muted]);

  const toggleMuted = useCallback(() => {
    setMuted((current) => {
      const next = !current;
      if (videoRef.current) videoRef.current.muted = next;
      return next;
    });
  }, []);

  const selectCue = useCallback((cueId: string, positionMilliseconds: number) => {
    setSelectedCueId(cueId);
    seek(positionMilliseconds);
  }, [seek]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
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
  }, [playheadMilliseconds, seek, toggleMuted, togglePlaying]);

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

  return (
    <div className="vietsub-editor-workspace">
      {project.needsRecovery && (
        <section className="vietsub-editor-recovery" role="status">
          <TriangleAlert size={17} />
          <div><strong>Dự án được phục hồi sau lần đóng trước.</strong><span>Hãy kiểm tra video và nội dung gần nhất trước khi tiếp tục.</span></div>
        </section>
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
            subtitleWorkspace={state.subtitleWorkspace}
            progress={state.mediaImportProgress}
            busy={busy}
            onImportMedia={onImportMedia}
            ocrSettings={state.ocrSettings}
            ocrRuntime={state.ocrRuntime}
            ocrPreview={state.ocrPreview}
            activeJob={state.activeJob}
            activationRequest={state.ocrActivationRequest}
            playheadMilliseconds={playheadMilliseconds}
            onUpdateOcrSettings={onUpdateOcrSettings}
            onPreviewOcr={onPreviewOcr}
            onStartOcr={onStartOcr}
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
            volume={volume}
            muted={muted}
            subtitlesVisible={subtitlesVisible}
            activeSubtitleText={activeSubtitleText}
            onImportMedia={onImportMedia}
            onPlayheadChange={updatePlayhead}
            onDurationChange={setDurationMilliseconds}
            onPlayingChange={setPlaying}
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
            ref={subtitleEditorRef}
            workspace={state.subtitleWorkspace}
            page={state.subtitlePage}
            busy={busy}
            notice={state.subtitleNotice}
            sourceLanguageCode={project.sourceLanguageCode}
            activeCueId={activePageCue?.cueId}
            getPlayheadMilliseconds={getPlayheadMilliseconds}
            selectedCueId={selectedCueId}
            onImportSrt={onImportSrt}
            onActivateTrack={onActivateSubtitleTrack}
            onLoadPage={onLoadSubtitlePage}
            onUpdateCue={onUpdateSubtitleCue}
            onSplitCue={onSplitSubtitleCue}
            onAlignCue={onAlignSubtitleCue}
            onDuplicateCue={onDuplicateSubtitleCue}
            onDeleteCue={onDeleteSubtitleCue}
            onExportSrt={onExportSrt}
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
        media={project.sourceVideo}
        mediaEvent={state.timelineMediaEvent}
        trackId={state.subtitleWorkspace?.activeTrackId}
        window={state.timelineWindow}
        playheadMilliseconds={playheadMilliseconds}
        playing={playing}
        busy={busy}
        selectedCueId={selectedCueId}
        onSeek={seek}
        onSelectCue={selectCue}
        onLoadWindow={onLoadTimelineWindow}
        onRequestThumbnails={onRequestTimelineThumbnails}
        onRequestWaveform={onRequestTimelineWaveform}
        onUpdateCue={onUpdateTimelineCue}
      />
      </div>
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
