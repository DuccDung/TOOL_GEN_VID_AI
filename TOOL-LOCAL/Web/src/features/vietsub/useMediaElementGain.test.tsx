// @vitest-environment jsdom
import { act, createElement, StrictMode, useEffect, useRef } from 'react';
import { createRoot } from 'react-dom/client';
import { expect, it, vi } from 'vitest';
import { useMediaElementGain } from './useMediaElementGain';

it('reuses the audio graph when React replays effects, and closes it on real unmount', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const sourceElements = new WeakSet<HTMLMediaElement>();
  const close = vi.fn(async () => { });
  const createSource = vi.fn((media: HTMLMediaElement) => {
    if (sourceElements.has(media)) throw new DOMException('Already connected', 'InvalidStateError');
    sourceElements.add(media);
    return { connect() { } };
  });
  vi.stubGlobal('AudioContext', class {
    state = 'running'; currentTime = 0; destination = {};
    createMediaElementSource = createSource;
    createGain() { return { connect() { }, gain: { cancelScheduledValues() { }, setTargetAtTime() { } } }; }
    close = close;
  });
  const failures: unknown[] = [];
  function Probe() {
    const audio = useRef<HTMLAudioElement | null>(null);
    const connect = useMediaElementGain(audio, 1.25);
    useEffect(() => { void connect().catch(error => failures.push(error)); }, [connect]);
    return createElement('audio', { ref: audio });
  }
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => root.render(createElement(StrictMode, null, createElement(Probe))));
    expect(createSource).toHaveBeenCalledTimes(1);
    expect(close).not.toHaveBeenCalled();
    expect(failures).toEqual([]);
  } finally {
    await act(async () => root.unmount());
    await new Promise(resolve => setTimeout(resolve, 10));
    vi.unstubAllGlobals();
  }
  expect(close).toHaveBeenCalledTimes(1);
});
