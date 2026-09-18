import type { VietsubSubtitleMaskSettings, VietsubVideoTransformSettings } from './types';

export const defaultVietsubSubtitleMask: VietsubSubtitleMaskSettings = {
  enabled: false, mode: 'BLUR', x: 0.05, y: 0.78, width: 0.9, height: 0.12,
  color: '#000000', blurPercent: 1.5, opacity: 0.35
};

export const defaultVietsubVideoTransformSettings: VietsubVideoTransformSettings = {
  flipHorizontal: false,
  flipVertical: false,
  subtitleMask: { ...defaultVietsubSubtitleMask }
};

export function cloneVideoTransformSettings(
  settings: VietsubVideoTransformSettings
): VietsubVideoTransformSettings & { subtitleMask: VietsubSubtitleMaskSettings } {
  return { ...settings, subtitleMask: { ...defaultVietsubSubtitleMask, ...settings.subtitleMask,
    opacity: settings.subtitleMask?.opacity ?? (settings.subtitleMask?.mode === 'SOLID' ? 1 : 0.35)
  } };
}

// Flipping a rectangle twice restores its source position; use this for both directions.
export function flipMaskRegion(mask: VietsubSubtitleMaskSettings, transform: VietsubVideoTransformSettings): VietsubSubtitleMaskSettings {
  return {
    ...mask,
    x: transform.flipHorizontal ? Math.max(0, 1 - mask.x - mask.width) : mask.x,
    y: transform.flipVertical ? Math.max(0, 1 - mask.y - mask.height) : mask.y
  };
}

export type MaskDragMode = 'move' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'nw';

export function resizeMaskRegion(mask: VietsubSubtitleMaskSettings, mode: MaskDragMode, dx: number, dy: number): VietsubSubtitleMaskSettings {
  const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value));
  if (mode === 'move') return { ...mask, x: clamp(mask.x + dx, 0, 1 - mask.width), y: clamp(mask.y + dy, 0, 1 - mask.height) };
  let left = mask.x;
  let top = mask.y;
  let right = mask.x + mask.width;
  let bottom = mask.y + mask.height;
  if (mode.includes('w')) left = clamp(left + dx, 0, right - 0.02);
  if (mode.includes('e')) right = clamp(right + dx, left + 0.02, 1);
  if (mode.includes('n')) top = clamp(top + dy, 0, bottom - 0.02);
  if (mode.includes('s')) bottom = clamp(bottom + dy, top + 0.02, 1);
  return { ...mask, x: left, y: top, width: Math.max(0.02, right - left), height: Math.max(0.02, bottom - top) };
}
