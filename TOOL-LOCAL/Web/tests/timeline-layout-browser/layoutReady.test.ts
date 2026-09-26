import { describe, expect, it } from 'vitest';
import { waitForTimelineLayout } from './layoutReady';

describe('native timeline layout readiness', () => {
  it('waits beyond two frames when React still holds its initial timeline scale', async () => {
    let frames = 0;
    const result = await waitForTimelineLayout(
      () => ({ viewportWidth: 1024, contentWidth: frames < 4 ? 360 : 1024, pixelRatio: 1 }),
      1, 360, async () => { frames++; }, () => frames * 16);
    expect(frames).toBeGreaterThanOrEqual(6);
    expect(result.contentWidth).toBe(1024);
  });

  it('waits for the requested host zoom even when the previous layout is stable', async () => {
    let frames = 0;
    const result = await waitForTimelineLayout(
      () => frames < 3
        ? { viewportWidth: 1024, contentWidth: 1024, pixelRatio: 1 }
        : { viewportWidth: 819, contentWidth: 819, pixelRatio: 1.25 },
      1.25, 360, async () => { frames++; }, () => frames * 16);
    expect(frames).toBeGreaterThanOrEqual(5);
    expect(result.pixelRatio).toBe(1.25);
  });

  it('fails with measurements if the resize is never applied', async () => {
    let elapsed = 0;
    await expect(waitForTimelineLayout(
      () => ({ viewportWidth: 1024, contentWidth: 360, pixelRatio: 1 }),
      1, 360, async () => { elapsed += 1000; }, () => elapsed
    )).rejects.toThrow('"viewportWidth":1024,"contentWidth":360');
  });
});
