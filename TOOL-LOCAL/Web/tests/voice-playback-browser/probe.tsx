import { useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { useSynchronizedVoice } from '../../src/features/vietsub/useSynchronizedVoice';

declare global {
  interface Window { voiceUrl: string; run: () => Promise<void>; chrome: { webview: { postMessage(value: unknown): void } }; }
}

function Probe({ version }: { version: number }) {
  const video = useRef<HTMLVideoElement>(null);
  const audio = useRef<HTMLAudioElement>(null);
  const [gain, setGain] = useState(1.25);
  const [active, setActive] = useState(false);
  const voice = useSynchronizedVoice(video, audio, window.voiceUrl, window.voiceUrl, gain, setActive);
  return <div data-active={active} data-version={version}>
    <video ref={video} src={window.voiceUrl} preload="auto" />
    <audio ref={audio} src={window.voiceUrl} crossOrigin="anonymous" preload="auto" />
    <button id="play" onClick={() => { if (video.current) { video.current.volume = 0.25; void video.current.play(); } }}>Play</button>
    <button id="mute" onClick={() => setGain(0)}>Mute</button>
    <button id="unmute" onClick={() => setGain(1.25)}>Unmute</button>
    <span id="notice">{voice.notice}</span>
  </div>;
}

const root = createRoot(document.getElementById('root')!);
const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
const check = (value: boolean, message: string) => { if (!value) throw new Error(message); };
const until = async (condition: () => boolean, message: string) => {
  for (let i = 0; i < 100; i++) { if (condition()) return; await wait(30); }
  throw new Error(message);
};
const audible = () => document.querySelector('[data-active="true"]') !== null;
const click = (id: string) => document.getElementById(id)!.click();

window.run = async () => {
  try {
    for (let version = 0; version < 2; version++) {
      root.render(<Probe key={version} version={version} />);
      await until(() => document.querySelector(`[data-version="${version}"]`) !== null, 'mount');
      const video = document.querySelector('video')!;
      const audio = document.querySelector('audio')!;
      await until(() => video.readyState >= 2 && audio.readyState >= 2, 'metadata');
      click('play');
      await until(audible, 'voice silent at open');
      check(!video.paused && !audio.paused, 'not playing');
      click('mute');
      await until(() => audio.paused && !audible(), 'mute failed');
      check(!video.paused && video.volume === 0.25, 'original channel changed');
      click('unmute');
      await until(audible, 'voice silent after unmute');
      video.currentTime = 2;
      await until(() => !video.seeking && Math.abs(audio.currentTime - video.currentTime) < 0.3 && audible(), 'seek drift');
      video.pause();
      await until(() => audio.paused && !audible(), 'pause failed');
      video.currentTime = 0.5;
      click('play');
      await until(audible, 'voice silent after replay');
      check(!document.getElementById('notice')!.textContent, 'playback notice');
      video.pause();
    }
    root.unmount();
    window.chrome.webview.postMessage({ productionHook: true });
  } catch (error) { window.chrome.webview.postMessage({ error: String(error) }); }
};
