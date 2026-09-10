// @vitest-environment jsdom
import { act, useRef } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useCueListFollow } from './useCueListFollow';

function Harness({ cue = 'one', navigation = '', playing = true, context = 'track' }) {
  const ref = useRef<HTMLDivElement>(null);
  const held = useCueListFollow(ref, context, cue, navigation, playing);
  return <div ref={ref} data-held={held} tabIndex={0}>
    {['one', 'two', 'three'].map(id => <article data-cue-id={id} key={id}><textarea aria-label={id} /><details><summary>Menu</summary></details></article>)}
  </div>;
}

describe('subtitle list follow', () => {
  let container: HTMLDivElement;
  let root: ReturnType<typeof createRoot>;
  let list: HTMLDivElement;
  let scroll: ReturnType<typeof vi.fn>;
  const render = async (props: Parameters<typeof Harness>[0] = {}) => { await act(async () => root.render(<Harness {...props} />)); };
  const advance = async (ms = 20) => { await act(async () => vi.advanceTimersByTime(ms)); };
  beforeEach(async () => {
    vi.useFakeTimers(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => setTimeout(() => callback(Date.now()), 16));
    vi.stubGlobal('cancelAnimationFrame', clearTimeout);
    vi.stubGlobal('matchMedia', () => ({ matches: false }));
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
    await render(); list = container.firstElementChild as HTMLDivElement;
    Object.defineProperties(list, { clientHeight: { value: 300 }, scrollHeight: { value: 1300 } });
    list.getBoundingClientRect = () => ({ top: 0, bottom: 300, height: 300 } as DOMRect);
    scroll = vi.fn(options => { list.scrollTop = options.top; });
    Object.defineProperty(list, 'scrollTo', { configurable: true, value: scroll });
    list.querySelectorAll<HTMLElement>('article').forEach((row, index) => {
      row.getBoundingClientRect = () => ({ top: [8, 108, 808][index] - list.scrollTop,
        bottom: [88, 188, 888][index] - list.scrollTop, height: 80 } as DOMRect);
    });
    await advance(); scroll.mockClear();
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); });

  it('scrolls only when the latest cue is outside the reading area', async () => {
    await render(); await advance(); await render({ cue: 'two' }); await advance(); expect(scroll).not.toHaveBeenCalled();
    await render({ cue: 'three' }); await advance(); expect(scroll).toHaveBeenCalledTimes(1);
    expect(scroll).toHaveBeenLastCalledWith({ top: 596, behavior: 'smooth' });
    await render({ cue: 'three' }); await advance(); expect(scroll).toHaveBeenCalledTimes(1);
  });
  it('discards an obsolete scheduled destination during rapid seeks', async () => {
    await render({ cue: 'three' }); await render({ cue: 'two' }); await advance();
    expect(scroll).not.toHaveBeenCalled();
  });
  it('respects wheel scrolling then resumes after two idle seconds', async () => {
    await act(async () => list.dispatchEvent(new WheelEvent('wheel', { bubbles: true })));
    await render({ cue: 'three' }); await advance(1999); expect(scroll).not.toHaveBeenCalled();
    await advance(1); await advance(); expect(scroll).toHaveBeenCalledTimes(1);
  });
  it('keeps a focused edit/menu pinned and resumes after leaving it', async () => {
    const field = list.querySelector('textarea')!;
    await act(async () => field.focus());
    await render({ cue: 'three' }); await advance(3000);
    expect(list.dataset.held).toBe('one'); expect(scroll).not.toHaveBeenCalled();
    await act(async () => field.blur()); await advance(2000); await advance();
    expect(scroll).toHaveBeenCalledTimes(1);
  });
  it('does not resume while paused and accepts explicit navigation during a hold', async () => {
    await act(async () => list.dispatchEvent(new WheelEvent('wheel')));
    await render({ cue: 'three', playing: false }); await advance(3000); expect(scroll).not.toHaveBeenCalled();
    await render({ cue: 'three', playing: false, navigation: 'selected-three' }); await advance();
    expect(scroll).toHaveBeenCalledTimes(1);
  });
  it('honors reduced motion and keyboard scroll intent', async () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }));
    await act(async () => list.dispatchEvent(new KeyboardEvent('keydown', { key: 'PageDown', bubbles: true })));
    await render({ cue: 'three' }); await advance(500); expect(scroll).not.toHaveBeenCalled();
    await render({ cue: 'three', navigation: 'explicit' }); await advance();
    expect(scroll).toHaveBeenLastCalledWith({ top: 596, behavior: 'instant' });
  });
  it('waits until a scrollbar drag ends before resuming', async () => {
    await act(async () => list.dispatchEvent(new Event('pointerdown', { bubbles: true })));
    await render({ cue: 'three' }); await advance(3000); expect(scroll).not.toHaveBeenCalled();
    await act(async () => window.dispatchEvent(new Event('pointerup')));
    await advance(2000); await advance(); expect(scroll).toHaveBeenCalledTimes(1);
  });
  it('does not bounce between the edges of a card taller than the viewport', async () => {
    const row = list.querySelector<HTMLElement>('[data-cue-id="three"]')!;
    row.getBoundingClientRect = () => ({ top: -30, bottom: 800, height: 830 } as DOMRect);
    await render({ cue: 'three' }); await advance(); expect(scroll).not.toHaveBeenCalled();
  });
  it('cancels old holds and navigates after the page context changes', async () => {
    await act(async () => list.dispatchEvent(new WheelEvent('wheel')));
    await render({ cue: 'three', context: 'next-page' }); await advance();
    expect(scroll).toHaveBeenCalledTimes(1);
  });
});
