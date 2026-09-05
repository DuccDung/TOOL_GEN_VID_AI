import { describe, expect, it } from 'vitest';
import {
  MAX_TIMELINE_ZOOM,
  MIN_TIMELINE_ZOOM,
  calculateViewportRange,
  clampTimelineZoom,
  fitTimelineZoom,
  pixelToTime,
  rulerStepMilliseconds,
  snapTimelineTime,
  timeToPixel,
  timelineContentWidth
} from './timelineGeometry';

describe('timelineGeometry', () => {
  it('converts milliseconds and pixels without losing the source unit', () => {
    expect(timeToPixel(12_345, 100)).toBe(1234.5);
    expect(pixelToTime(1234.5, 100)).toBe(12_345);
  });

  it('clamps invalid and extreme zoom values', () => {
    expect(clampTimelineZoom(Number.NaN)).toBe(MIN_TIMELINE_ZOOM);
    expect(clampTimelineZoom(1)).toBe(MIN_TIMELINE_ZOOM);
    expect(clampTimelineZoom(10_000)).toBe(MAX_TIMELINE_ZOOM);
  });

  it('keeps a multi-hour viewport bounded by the media duration', () => {
    const duration = 6 * 60 * 60 * 1000;
    const range = calculateViewportRange(40_000, 1_200, 25, duration, 300);
    expect(range.startMilliseconds).toBeGreaterThanOrEqual(0);
    expect(range.endMilliseconds).toBeLessThanOrEqual(duration);
    expect(range.endMilliseconds).toBeGreaterThan(range.startMilliseconds);
    expect(timelineContentWidth(duration, 25, 1_200)).toBe(540_000);
  });

  it('handles zero width and short video edges safely', () => {
    expect(calculateViewportRange(-100, 0, 0, 800, 0)).toEqual({
      startMilliseconds: 0,
      endMilliseconds: 125
    });
  });

  it('stretches a short timeline to the full viewport without changing media time', () => {
    const scale = fitTimelineZoom(8_000, 1_200, 40);
    expect(scale).toBe(150);
    expect(timelineContentWidth(8_000, scale, 1_200)).toBe(1_200);
    expect(pixelToTime(1_200, scale)).toBe(8_000);
  });

  it('keeps fractional CSS pixels stable on scaled displays', () => {
    const pixel = timeToPixel(123_456, 37.5);
    expect(pixelToTime(pixel, 37.5)).toBeCloseTo(123_456, 8);
    expect(calculateViewportRange(10.25, 799.5, 37.5, 60_000, 80.5)).toEqual({
      startMilliseconds: 0,
      endMilliseconds: 23_740
    });
  });

  it('snaps only when a candidate is within the pixel threshold', () => {
    expect(snapTimelineTime(1_940, 100, [0, 2_000, 4_000], 8)).toBe(2_000);
    expect(snapTimelineTime(1_800, 100, [2_000], 8)).toBe(1_800);
  });

  it('chooses ruler steps that remain readable across zoom levels', () => {
    expect(rulerStepMilliseconds(320)).toBe(250);
    expect(rulerStepMilliseconds(8)).toBe(10_000);
  });
});
