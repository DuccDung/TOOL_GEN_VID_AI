type LayoutSample = { viewportWidth: number; contentWidth: number; pixelRatio: number };

// Check that React has applied the measured viewport and the host's zoom. This
// deliberately does not inspect text alignment, clipping or waveform assertions.
export async function waitForTimelineLayout(
  read: () => LayoutSample | null,
  expectedPixelRatio: number,
  minimumContentWidth: number,
  nextFrame: () => Promise<unknown> = () => new Promise(resolve => requestAnimationFrame(resolve)),
  now: () => number = () => performance.now()
) {
  const started = now();
  let previous = '';
  let stable = 0;
  let last: LayoutSample | null = null;
  while (now() - started < 3000) {
    last = read();
    const ready = last !== null && last.viewportWidth > 0
      && Math.abs(last.contentWidth - Math.max(minimumContentWidth, last.viewportWidth)) < 1
      && Math.abs(last.pixelRatio - expectedPixelRatio) < 0.01;
    const signature = JSON.stringify(last);
    stable = ready ? (signature === previous ? stable + 1 : 1) : 0;
    if (stable >= 3) return last!;
    previous = signature;
    await nextFrame();
  }
  throw new Error(`Timeline layout did not settle: ${JSON.stringify(last)}, expectedPixelRatio=${expectedPixelRatio}`);
}
