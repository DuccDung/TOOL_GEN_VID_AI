import { useCallback, useEffect, useRef, useState } from 'react';
import type { RefObject } from 'react';
import { useMediaElementGain } from './useMediaElementGain';

// The video is the clock. All voice starts, including late loads and retries,
// read the current elements and settings rather than an earlier play request.
export function useSynchronizedVoice(
  videoRef: RefObject<HTMLVideoElement | null>,
  audioRef: RefObject<HTMLAudioElement | null>,
  videoUrl: string | null | undefined,
  voiceUrl: string | null | undefined,
  gain: number,
  onActivity: (active: boolean) => void
) {
  const ensureGain = useMediaElementGain(audioRef, gain, onActivity);
  const [noticeEvent, setNoticeEvent] = useState({ text: null as string | null, id: 0 });
  const setNotice = useCallback((text: string | null, newEvent = false) => {
    setNoticeEvent(current => !newEvent && current.text === text ? current : { text, id: current.id + 1 });
  }, []);
  const latest = useRef({ gain, ensureGain, voiceUrl });
  latest.current = { gain, ensureGain, voiceUrl };
  const epoch = useRef(0);
  const waiting = useRef(false);
  const pending = useRef<HTMLAudioElement | null>(null);

  const stop = useCallback(() => {
    epoch.current++;
    pending.current = null;
    audioRef.current?.pause();
    onActivity(false);
  }, [audioRef, onActivity]);

  const sync = useCallback((force = false) => {
    const video = videoRef.current;
    const audio = audioRef.current;
    if (!audio || !video) return;
    audio.muted = latest.current.gain <= 0;
    audio.playbackRate = video.playbackRate;
    const position = Number.isFinite(video.currentTime) ? Math.max(0, video.currentTime) : 0;
    const outsideVoice = Number.isFinite(audio.duration) && position >= audio.duration;
    if (Math.abs(audio.currentTime - position) > (force ? 0.01 : 0.25)) {
      try { audio.currentTime = outsideVoice ? audio.duration : position; } catch { /* Retry after metadata. */ }
    }
    if (!latest.current.voiceUrl || audio.muted || video.paused || video.ended || video.seeking || waiting.current || outsideVoice) {
      stop();
      return;
    }
    if (audio.error) return; // A failed resource needs an explicit reload, not a play loop.
    if (pending.current === audio) return;
    const requestEpoch = epoch.current;
    const current = () => requestEpoch === epoch.current && audioRef.current === audio && !video.paused
      && !video.ended && latest.current.gain > 0;
    // resume() may wait for user activation. Never put video.play() behind it.
    void latest.current.ensureGain().catch(() => {
      if (current()) setNotice('Bộ âm thanh chưa khởi động được. Bấm Thử lại giọng.');
    });
    if (!audio.paused && !audio.ended) return;
    pending.current = audio;
    void audio.play().then(() => {
      if (current()) setNotice(null);
    }).catch(error => {
      if (current() && error?.name !== 'AbortError') {
        setNotice('Chưa phát được giọng đã tạo. Bấm Thử lại giọng.');
      }
    }).finally(() => {
      if (requestEpoch === epoch.current && pending.current === audio) pending.current = null;
    });
  }, [audioRef, stop, videoRef]);

  useEffect(() => {
    const video = videoRef.current;
    const audio = audioRef.current;
    waiting.current = false;
    setNotice(null);
    if (!video || !audio || !voiceUrl) return;
    const ready = () => sync(true);
    const tick = () => sync();
    const stall = () => { waiting.current = true; stop(); };
    const play = () => { waiting.current = false; sync(true); };
    const failed = () => { stop(); setNotice('Không tải được giọng đã tạo. Bấm Thử lại giọng.', true); };
    const videoEvents = { play, playing: play, canplay: play, waiting: stall,
      pause: stop, ended: stop, emptied: stop, error: stop, seeking: stop, seeked: play, timeupdate: tick, ratechange: ready };
    const audioEvents = { loadedmetadata: ready, canplay: ready, error: failed };
    for (const [name, listener] of Object.entries(videoEvents)) video.addEventListener(name, listener);
    for (const [name, listener] of Object.entries(audioEvents)) audio.addEventListener(name, listener);
    sync(true);
    return () => {
      for (const [name, listener] of Object.entries(videoEvents)) video.removeEventListener(name, listener);
      for (const [name, listener] of Object.entries(audioEvents)) audio.removeEventListener(name, listener);
      stop();
      audio.pause();
    };
  }, [audioRef, videoRef, videoUrl, voiceUrl, stop, sync]);

  useEffect(() => { sync(); }, [gain, sync]);

  const retry = useCallback(() => {
    stop();
    setNotice(null);
    audioRef.current?.load();
    sync(true);
  }, [audioRef, stop, sync]);

  return { sync, stop, notice: noticeEvent.text, noticeId: noticeEvent.id, retry };
}
