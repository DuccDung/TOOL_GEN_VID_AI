import { useCallback, useEffect, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent, RefObject } from 'react';
import type { VietsubSubtitleStyle } from './types';
import { subtitleTextCss } from './vietsubSubtitleStyle';

type VideoContentRect = {
  left: number;
  top: number;
  width: number;
  height: number;
};

export function useVideoContentRect(
  stageRef: RefObject<HTMLElement | null>,
  videoRef: RefObject<HTMLVideoElement | null>,
  fallbackWidth = 16,
  fallbackHeight = 9,
  sizingMode: 'contain' | 'cover' = 'contain'
): VideoContentRect {
  const [rect, setRect] = useState<VideoContentRect>({ left: 0, top: 0, width: 0, height: 0 });

  const measure = useCallback(() => {
    const stage = stageRef.current;
    const video = videoRef.current;
    if (!stage) return;
    const stageWidth = Math.max(0, stage.clientWidth);
    const stageHeight = Math.max(0, stage.clientHeight);
    const sourceWidth = Math.max(1, video?.videoWidth || fallbackWidth);
    const sourceHeight = Math.max(1, video?.videoHeight || fallbackHeight);
    if (stageWidth === 0 || stageHeight === 0) return;
    const scale = sizingMode === 'cover'
      ? Math.max(stageWidth / sourceWidth, stageHeight / sourceHeight)
      : Math.min(stageWidth / sourceWidth, stageHeight / sourceHeight);
    const width = sourceWidth * scale;
    const height = sourceHeight * scale;
    const next = {
      left: (stageWidth - width) / 2,
      top: (stageHeight - height) / 2,
      width,
      height
    };
    setRect((current) => (
      Math.abs(current.left - next.left) < 0.5
      && Math.abs(current.top - next.top) < 0.5
      && Math.abs(current.width - next.width) < 0.5
      && Math.abs(current.height - next.height) < 0.5
        ? current
        : next
    ));
  }, [fallbackHeight, fallbackWidth, sizingMode, stageRef, videoRef]);

  useEffect(() => {
    const stage = stageRef.current;
    const video = videoRef.current;
    measure();
    video?.addEventListener('loadedmetadata', measure);
    video?.addEventListener('resize', measure);
    window.addEventListener('resize', measure);
    const observer = typeof ResizeObserver === 'undefined' || !stage
      ? null
      : new ResizeObserver(measure);
    if (stage) observer?.observe(stage);
    return () => {
      video?.removeEventListener('loadedmetadata', measure);
      video?.removeEventListener('resize', measure);
      window.removeEventListener('resize', measure);
      observer?.disconnect();
    };
  }, [measure, stageRef, videoRef]);

  return rect;
}

export function VietsubSubtitleOverlay({
  text,
  style,
  contentRect,
  className = '',
  interactive = false,
  interactionScale = 1,
  onPositionChange
}: {
  text: string;
  style: VietsubSubtitleStyle;
  contentRect: VideoContentRect;
  className?: string;
  interactive?: boolean;
  interactionScale?: number;
  onPositionChange?: (xPercent: number, yPercent: number) => void;
}) {
  const dragRef = useRef<{
    pointerId: number;
    startClientX: number;
    startClientY: number;
    startX: number;
    startY: number;
  } | null>(null);
  const horizontalAnchor = style.alignment === 'BOTTOM_LEFT'
    ? 0
    : style.alignment === 'BOTTOM_RIGHT'
      ? -100
      : -50;
  const verticalAnchor = style.verticalPosition === 'TOP'
    ? 0
    : style.verticalPosition === 'BOTTOM'
      ? -100
      : -50;
  const textAlign = style.alignment === 'BOTTOM_LEFT'
    ? 'left'
    : style.alignment === 'BOTTOM_RIGHT'
      ? 'right'
      : 'center';

  const startDrag = (event: ReactPointerEvent<HTMLSpanElement>) => {
    if (!interactive || !onPositionChange) return;
    event.preventDefault();
    event.stopPropagation();
    event.currentTarget.setPointerCapture(event.pointerId);
    dragRef.current = {
      pointerId: event.pointerId,
      startClientX: event.clientX,
      startClientY: event.clientY,
      startX: style.positionXPercent,
      startY: style.positionYPercent
    };
  };

  const moveDrag = (event: ReactPointerEvent<HTMLSpanElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== event.pointerId || !onPositionChange) return;
    event.preventDefault();
    const safeScale = Math.max(0.1, interactionScale);
    const x = drag.startX + ((event.clientX - drag.startClientX) / (contentRect.width * safeScale) * 100);
    const y = drag.startY + ((event.clientY - drag.startClientY) / (contentRect.height * safeScale) * 100);
    onPositionChange(clampPosition(x), clampPosition(y));
  };

  const stopDrag = (event: ReactPointerEvent<HTMLSpanElement>) => {
    if (dragRef.current?.pointerId !== event.pointerId) return;
    dragRef.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const moveWithKeyboard = (event: ReactKeyboardEvent<HTMLSpanElement>) => {
    if (!interactive || !onPositionChange || !event.key.startsWith('Arrow')) return;
    event.preventDefault();
    event.stopPropagation();
    const step = event.shiftKey ? 2 : 0.5;
    const horizontal = event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0;
    const vertical = event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0;
    onPositionChange(
      clampPosition(style.positionXPercent + horizontal),
      clampPosition(style.positionYPercent + vertical)
    );
  };

  if (!text.trim() || contentRect.width <= 0 || contentRect.height <= 0) return null;

  return (
    <div
      className={`vietsub-subtitle-render-layer ${className}`.trim()}
      aria-live="off"
      style={{
        left: `${contentRect.left}px`,
        top: `${contentRect.top}px`,
        width: `${contentRect.width}px`,
        height: `${contentRect.height}px`
      }}
    >
      <div
        className="vietsub-subtitle-positioner"
        style={{
          left: `${style.positionXPercent}%`,
          top: `${style.positionYPercent}%`,
          width: `${style.maxWidthPercent}%`,
          textAlign,
          transform: `translate(${horizontalAnchor}%, ${verticalAnchor}%)`
        }}
      >
        <span
          className={`vietsub-subtitle-render-text ${interactive ? 'is-interactive' : ''}`}
          role={interactive ? 'button' : undefined}
          tabIndex={interactive ? 0 : undefined}
          aria-label={interactive ? 'Kéo để đặt vị trí phụ đề; dùng phím mũi tên để tinh chỉnh' : undefined}
          aria-valuetext={interactive ? `X ${formatPosition(style.positionXPercent)}%, Y ${formatPosition(style.positionYPercent)}%` : undefined}
          style={{
            ...subtitleTextCss(style, contentRect.height),
            maxWidth: '100%',
            padding: `${Math.max(2, contentRect.height * 0.006)}px ${Math.max(4, contentRect.height * 0.012)}px`,
            textAlign
          }}
          onPointerDown={startDrag}
          onPointerMove={moveDrag}
          onPointerUp={stopDrag}
          onPointerCancel={stopDrag}
          onKeyDown={moveWithKeyboard}
        >
          {text}
        </span>
      </div>
    </div>
  );
}

function clampPosition(value: number): number {
  return Math.round(Math.min(98, Math.max(2, value)) * 10) / 10;
}

function formatPosition(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}
