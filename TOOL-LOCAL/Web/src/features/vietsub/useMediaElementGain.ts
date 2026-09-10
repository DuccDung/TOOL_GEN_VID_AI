import { useCallback, useEffect, useRef } from 'react';
import type { RefObject } from 'react';
import { hasVoiceSignal } from './vietsubAudioMix';

export function useMediaElementGain(
  mediaRef: RefObject<HTMLMediaElement | null>,
  requestedGain: number,
  onActivity?: (active: boolean) => void
) {
  const contextRef = useRef<AudioContext | null>(null);
  const gainNodeRef = useRef<GainNode | null>(null);
  const connectedElementRef = useRef<HTMLMediaElement | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const cleanupTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const gain = Number.isFinite(requestedGain) ? Math.min(1.5, Math.max(0, requestedGain)) : 0;

  const updateGainNode = useCallback((node: GainNode, context: AudioContext) => {
    node.gain.cancelScheduledValues(context.currentTime);
    node.gain.setTargetAtTime(gain, context.currentTime, 0.025);
  }, [gain]);

  const ensureConnected = useCallback(async () => {
    const media = mediaRef.current;
    if (!media) return;

    if (connectedElementRef.current !== media) {
      if (contextRef.current) {
        void contextRef.current.close().catch(() => { });
      }
      contextRef.current = null;
      gainNodeRef.current = null;
      connectedElementRef.current = null;
      analyserRef.current = null;

      let candidateContext: AudioContext | null = null;
      try {
        const context = new AudioContext();
        candidateContext = context;
        const gainNode = context.createGain();
        gainNode.connect(context.destination);
        const source = context.createMediaElementSource(media);
        source.connect(gainNode);
        media.volume = 1;
        contextRef.current = context;
        gainNodeRef.current = gainNode;
        connectedElementRef.current = media;
        try {
          if (onActivity) {
            const analyser = context.createAnalyser();
            analyser.fftSize = 512;
            gainNode.connect(analyser);
            analyserRef.current = analyser;
          }
        } catch { /* Signal metering must not interrupt voice playback. */ }
      } catch {
        if (candidateContext) void candidateContext.close().catch(() => { });
        media.volume = Math.min(1, gain);
        return;
      }
    }

    const context = contextRef.current;
    const gainNode = gainNodeRef.current;
    if (!context || !gainNode) return;
    updateGainNode(gainNode, context);
    if (context.state !== 'running') {
      let timer: ReturnType<typeof setTimeout> | undefined;
      try {
        await Promise.race([
          context.resume(),
          new Promise<never>((_, reject) => { timer = setTimeout(() => reject(new Error('audio_context_not_running')), 1500); })
        ]);
      } finally { clearTimeout(timer); }
    }
  }, [gain, mediaRef, onActivity, updateGainNode]);

  useEffect(() => {
    if (!onActivity) return;
    const samples = new Float32Array(512);
    const timer = window.setInterval(() => {
      const media = mediaRef.current;
      const analyser = analyserRef.current;
      let active = false;
      if (analyser && media && !media.paused && !media.ended && !media.muted && contextRef.current?.state === 'running') {
        analyser.getFloatTimeDomainData(samples);
        active = hasVoiceSignal(samples);
      }
      onActivity(active);
    }, 50);
    return () => { window.clearInterval(timer); onActivity(false); };
  }, [mediaRef, onActivity]);

  useEffect(() => {
    const media = mediaRef.current;
    const context = contextRef.current;
    const gainNode = gainNodeRef.current;
    if (context && gainNode && connectedElementRef.current === media) {
      updateGainNode(gainNode, context);
    } else if (media) {
      media.volume = Math.min(1, gain);
    }
  }, [gain, mediaRef, updateGainNode]);

  useEffect(() => {
    if (cleanupTimerRef.current !== null) clearTimeout(cleanupTimerRef.current);
    return () => {
      // React can replay effects using the same audio element. Closing its
      // context immediately would permanently detach that element's sound.
      cleanupTimerRef.current = setTimeout(() => {
        const context = contextRef.current;
        contextRef.current = null;
        gainNodeRef.current = null;
        connectedElementRef.current = null;
        analyserRef.current = null;
        if (context) void context.close().catch(() => { });
      }, 0);
    };
  }, []);

  return ensureConnected;
}
