export const MIN_TIMELINE_ZOOM = 8;
export const MAX_TIMELINE_ZOOM = 320;

export type TimelineViewportRange = {
  startMilliseconds: number;
  endMilliseconds: number;
};

export function clampTimelineZoom(pixelsPerSecond: number): number {
  if (!Number.isFinite(pixelsPerSecond)) return MIN_TIMELINE_ZOOM;
  return Math.max(MIN_TIMELINE_ZOOM, Math.min(MAX_TIMELINE_ZOOM, pixelsPerSecond));
}

function normalizeTimelineScale(pixelsPerSecond: number): number {
  if (!Number.isFinite(pixelsPerSecond)) return MIN_TIMELINE_ZOOM;
  return Math.max(MIN_TIMELINE_ZOOM, pixelsPerSecond);
}

export function fitTimelineZoom(
  durationMilliseconds: number,
  viewportWidth: number,
  requestedPixelsPerSecond: number
): number {
  const requested = clampTimelineZoom(requestedPixelsPerSecond);
  const duration = Math.max(0, Number.isFinite(durationMilliseconds) ? durationMilliseconds : 0);
  const width = Math.max(1, Number.isFinite(viewportWidth) ? viewportWidth : 1);
  return duration > 0
    ? Math.max(requested, width * 1000 / duration)
    : requested;
}

export function timeToPixel(milliseconds: number, pixelsPerSecond: number): number {
  const safeTime = Number.isFinite(milliseconds) ? Math.max(0, milliseconds) : 0;
  return safeTime * normalizeTimelineScale(pixelsPerSecond) / 1000;
}

export function pixelToTime(pixels: number, pixelsPerSecond: number): number {
  const safePixels = Number.isFinite(pixels) ? Math.max(0, pixels) : 0;
  return safePixels * 1000 / normalizeTimelineScale(pixelsPerSecond);
}

export function timelineContentWidth(
  durationMilliseconds: number,
  pixelsPerSecond: number,
  minimumWidth = 1
): number {
  return Math.max(Math.max(1, minimumWidth), timeToPixel(durationMilliseconds, pixelsPerSecond));
}

export function calculateViewportRange(
  scrollLeft: number,
  viewportWidth: number,
  pixelsPerSecond: number,
  durationMilliseconds: number,
  overscanPixels = 240
): TimelineViewportRange {
  const duration = Math.max(0, Number.isFinite(durationMilliseconds) ? durationMilliseconds : 0);
  const safeScroll = Math.max(0, Number.isFinite(scrollLeft) ? scrollLeft : 0);
  const safeWidth = Math.max(1, Number.isFinite(viewportWidth) ? viewportWidth : 1);
  const safeOverscan = Math.max(0, Number.isFinite(overscanPixels) ? overscanPixels : 0);
  const startMilliseconds = Math.max(0, pixelToTime(safeScroll - safeOverscan, pixelsPerSecond));
  const endMilliseconds = Math.min(
    duration,
    pixelToTime(safeScroll + safeWidth + safeOverscan, pixelsPerSecond)
  );
  return {
    startMilliseconds: Math.floor(startMilliseconds),
    endMilliseconds: Math.max(Math.floor(startMilliseconds) + 1, Math.ceil(endMilliseconds))
  };
}

export function snapTimelineTime(
  milliseconds: number,
  pixelsPerSecond: number,
  candidates: readonly number[],
  thresholdPixels = 8
): number {
  const value = Math.max(0, Number.isFinite(milliseconds) ? milliseconds : 0);
  const thresholdMilliseconds = Math.max(0, thresholdPixels) * 1000
    / normalizeTimelineScale(pixelsPerSecond);
  let nearest = value;
  let distance = thresholdMilliseconds + 1;
  for (const candidate of candidates) {
    if (!Number.isFinite(candidate) || candidate < 0) continue;
    const nextDistance = Math.abs(candidate - value);
    if (nextDistance <= thresholdMilliseconds && nextDistance < distance) {
      nearest = candidate;
      distance = nextDistance;
    }
  }
  return Math.round(nearest);
}

export function rulerStepMilliseconds(pixelsPerSecond: number): number {
  const zoom = normalizeTimelineScale(pixelsPerSecond);
  const candidates = [10, 25, 50, 100, 250, 500, 1_000, 2_000, 5_000, 10_000, 30_000, 60_000, 300_000];
  return candidates.find((step) => step * zoom / 1000 >= 64) ?? candidates[candidates.length - 1];
}
