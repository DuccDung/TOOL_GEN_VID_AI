import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import {
  Captions,
  Film,
  Focus,
  LockKeyhole,
  Play,
  Volume2,
  ZoomIn,
  ZoomOut
} from 'lucide-react';
import type {
  VietsubMediaSummary,
  VietsubTimelineMediaEvent,
  VietsubTimelineCue,
  VietsubTimelineCueUpdate,
  VietsubTimelineWindow,
  VietsubTimelineWindowQuery
} from './types';
import {
  calculateViewportRange,
  clampTimelineZoom,
  fitTimelineZoom,
  pixelToTime,
  rulerStepMilliseconds,
  snapTimelineTime,
  timeToPixel,
  timelineContentWidth
} from './timelineGeometry';
import {
  initialTimelineMediaLoadState,
  markTimelineMediaFailed,
  markTimelineMediaLoaded,
  markTimelineMediaLoading,
  markTimelineMediaReady,
  markTimelineMediaRequested,
  prioritizeTimelineThumbnailIndices,
  retryTimelineMedia,
  selectTimelineThumbnailIndices,
  shouldResetTimelineMediaState,
  timelineMediaRetryDelay,
  type TimelineMediaLoadState
} from './timelineMediaState';

type VietsubTimelineProps = {
  media?: VietsubMediaSummary | null;
  mediaEvent?: VietsubTimelineMediaEvent | null;
  trackId?: string | null;
  window?: VietsubTimelineWindow | null;
  playheadMilliseconds: number;
  playing: boolean;
  busy: boolean;
  selectedCueId?: string | null;
  onSeek: (milliseconds: number) => void;
  onSelectCue: (cueId: string, milliseconds: number) => void;
  onLoadWindow: (query: VietsubTimelineWindowQuery) => void;
  onRequestThumbnails: (sourceSha256: string, indices: number[]) => void;
  onRequestWaveform: (sourceSha256: string) => void;
  onUpdateCue: (update: VietsubTimelineCueUpdate) => Promise<boolean>;
};

type CueDrag = {
  cue: VietsubTimelineCue;
  mode: 'move' | 'start' | 'end';
  originClientX: number;
  startMilliseconds: number;
  endMilliseconds: number;
  moved: boolean;
  captureTarget: HTMLElement;
  pointerId: number;
};

export function VietsubTimeline({
  media,
  mediaEvent,
  trackId,
  window: timelineWindow,
  playheadMilliseconds,
  playing,
  busy,
  selectedCueId,
  onSeek,
  onSelectCue,
  onLoadWindow,
  onRequestThumbnails,
  onRequestWaveform,
  onUpdateCue
}: VietsubTimelineProps) {
  const durationMilliseconds = Math.max(0, Math.round((media?.durationSeconds ?? 0) * 1000));
  const [pixelsPerSecond, setPixelsPerSecond] = useState(40);
  const [autoFollow, setAutoFollow] = useState(true);
  const [viewportWidth, setViewportWidth] = useState(1);
  const [visibleRange, setVisibleRange] = useState({ startMilliseconds: 0, endMilliseconds: 1 });
  const [drag, setDrag] = useState<CueDrag | null>(null);
  const [thumbnailLoads, setThumbnailLoads] = useState<Record<string, TimelineMediaLoadState>>({});
  const [waveformLoad, setWaveformLoad] = useState<TimelineMediaLoadState | null>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const requestTimerRef = useRef<number | null>(null);
  const mediaRetryTimersRef = useRef(new Map<string, number>());
  const dragRef = useRef<CueDrag | null>(null);
  const playheadCleanupRef = useRef<(() => void) | null>(null);
  const previousMediaRef = useRef<VietsubMediaSummary | null>(null);
  const effectivePixelsPerSecond = fitTimelineZoom(
    durationMilliseconds,
    viewportWidth,
    pixelsPerSecond
  );
  const contentWidth = timelineContentWidth(
    durationMilliseconds,
    effectivePixelsPerSecond,
    viewportWidth
  );
  const rulerStep = rulerStepMilliseconds(effectivePixelsPerSecond);
  const gridStepPixels = timeToPixel(rulerStep, effectivePixelsPerSecond);

  useEffect(() => {
    dragRef.current = drag;
  }, [drag]);

  useEffect(() => {
    const previous = previousMediaRef.current;
    previousMediaRef.current = media ?? null;
    if (!media || shouldResetTimelineMediaState(
      previous?.mediaId,
      media.mediaId,
      previous?.sha256,
      media.sha256
    )) {
      for (const timer of mediaRetryTimersRef.current.values()) window.clearTimeout(timer);
      mediaRetryTimersRef.current.clear();
      setThumbnailLoads({});
      setWaveformLoad(null);
    }
  }, [media?.mediaId, media?.sha256]);

  useEffect(() => {
    if (!media) return;
    setThumbnailLoads((current) => {
      let changed = false;
      const next = { ...current };
      for (const thumbnail of media.timelineThumbnails ?? []) {
        const key = thumbnailArtifactKey(media, thumbnail.index);
        const existing = next[key] ?? initialTimelineMediaLoadState();
        if (!next[key] || existing.revision !== thumbnail.revision) {
          next[key] = markTimelineMediaReady(existing, thumbnail.revision);
          changed = true;
        }
      }
      return changed ? next : current;
    });
    if (media.waveformStatus === 'READY' && media.waveformUrl) {
      setWaveformLoad((current) => {
        const existing = current ?? initialTimelineMediaLoadState();
        return existing.revision === media.waveformRevision && current
          ? current
          : markTimelineMediaReady(existing, media.waveformRevision);
      });
    } else if (media.waveformStatus === 'NO_AUDIO') {
      setWaveformLoad(null);
    }
  }, [media]);

  useEffect(() => {
    if (!media || !mediaEvent
      || mediaEvent.mediaId !== media.mediaId
      || mediaEvent.sourceSha256?.toLowerCase() !== media.sha256.toLowerCase()) return;
    if (mediaEvent.resourceType === 'thumbnail' && Number.isInteger(mediaEvent.index)) {
      const key = thumbnailArtifactKey(media, mediaEvent.index!);
      setThumbnailLoads((current) => {
        const existing = current[key] ?? initialTimelineMediaLoadState();
        return {
          ...current,
          [key]: mediaEvent.kind === 'ready'
            ? markTimelineMediaReady(existing, mediaEvent.revision ?? existing.revision)
            : markTimelineMediaFailed(
                existing,
                mediaEvent.errorCode ?? 'vietsub_thumbnail_generation_failed'
              )
        };
      });
    }
    if (mediaEvent.resourceType === 'waveform') {
      setWaveformLoad((current) => {
        const existing = current ?? initialTimelineMediaLoadState();
        return mediaEvent.kind === 'ready'
          ? markTimelineMediaReady(existing, mediaEvent.revision ?? existing.revision)
          : markTimelineMediaFailed(
              existing,
              mediaEvent.errorCode ?? 'vietsub_waveform_generation_failed'
            );
      });
    }
  }, [media, mediaEvent]);

  useEffect(() => {
    if (Object.values(thumbnailLoads).some((state) => state.phase === 'ready')) {
      setThumbnailLoads((current) => Object.fromEntries(
        Object.entries(current).map(([key, state]) => [
          key,
          state.phase === 'ready' ? markTimelineMediaLoading(state) : state
        ])
      ));
    }
    if (waveformLoad?.phase === 'ready') {
      setWaveformLoad((current) => current ? markTimelineMediaLoading(current) : current);
    }
  }, [thumbnailLoads, waveformLoad]);

  useEffect(() => () => {
    const activeDrag = dragRef.current;
    if (activeDrag?.captureTarget.hasPointerCapture(activeDrag.pointerId)) {
      activeDrag.captureTarget.releasePointerCapture(activeDrag.pointerId);
    }
    playheadCleanupRef.current?.();
    for (const timer of mediaRetryTimersRef.current.values()) window.clearTimeout(timer);
    mediaRetryTimersRef.current.clear();
  }, []);

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    const updateWidth = () => setViewportWidth(Math.max(1, viewport.clientWidth));
    updateWidth();
    const observer = new ResizeObserver(updateWidth);
    observer.observe(viewport);
    return () => observer.disconnect();
  }, []);

  const requestVisibleWindow = useCallback(() => {
    const viewport = viewportRef.current;
    if (!viewport || durationMilliseconds <= 0) return;
    const range = calculateViewportRange(
      viewport.scrollLeft,
      viewport.clientWidth,
      effectivePixelsPerSecond,
      durationMilliseconds,
      320
    );
    setVisibleRange(range);
    if (!trackId) return;
    onLoadWindow({
      trackId,
      windowStartMilliseconds: range.startMilliseconds,
      windowEndMilliseconds: range.endMilliseconds,
      maximumCues: 400
    });
  }, [durationMilliseconds, effectivePixelsPerSecond, onLoadWindow, trackId]);

  const scheduleWindowRequest = useCallback(() => {
    if (requestTimerRef.current !== null) window.clearTimeout(requestTimerRef.current);
    requestTimerRef.current = window.setTimeout(() => {
      requestTimerRef.current = null;
      requestVisibleWindow();
    }, 120);
  }, [requestVisibleWindow]);

  useEffect(() => {
    scheduleWindowRequest();
    return () => {
      if (requestTimerRef.current !== null) window.clearTimeout(requestTimerRef.current);
    };
  }, [scheduleWindowRequest]);

  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport || !playing || !autoFollow || durationMilliseconds <= 0) return;
    const position = timeToPixel(playheadMilliseconds, effectivePixelsPerSecond);
    const leftBoundary = viewport.scrollLeft + 80;
    const rightBoundary = viewport.scrollLeft + viewport.clientWidth - 80;
    if (position < leftBoundary || position > rightBoundary) {
      viewport.scrollTo({ left: Math.max(0, position - viewport.clientWidth * 0.35), behavior: 'smooth' });
      scheduleWindowRequest();
    }
  }, [autoFollow, durationMilliseconds, effectivePixelsPerSecond, playing, playheadMilliseconds, scheduleWindowRequest]);

  useEffect(() => {
    if (!drag) return;
    const onPointerMove = (event: PointerEvent) => {
      const deltaMilliseconds = (event.clientX - drag.originClientX) * 1000 / effectivePixelsPerSecond;
      const minimumDuration = 100;
      let start = drag.cue.startMilliseconds;
      let end = drag.cue.endMilliseconds;
      const candidates = [0, durationMilliseconds, playheadMilliseconds];
      for (const cue of timelineWindow?.cues ?? []) {
        if (cue.cueId !== drag.cue.cueId) candidates.push(cue.startMilliseconds, cue.endMilliseconds);
      }
      if (drag.mode === 'move') {
        const cueDuration = drag.cue.endMilliseconds - drag.cue.startMilliseconds;
        start = Math.max(0, Math.min(durationMilliseconds - cueDuration, snapTimelineTime(
          Math.max(0, Math.min(durationMilliseconds - cueDuration, drag.cue.startMilliseconds + deltaMilliseconds)),
          effectivePixelsPerSecond,
          candidates
        )));
        end = start + cueDuration;
      } else if (drag.mode === 'start') {
        start = Math.max(0, Math.min(drag.cue.endMilliseconds - minimumDuration, snapTimelineTime(
          Math.max(0, Math.min(drag.cue.endMilliseconds - minimumDuration, drag.cue.startMilliseconds + deltaMilliseconds)),
          effectivePixelsPerSecond,
          candidates
        )));
      } else {
        end = Math.min(durationMilliseconds, Math.max(drag.cue.startMilliseconds + minimumDuration, snapTimelineTime(
          Math.min(durationMilliseconds, Math.max(drag.cue.startMilliseconds + minimumDuration, drag.cue.endMilliseconds + deltaMilliseconds)),
          effectivePixelsPerSecond,
          candidates
        )));
      }
      setDrag((current) => current ? {
        ...current,
        startMilliseconds: Math.round(start),
        endMilliseconds: Math.round(end),
        moved: current.moved || Math.abs(event.clientX - current.originClientX) >= 2
      } : null);
    };
    const onPointerUp = (event: PointerEvent) => {
      const completed = drag;
      setDrag(null);
      if (completed.captureTarget.hasPointerCapture(completed.pointerId)) {
        completed.captureTarget.releasePointerCapture(completed.pointerId);
      }
      if (event.type === 'pointercancel') return;
      if (!completed.moved) {
        onSelectCue(completed.cue.cueId, completed.cue.startMilliseconds);
        return;
      }
      if (!timelineWindow || busy) return;
      void onUpdateCue({
        trackId: timelineWindow.trackId,
        cueId: completed.cue.cueId,
        expectedTrackRevision: timelineWindow.trackRevision,
        startMilliseconds: completed.startMilliseconds,
        endMilliseconds: completed.endMilliseconds
      });
    };
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp, { once: true });
    window.addEventListener('pointercancel', onPointerUp, { once: true });
    return () => {
      window.removeEventListener('pointermove', onPointerMove);
      window.removeEventListener('pointerup', onPointerUp);
      window.removeEventListener('pointercancel', onPointerUp);
    };
  }, [busy, drag, durationMilliseconds, effectivePixelsPerSecond, onSelectCue, onUpdateCue, playheadMilliseconds, timelineWindow]);

  const rulerTicks = useMemo(() => {
    const first = Math.floor(visibleRange.startMilliseconds / rulerStep) * rulerStep;
    const ticks: number[] = [];
    for (let value = first; value <= visibleRange.endMilliseconds + rulerStep && ticks.length < 300; value += rulerStep) {
      if (value >= 0 && value <= durationMilliseconds) ticks.push(value);
    }
    return ticks;
  }, [durationMilliseconds, rulerStep, visibleRange]);

  const timelineThumbnails = useMemo(() => {
    if (!media || durationMilliseconds <= 0) return [];
    const count = Math.max(1, media.thumbnailCount || 12);
    const existing = new Map(
      (media.timelineThumbnails ?? []).map((thumbnail) => [thumbnail.index, thumbnail])
    );
    return Array.from({ length: count }, (_, index) => {
      const artifact = existing.get(index);
      return {
        index,
        url: artifact?.url ?? null,
        revision: artifact?.revision ?? 0,
        timestampMilliseconds: artifact?.timestampMilliseconds
          ?? durationMilliseconds * (index * 2 + 1) / (count * 2),
        startMilliseconds: artifact?.startMilliseconds
          ?? durationMilliseconds * index / count,
        endMilliseconds: artifact?.endMilliseconds
          ?? durationMilliseconds * (index + 1) / count
      };
    });
  }, [durationMilliseconds, media]);

  useEffect(() => {
    if (!media || durationMilliseconds <= 0 || timelineThumbnails.length === 0) return;
    const count = timelineThumbnails.length;
    const visibleIndices = selectTimelineThumbnailIndices(
      count,
      durationMilliseconds,
      visibleRange.startMilliseconds,
      visibleRange.endMilliseconds
    );
    const missing: number[] = [];
    for (const index of visibleIndices) {
      const item = timelineThumbnails[index];
      const key = thumbnailArtifactKey(media, index);
      const existing = thumbnailLoads[key] ?? initialTimelineMediaLoadState(
        Boolean(item.url),
        item.revision
      );
      if (!item.url && existing.phase === 'missing') missing.push(index);
    }
    if (missing.length > 0) {
      setThumbnailLoads((current) => {
        const next = { ...current };
        for (const index of missing) {
          const item = timelineThumbnails[index];
          const key = thumbnailArtifactKey(media, index);
          next[key] = markTimelineMediaRequested(
            current[key] ?? initialTimelineMediaLoadState(Boolean(item.url), item.revision)
          );
        }
        return next;
      });
      const center = (visibleRange.startMilliseconds + visibleRange.endMilliseconds) / 2;
      onRequestThumbnails(
        media.sha256,
        prioritizeTimelineThumbnailIndices(missing, count, durationMilliseconds, center)
      );
    }
  }, [
    durationMilliseconds,
    media,
    onRequestThumbnails,
    thumbnailLoads,
    timelineThumbnails,
    visibleRange.endMilliseconds,
    visibleRange.startMilliseconds
  ]);

  useEffect(() => {
    if (!media || !media.hasAudio || media.waveformStatus === 'NO_AUDIO') return;
    if (media.waveformStatus === 'PENDING' && waveformLoad === null) {
      setWaveformLoad(markTimelineMediaRequested(initialTimelineMediaLoadState()));
      onRequestWaveform(media.sha256);
    }
  }, [media, onRequestWaveform, waveformLoad]);

  useEffect(() => {
    if (!media) return;
    const activeRetryKeys = new Set<string>();
    const schedule = (
      key: string,
      state: TimelineMediaLoadState,
      request: () => void
    ) => {
      if (state.phase !== 'retry_wait' || mediaRetryTimersRef.current.has(key)) return;
      activeRetryKeys.add(key);
      const timer = window.setTimeout(() => {
        mediaRetryTimersRef.current.delete(key);
        request();
      }, timelineMediaRetryDelay(state.retryCount));
      mediaRetryTimersRef.current.set(key, timer);
    };
    for (const [key, state] of Object.entries(thumbnailLoads)) {
      if (state.phase === 'retry_wait') activeRetryKeys.add(key);
      const index = Number(key.split(':').at(-1));
      if (!Number.isInteger(index)) continue;
      schedule(key, state, () => {
        setThumbnailLoads((current) => ({
          ...current,
          [key]: retryTimelineMedia(current[key] ?? state)
        }));
        onRequestThumbnails(media.sha256, [index]);
      });
    }
    if (waveformLoad) {
      const key = waveformArtifactKey(media);
      if (waveformLoad.phase === 'retry_wait') activeRetryKeys.add(key);
      schedule(key, waveformLoad, () => {
        setWaveformLoad((current) => retryTimelineMedia(current ?? waveformLoad));
        onRequestWaveform(media.sha256);
      });
    }
    for (const [key, timer] of mediaRetryTimersRef.current) {
      if (activeRetryKeys.has(key)) continue;
      window.clearTimeout(timer);
      mediaRetryTimersRef.current.delete(key);
    }
  }, [media, onRequestThumbnails, onRequestWaveform, thumbnailLoads, waveformLoad]);

  const renderedCues = timelineWindow && timelineWindow.trackId === trackId
    ? timelineWindow.cues
    : [];

  return (
    <section className="card vietsub-editor-timeline" aria-label="Timeline dự án Vietsub">
      <div className="vietsub-timeline-toolbar">
        <div><span className="vietsub-eyebrow">TIMELINE</span><strong>{formatTimeline(playheadMilliseconds)}</strong></div>
        <div className="vietsub-timeline-tools">
          {timelineWindow?.truncated && <span className="warning">Cửa sổ có quá nhiều cue · hãy phóng to</span>}
          <button type="button" onClick={() => setAutoFollow((value) => !value)} className={autoFollow ? 'is-active' : ''} title="Tự theo playhead">
            <Focus size={14} /> Theo playhead
          </button>
          <button type="button" onClick={() => setPixelsPerSecond(clampTimelineZoom(effectivePixelsPerSecond / 1.5))} title="Thu nhỏ timeline"><ZoomOut size={15} /></button>
          <input
            type="range"
            min={8}
            max={320}
            value={effectivePixelsPerSecond}
            aria-label="Mức phóng timeline"
            onChange={(event) => setPixelsPerSecond(clampTimelineZoom(Number(event.target.value)))}
          />
          <button type="button" onClick={() => setPixelsPerSecond(clampTimelineZoom(effectivePixelsPerSecond * 1.5))} title="Phóng to timeline"><ZoomIn size={15} /></button>
          <span>{media ? formatTimeline(durationMilliseconds) : 'Chưa có video'}</span>
        </div>
      </div>
      <div className={`vietsub-timeline-canvas ${media ? '' : 'is-empty'}`}>
        <div className="vietsub-timeline-track-labels" aria-hidden="true">
          <span>Thời gian</span>
          <span><Film size={13} /> Video</span>
          <span><Volume2 size={13} /> Voice gốc</span>
          <span><Captions size={13} /> Phụ đề</span>
        </div>
        <div
          className="vietsub-timeline-viewport"
          ref={viewportRef}
          role="slider"
          tabIndex={media ? 0 : -1}
          aria-label="Vị trí phát trên timeline"
          aria-valuemin={0}
          aria-valuemax={durationMilliseconds}
          aria-valuenow={Math.min(playheadMilliseconds, durationMilliseconds)}
          onScroll={scheduleWindowRequest}
          onKeyDown={(event) => {
            if (!media || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
            event.preventDefault();
            const step = event.shiftKey ? 5_000 : 1_000;
            onSeek(Math.max(0, Math.min(
              durationMilliseconds,
              playheadMilliseconds + (event.key === 'ArrowRight' ? step : -step)
            )));
          }}
        >
          <div
            className="vietsub-timeline-content"
            style={{
              width: `${contentWidth}px`,
              '--timeline-grid-size': `${gridStepPixels}px`
            } as CSSProperties}
            onClick={(event) => {
              if (!media || drag) return;
              const bounds = event.currentTarget.getBoundingClientRect();
              onSeek(Math.round(pixelToTime(event.clientX - bounds.left, effectivePixelsPerSecond)));
            }}
          >
            <div className="vietsub-timeline-ruler">
              {rulerTicks.map((tick) => (
                <span style={{ left: `${timeToPixel(tick, effectivePixelsPerSecond)}px` }} key={tick}>
                  <i />{formatRuler(tick)}
                </span>
              ))}
            </div>
            <div className="vietsub-timeline-grid" aria-hidden="true" />
            <div className="vietsub-timeline-video-track">
              {timelineThumbnails.length ? timelineThumbnails.map((thumbnail) => {
                const artifactKey = media
                  ? thumbnailArtifactKey(media, thumbnail.index)
                  : `missing:${thumbnail.index}`;
                const loadState = thumbnailLoads[artifactKey]
                  ?? initialTimelineMediaLoadState(Boolean(thumbnail.url), thumbnail.revision);
                const label = `Frame video tại ${formatTimeline(thumbnail.timestampMilliseconds)}`;
                const canRender = Boolean(thumbnail.url)
                  && ['ready', 'loading', 'loaded'].includes(loadState.phase);
                return (
                  <div
                    className={`vietsub-timeline-thumbnail is-${loadState.phase}`}
                    data-vietsub-thumbnail="true"
                    style={{
                    left: `${timeToPixel(thumbnail.startMilliseconds, effectivePixelsPerSecond)}px`,
                    width: `${Math.max(1, timeToPixel(
                      thumbnail.endMilliseconds - thumbnail.startMilliseconds,
                      effectivePixelsPerSecond
                    ))}px`
                    }}
                    key={artifactKey}
                  >
                    {canRender ? (
                      <img
                        key={`${thumbnail.url}-${thumbnail.revision}-${loadState.epoch}`}
                        src={thumbnail.url!}
                        alt=""
                        aria-label={label}
                        referrerPolicy="no-referrer"
                        loading="lazy"
                        draggable={false}
                        onLoad={() => setThumbnailLoads((current) => ({
                          ...current,
                          [artifactKey]: markTimelineMediaLoaded(
                            current[artifactKey]
                              ?? initialTimelineMediaLoadState(true, thumbnail.revision)
                          )
                        }))}
                        onError={() => setThumbnailLoads((current) => ({
                          ...current,
                          [artifactKey]: markTimelineMediaFailed(
                            current[artifactKey]
                              ?? initialTimelineMediaLoadState(true, thumbnail.revision),
                            current[artifactKey]?.errorCode ?? 'vietsub_media_browser_load_failed'
                          )
                        }))}
                      />
                    ) : (
                      <span
                        className="vietsub-timeline-artifact-placeholder"
                        role="img"
                        aria-label={`${label} ${loadState.phase === 'failed_terminal' ? 'chưa tải được' : 'đang chuẩn bị'}`}
                        title={loadState.errorCode ?? undefined}
                      >
                        {loadState.phase === 'failed_terminal'
                          ? 'Frame chưa tải được'
                          : loadState.phase === 'retry_wait'
                            ? 'Đang thử lại frame…'
                            : 'Đang chuẩn bị frame…'}
                      </span>
                    )}
                  </div>
                );
              }) : <span>{media ? 'Đang chuẩn bị thumbnail…' : 'Nhập video để hiển thị timeline'}</span>}
            </div>
            <div className={`vietsub-timeline-voice-track is-${media?.waveformStatus?.toLowerCase() ?? 'empty'}`}>
              {media?.waveformStatus === 'READY' && media.waveformUrl ? (
                waveformLoad?.phase !== 'retry_wait'
                  && waveformLoad?.phase !== 'failed_terminal' ? (
                  <img
                    key={`${media.waveformUrl}-${media.waveformRevision}-${waveformLoad?.epoch ?? 0}`}
                    src={media.waveformUrl}
                    alt=""
                    aria-label="Dạng sóng âm thanh gốc"
                    referrerPolicy="no-referrer"
                    draggable={false}
                    onLoad={() => setWaveformLoad((current) => markTimelineMediaLoaded(
                      current ?? initialTimelineMediaLoadState()
                    ))}
                    onError={() => setWaveformLoad((current) => markTimelineMediaFailed(
                      current ?? initialTimelineMediaLoadState(true, media.waveformRevision),
                      current?.errorCode ?? 'vietsub_media_browser_load_failed'
                    ))}
                  />
                ) : (
                  <span
                    className="vietsub-timeline-artifact-placeholder is-waveform"
                    role="img"
                    aria-label={waveformLoad.phase === 'failed_terminal' ? 'Waveform tải thất bại' : 'Waveform đang thử lại'}
                    title={waveformLoad.errorCode ?? undefined}
                  >
                    {waveformLoad.phase === 'failed_terminal'
                      ? 'Chưa tải được waveform'
                      : 'Đang thử lại waveform…'}
                  </span>
                )
              ) : media?.waveformStatus === 'NO_AUDIO' ? (
                <span>Video không có âm thanh gốc</span>
              ) : media?.waveformStatus === 'FAILED' ? (
                <span>Chưa thể phân tích âm thanh gốc</span>
              ) : (
                <span>{media ? 'Đang chuẩn bị waveform…' : 'Chưa có âm thanh gốc'}</span>
              )}
            </div>
            <div className="vietsub-timeline-subtitle-track">
              {renderedCues.map((cue) => {
                const draft = drag?.cue.cueId === cue.cueId ? drag : null;
                const start = draft?.startMilliseconds ?? cue.startMilliseconds;
                const end = draft?.endMilliseconds ?? cue.endMilliseconds;
                const active = playheadMilliseconds >= start && playheadMilliseconds < end;
                return (
                  <button
                    type="button"
                    className={`${cue.hasTranslation ? 'translated' : 'pending'} ${active ? 'active' : ''} ${selectedCueId === cue.cueId ? 'selected' : ''} ${cue.hasWarnings ? 'warning' : ''}`}
                    style={{
                      left: `${timeToPixel(start, effectivePixelsPerSecond)}px`,
                      width: `${Math.max(4, timeToPixel(end - start, effectivePixelsPerSecond))}px`
                    }}
                    title={`#${cue.cueIndex + 1} · ${formatTimeline(start)}–${formatTimeline(end)} · ${cue.previewText}`}
                    aria-label={`Cue ${cue.cueIndex + 1}, ${formatTimeline(start)} đến ${formatTimeline(end)}, ${cue.previewText}`}
                    aria-pressed={selectedCueId === cue.cueId}
                    disabled={busy}
                    onClick={(event) => event.stopPropagation()}
                    onPointerDown={(event) => beginCueDrag(event, cue, 'move', setDrag)}
                    onKeyDown={(event) => {
                      if (!['ArrowLeft', 'ArrowRight'].includes(event.key) || !timelineWindow || busy) return;
                      event.preventDefault();
                      const delta = (event.shiftKey ? 1_000 : 100) * (event.key === 'ArrowRight' ? 1 : -1);
                      const cueDuration = cue.endMilliseconds - cue.startMilliseconds;
                      const startMilliseconds = Math.max(0, Math.min(
                        durationMilliseconds - cueDuration,
                        cue.startMilliseconds + delta
                      ));
                      void onUpdateCue({
                        trackId: timelineWindow.trackId,
                        cueId: cue.cueId,
                        expectedTrackRevision: timelineWindow.trackRevision,
                        startMilliseconds,
                        endMilliseconds: startMilliseconds + cueDuration
                      });
                    }}
                    key={cue.cueId}
                  >
                    <i className="resize-start" onPointerDown={(event) => beginCueDrag(event, cue, 'start', setDrag)} />
                    <span className="vietsub-timeline-cue-text">
                      {cue.locked && <LockKeyhole size={10} />}
                      <b>{cue.previewText || `Cue ${cue.cueIndex + 1}`}</b>
                    </span>
                    <i className="resize-end" onPointerDown={(event) => beginCueDrag(event, cue, 'end', setDrag)} />
                  </button>
                );
              })}
            </div>
            {media && (
              <button
                type="button"
                className="vietsub-timeline-playhead"
                style={{ left: `${timeToPixel(playheadMilliseconds, effectivePixelsPerSecond)}px` }}
                aria-label={`Playhead tại ${formatTimeline(playheadMilliseconds)}`}
                onPointerDown={(event) => {
                  event.stopPropagation();
                  const content = event.currentTarget.parentElement;
                  if (!content) return;
                  const move = (moveEvent: PointerEvent) => {
                    const bounds = content.getBoundingClientRect();
                    onSeek(Math.max(0, Math.min(durationMilliseconds, Math.round(pixelToTime(moveEvent.clientX - bounds.left, effectivePixelsPerSecond)))));
                  };
                  const cleanup = () => {
                    window.removeEventListener('pointermove', move);
                    window.removeEventListener('pointerup', cleanup);
                    window.removeEventListener('pointercancel', cleanup);
                    playheadCleanupRef.current = null;
                  };
                  playheadCleanupRef.current?.();
                  playheadCleanupRef.current = cleanup;
                  window.addEventListener('pointermove', move);
                  window.addEventListener('pointerup', cleanup, { once: true });
                  window.addEventListener('pointercancel', cleanup, { once: true });
                }}
              >
                <Play size={10} fill="currentColor" /><i />
              </button>
            )}
          </div>
        </div>
      </div>
    </section>
  );
}

function beginCueDrag(
  event: React.PointerEvent<HTMLElement>,
  cue: VietsubTimelineCue,
  mode: CueDrag['mode'],
  setDrag: (value: CueDrag) => void
) {
  event.preventDefault();
  event.stopPropagation();
  event.currentTarget.setPointerCapture(event.pointerId);
  setDrag({
    cue,
    mode,
    originClientX: event.clientX,
    startMilliseconds: cue.startMilliseconds,
    endMilliseconds: cue.endMilliseconds,
    moved: false,
    captureTarget: event.currentTarget,
    pointerId: event.pointerId
  });
}

function formatTimeline(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.floor(milliseconds / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
    : `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function formatRuler(milliseconds: number): string {
  if (milliseconds < 1_000) return `${milliseconds} ms`;
  return formatTimeline(milliseconds);
}

function thumbnailArtifactKey(media: VietsubMediaSummary, index: number): string {
  return `${media.mediaId}:v${media.thumbnailProfileVersion}:${media.sha256.toLowerCase()}:${index}`;
}

function waveformArtifactKey(media: VietsubMediaSummary): string {
  return `${media.mediaId}:v${media.waveformProfileVersion}:${media.sha256.toLowerCase()}:waveform`;
}
