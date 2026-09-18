import { useRef } from 'react';
import type { KeyboardEvent, PointerEvent } from 'react';
import type { VietsubSubtitleMaskSettings, VietsubVideoTransformSettings } from './types';
import { flipMaskRegion, resizeMaskRegion } from './vietsubVideoTransform';
import type { MaskDragMode } from './vietsubVideoTransform';

const handles: { mode: MaskDragMode; label: string }[] = [
  { mode: 'nw', label: 'góc trên trái' }, { mode: 'n', label: 'cạnh trên' },
  { mode: 'ne', label: 'góc trên phải' }, { mode: 'e', label: 'cạnh phải' },
  { mode: 'se', label: 'góc dưới phải' }, { mode: 's', label: 'cạnh dưới' },
  { mode: 'sw', label: 'góc dưới trái' }, { mode: 'w', label: 'cạnh trái' }
];

export function VietsubSubtitleMaskOverlay({ mask, transform, contentRect, sourceHeight,
  interactive = false, interactionScale = 1, onChange }: {
  mask: VietsubSubtitleMaskSettings;
  transform: VietsubVideoTransformSettings;
  contentRect: { left: number; top: number; width: number; height: number };
  sourceHeight: number;
  interactive?: boolean;
  interactionScale?: number;
  onChange?: (mask: VietsubSubtitleMaskSettings) => void;
}) {
  const drag = useRef<{ id: number; x: number; y: number; origin: VietsubSubtitleMaskSettings; mode: MaskDragMode } | null>(null);
  const box = useRef<HTMLDivElement>(null);
  if (!mask.enabled || contentRect.width <= 0 || contentRect.height <= 0) return null;
  const displayed = flipMaskRegion(mask, transform);
  const position = {
    left: contentRect.left + displayed.x * contentRect.width,
    top: contentRect.top + displayed.y * contentRect.height,
    width: displayed.width * contentRect.width,
    height: displayed.height * contentRect.height
  };
  const update = (next: VietsubSubtitleMaskSettings) => onChange?.(flipMaskRegion(next, transform));
  const start = (event: PointerEvent, mode: MaskDragMode) => {
    if (!interactive || event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    box.current?.focus();
    box.current?.setPointerCapture(event.pointerId);
    drag.current = { id: event.pointerId, x: event.clientX, y: event.clientY, origin: displayed, mode };
  };
  const stop = (event: PointerEvent) => {
    if (drag.current?.id !== event.pointerId) return;
    drag.current = null;
    if (box.current?.hasPointerCapture(event.pointerId)) box.current.releasePointerCapture(event.pointerId);
  };
  const keyboard = (event: KeyboardEvent, mode: MaskDragMode) => {
    if (!event.key.startsWith('Arrow')) return;
    event.preventDefault();
    event.stopPropagation();
    const step = event.shiftKey ? 0.02 : 0.005;
    update(resizeMaskRegion(displayed, mode,
      event.key === 'ArrowLeft' ? -step : event.key === 'ArrowRight' ? step : 0,
      event.key === 'ArrowUp' ? -step : event.key === 'ArrowDown' ? step : 0));
  };
  const sigma = Math.max(0.5, Math.min(512, sourceHeight * mask.blurPercent / 100));
  return <>
    <div className="vietsub-subtitle-mask-effect" aria-hidden="true" style={{ ...position,
      backgroundColor: mask.mode === 'SOLID' ? mask.color : undefined,
      opacity: mask.mode === 'SOLID' ? (mask.opacity ?? 1) : undefined,
      backdropFilter: mask.mode === 'BLUR' ? `blur(${sigma * contentRect.height / Math.max(1, sourceHeight)}px)` : undefined
    }} />
    {interactive && <div ref={box} className="vietsub-subtitle-mask-selection" style={position}
      role="group" tabIndex={0} aria-label="Vùng che phụ đề gốc; dùng phím mũi tên để di chuyển"
      onPointerDown={event => start(event, 'move')}
      onPointerMove={event => {
        const active = drag.current;
        if (!active || active.id !== event.pointerId) return;
        event.preventDefault();
        update(resizeMaskRegion(active.origin, active.mode,
          (event.clientX - active.x) / (contentRect.width * Math.max(0.1, interactionScale)),
          (event.clientY - active.y) / (contentRect.height * Math.max(0.1, interactionScale))));
      }}
      onPointerUp={stop} onPointerCancel={stop} onLostPointerCapture={() => { drag.current = null; }}
      onKeyDown={event => keyboard(event, 'move')}>
      <span className="vietsub-subtitle-mask-label">Che sub gốc</span>
      {handles.map(({ mode, label }) => <button key={mode} type="button"
        className={`vietsub-subtitle-mask-handle is-${mode}`} aria-label={`Chỉnh vùng che: ${label}`}
        onPointerDown={event => start(event, mode)} onKeyDown={event => keyboard(event, mode)} />)}
    </div>}
  </>;
}
