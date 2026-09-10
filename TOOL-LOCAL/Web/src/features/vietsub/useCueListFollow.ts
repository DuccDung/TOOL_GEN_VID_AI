import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { RefObject } from 'react';

const resumeDelay = 2000;
const editingSelector = 'input, textarea, select, [contenteditable="true"], details';

/** The list owns scrolling. Playback never scrolls an ancestor or interrupts an edit. */
export function useCueListFollow(
  listRef: RefObject<HTMLDivElement | null>,
  context: string,
  targetCueId: string | null,
  navigation: string,
  playing: boolean
) {
  // undefined follows playback; null deliberately holds the gap between two cues.
  const [heldCueId, setHeldCueId] = useState<string | null | undefined>(undefined);
  const [resume, setResume] = useState(0);
  const latest = useRef({ targetCueId, playing });
  latest.current = { targetCueId, playing };
  const suspended = useRef(false);
  const dragging = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const animationTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const animating = useRef(false);
  const lastNavigation = useRef(navigation);
  const frame = useRef<number | undefined>(undefined);

  const cancelScroll = () => {
    if (frame.current !== undefined) cancelAnimationFrame(frame.current);
    frame.current = undefined;
    clearTimeout(animationTimer.current);
    if (animating.current) {
      const list = listRef.current;
      list?.scrollTo({ top: list.scrollTop, behavior: 'instant' });
      animating.current = false;
    }
  };
  const editing = () => {
    const list = listRef.current;
    const active = document.activeElement;
    return Boolean(list && ((active instanceof HTMLElement && list.contains(active)
      && active.closest(editingSelector)) || list.querySelector('details[open]')));
  };
  const queueResume = () => {
    clearTimeout(timer.current);
    timer.current = setTimeout(() => {
      if (dragging.current || editing() || !latest.current.playing) return;
      suspended.current = false;
      setHeldCueId(undefined);
      setResume(value => value + 1);
    }, resumeDelay);
  };

  useLayoutEffect(() => {
    const list = listRef.current;
    if (!list) return;
    suspended.current = false;
    setHeldCueId(undefined);
    const hold = () => {
      cancelScroll();
      suspended.current = true;
      setHeldCueId(current => current === undefined ? latest.current.targetCueId : current);
      queueResume();
    };
    const pointerDown = (event: PointerEvent) => {
      if (event.target === list || event.pointerType === 'touch') {
        dragging.current = true;
        hold();
      }
    };
    const pointerUp = () => { if (dragging.current) { dragging.current = false; queueResume(); } };
    const keyDown = (event: KeyboardEvent) => {
      if (event.target instanceof HTMLElement && event.target.closest(editingSelector)) return;
      if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' '].includes(event.key)) hold();
    };
    const focus = (event: Event) => {
      const target = event.target;
      if (!(target instanceof HTMLElement) || !target.closest(editingSelector)) return;
      cancelScroll();
      suspended.current = true;
      setHeldCueId(target.closest<HTMLElement>('[data-cue-id]')?.dataset.cueId ?? latest.current.targetCueId);
    };
    const toggle = (event: Event) => {
      if (event.target instanceof HTMLDetailsElement && event.target.open) focus(event);
      else queueResume();
    };
    list.addEventListener('wheel', hold, { passive: true });
    list.addEventListener('touchstart', hold, { passive: true });
    list.addEventListener('pointerdown', pointerDown);
    list.addEventListener('keydown', keyDown);
    list.addEventListener('focusin', focus);
    list.addEventListener('focusout', queueResume);
    list.addEventListener('toggle', toggle, true);
    window.addEventListener('pointerup', pointerUp);
    window.addEventListener('pointercancel', pointerUp);
    return () => {
      cancelScroll(); clearTimeout(timer.current); dragging.current = false;
      list.removeEventListener('wheel', hold);
      list.removeEventListener('touchstart', hold);
      list.removeEventListener('pointerdown', pointerDown);
      list.removeEventListener('keydown', keyDown);
      list.removeEventListener('focusin', focus);
      list.removeEventListener('focusout', queueResume);
      list.removeEventListener('toggle', toggle, true);
      window.removeEventListener('pointerup', pointerUp);
      window.removeEventListener('pointercancel', pointerUp);
    };
    // Event handlers read the current playhead target through latest, without reattaching each tick.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [context, listRef]);

  useEffect(() => {
    if (playing && suspended.current) queueResume();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [playing]);

  useLayoutEffect(() => {
    const explicit = navigation !== lastNavigation.current;
    lastNavigation.current = navigation;
    cancelScroll();
    if (explicit) {
      suspended.current = false;
      clearTimeout(timer.current);
      setHeldCueId(undefined);
    }
    if (!targetCueId || (!explicit && (suspended.current || editing()))) return;
    frame.current = requestAnimationFrame(() => {
      const list = listRef.current;
      const row = Array.from(list?.querySelectorAll<HTMLElement>('[data-cue-id]') ?? [])
        .find(element => element.dataset.cueId === targetCueId);
      if (!list || !row || (!explicit && suspended.current)) return;
      const viewport = list.getBoundingClientRect();
      const bounds = row.getBoundingClientRect();
      const top = viewport.top + list.clientTop + 8;
      const bottom = viewport.top + list.clientTop + list.clientHeight - 8;
      if (bottom <= top) return;
      let delta = 0;
      if (bounds.height > bottom - top) {
        // An oversized card already spanning the reading area must not bounce between its edges.
        if (explicit || bounds.top > top || bounds.bottom < bottom) delta = bounds.top - top;
      } else if (bounds.top < top) delta = bounds.top - top;
      else if (bounds.bottom > bottom) delta = bounds.bottom - bottom;
      const destination = Math.max(0, Math.min(list.scrollHeight - list.clientHeight, list.scrollTop + delta));
      if (Math.abs(destination - list.scrollTop) < 1) return;
      const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
      list.scrollTo({ top: destination, behavior: reducedMotion ? 'instant' : 'smooth' });
      animating.current = !reducedMotion;
      animationTimer.current = setTimeout(() => { animating.current = false; }, 450);
    });
    return () => { if (frame.current !== undefined) cancelAnimationFrame(frame.current); };
    // Only cue/navigation changes schedule a scroll; revision and clock ticks do not.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [context, targetCueId, navigation, resume]);

  return heldCueId;
}
