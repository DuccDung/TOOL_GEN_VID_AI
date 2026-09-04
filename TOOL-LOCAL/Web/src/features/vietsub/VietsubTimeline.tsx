import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Captions,
  Film,
  Focus,
  LockKeyhole,
  Play,
  ZoomIn,
  ZoomOut
} from 'lucide-react';
import type {
  VietsubMediaSummary,
  VietsubTimelineCue,
  VietsubTimelineCueUpdate,
  VietsubTimelineWindow,
  VietsubTimelineWindowQuery
} from './types';
import {
  calculateViewportRange,
  clampTimelineZoom,
  pixelToTime,
  rulerStepMilliseconds,
  snapTimelineTime,
  timeToPixel,
  timelineContentWidth
} from './timelineGeometry';

type VietsubTimelineProps = {
  media?: VietsubMediaSummary | null;
  trackId?: string | null;
  window?: VietsubTimelineWindow | null;
  playheadMilliseconds: number;
  playing: boolean;
  busy: boolean;
  selectedCueId?: string | null;
  onSeek: (milliseconds: number) => void;
  onSelectCue: (cueId: string, milliseconds: number) => void;
  onLoadWindow: (query: VietsubTimelineWindowQuery) => void;
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
  trackId,
  window: timelineWindow,
  playheadMilliseconds,
  playing,
  busy,
  selectedCueId,
  onSeek,
  onSelectCue,
  onLoadWindow,
  onUpdateCue
}: VietsubTimelineProps) {
  const durationMilliseconds = Math.max(0, Math.round((media?.durationSeconds ?? 0) * 1000));
  const [pixelsPerSecond, setPixelsPerSecond] = useState(40);
  const [autoFollow, setAutoFollow] = useState(true);
  const [visibleRange, setVisibleRange] = useState({ startMilliseconds: 0, endMilliseconds: 1 });
  const [drag, setDrag] = useState<CueDrag | null>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const requestTimerRef = useRef<number | null>(null);
  const dragRef = useRef<CueDrag | null>(null);
  const playheadCleanupRef = useRef<(() => void) | null>(null);
  const contentWidth = timelineContentWidth(durationMilliseconds, pixelsPerSecond, 1);

  useEffect(() => {
    dragRef.current = drag;
  }, [drag]);

  useEffect(() => () => {
    const activeDrag = dragRef.current;
    if (activeDrag?.captureTarget.hasPointerCapture(activeDrag.pointerId)) {
      activeDrag.captureTarget.releasePointerCapture(activeDrag.pointerId);
    }
    playheadCleanupRef.current?.();
  }, []);

  const requestVisibleWindow = useCallback(() => {
    const viewport = viewportRef.current;
    if (!viewport || !trackId || durationMilliseconds <= 0) return;
    const range = calculateViewportRange(
      viewport.scrollLeft,
      viewport.clientWidth,
      pixelsPerSecond,
      durationMilliseconds,
      320
    );
    setVisibleRange(range);
    onLoadWindow({
      trackId,
      windowStartMilliseconds: range.startMilliseconds,
      windowEndMilliseconds: range.endMilliseconds,
      maximumCues: 400
    });
  }, [durationMilliseconds, onLoadWindow, pixelsPerSecond, trackId]);

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
    const position = timeToPixel(playheadMilliseconds, pixelsPerSecond);
    const leftBoundary = viewport.scrollLeft + 80;
    const rightBoundary = viewport.scrollLeft + viewport.clientWidth - 80;
    if (position < leftBoundary || position > rightBoundary) {
      viewport.scrollTo({ left: Math.max(0, position - viewport.clientWidth * 0.35), behavior: 'smooth' });
      scheduleWindowRequest();
    }
  }, [autoFollow, durationMilliseconds, pixelsPerSecond, playing, playheadMilliseconds, scheduleWindowRequest]);

  useEffect(() => {
    if (!drag) return;
    const onPointerMove = (event: PointerEvent) => {
      const deltaMilliseconds = (event.clientX - drag.originClientX) * 1000 / pixelsPerSecond;
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
          pixelsPerSecond,
          candidates
        )));
        end = start + cueDuration;
      } else if (drag.mode === 'start') {
        start = Math.max(0, Math.min(drag.cue.endMilliseconds - minimumDuration, snapTimelineTime(
          Math.max(0, Math.min(drag.cue.endMilliseconds - minimumDuration, drag.cue.startMilliseconds + deltaMilliseconds)),
          pixelsPerSecond,
          candidates
        )));
      } else {
        end = Math.min(durationMilliseconds, Math.max(drag.cue.startMilliseconds + minimumDuration, snapTimelineTime(
          Math.min(durationMilliseconds, Math.max(drag.cue.startMilliseconds + minimumDuration, drag.cue.endMilliseconds + deltaMilliseconds)),
          pixelsPerSecond,
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
  }, [busy, drag, durationMilliseconds, onSelectCue, onUpdateCue, pixelsPerSecond, playheadMilliseconds, timelineWindow]);

  const rulerTicks = useMemo(() => {
    const step = rulerStepMilliseconds(pixelsPerSecond);
    const first = Math.floor(visibleRange.startMilliseconds / step) * step;
    const ticks: number[] = [];
    for (let value = first; value <= visibleRange.endMilliseconds + step && ticks.length < 300; value += step) {
      if (value >= 0 && value <= durationMilliseconds) ticks.push(value);
    }
    return ticks;
  }, [durationMilliseconds, pixelsPerSecond, visibleRange]);

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
          <button type="button" onClick={() => setPixelsPerSecond((value) => clampTimelineZoom(value / 1.5))} title="Thu nhỏ timeline"><ZoomOut size={15} /></button>
          <input
            type="range"
            min={8}
            max={320}
            value={pixelsPerSecond}
            aria-label="Mức phóng timeline"
            onChange={(event) => setPixelsPerSecond(clampTimelineZoom(Number(event.target.value)))}
          />
          <button type="button" onClick={() => setPixelsPerSecond((value) => clampTimelineZoom(value * 1.5))} title="Phóng to timeline"><ZoomIn size={15} /></button>
          <span>{media ? formatTimeline(durationMilliseconds) : 'Chưa có video'}</span>
        </div>
      </div>
      <div className={`vietsub-timeline-canvas ${media ? '' : 'is-empty'}`}>
        <div className="vietsub-timeline-track-labels" aria-hidden="true">
          <span>Thời gian</span>
          <span><Film size={13} /> Video</span>
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
            style={{ width: `${contentWidth}px` }}
            onClick={(event) => {
              if (!media || drag) return;
              const bounds = event.currentTarget.getBoundingClientRect();
              onSeek(Math.round(pixelToTime(event.clientX - bounds.left, pixelsPerSecond)));
            }}
          >
            <div className="vietsub-timeline-ruler">
              {rulerTicks.map((tick) => (
                <span style={{ left: `${timeToPixel(tick, pixelsPerSecond)}px` }} key={tick}>
                  <i />{formatRuler(tick)}
                </span>
              ))}
            </div>
            <div className="vietsub-timeline-video-track">
              {media?.thumbnailUrls.length ? media.thumbnailUrls.map((url, index) => (
                <img src={url} alt="" loading="lazy" key={`${url}-${index}`} />
              )) : <span>{media ? 'Đang chuẩn bị thumbnail…' : 'Nhập video để hiển thị timeline'}</span>}
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
                      left: `${timeToPixel(start, pixelsPerSecond)}px`,
                      width: `${Math.max(4, timeToPixel(end - start, pixelsPerSecond))}px`
                    }}
                    title={`#${cue.cueIndex + 1} · ${cue.previewText}`}
                    aria-label={`Cue ${cue.cueIndex + 1}, ${formatTimeline(start)} đến ${formatTimeline(end)}`}
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
                    <span>{cue.locked && <LockKeyhole size={10} />} {cue.cueIndex + 1}</span>
                    <i className="resize-end" onPointerDown={(event) => beginCueDrag(event, cue, 'end', setDrag)} />
                  </button>
                );
              })}
            </div>
            {media && (
              <button
                type="button"
                className="vietsub-timeline-playhead"
                style={{ left: `${timeToPixel(playheadMilliseconds, pixelsPerSecond)}px` }}
                aria-label={`Playhead tại ${formatTimeline(playheadMilliseconds)}`}
                onPointerDown={(event) => {
                  event.stopPropagation();
                  const content = event.currentTarget.parentElement;
                  if (!content) return;
                  const move = (moveEvent: PointerEvent) => {
                    const bounds = content.getBoundingClientRect();
                    onSeek(Math.max(0, Math.min(durationMilliseconds, Math.round(pixelToTime(moveEvent.clientX - bounds.left, pixelsPerSecond)))));
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
