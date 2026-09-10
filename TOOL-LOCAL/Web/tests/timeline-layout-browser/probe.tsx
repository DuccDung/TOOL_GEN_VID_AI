import { createRoot } from 'react-dom/client';
import { VietsubTimeline } from '../../src/features/vietsub/VietsubTimeline';
import type { VietsubMediaSummary, VietsubTimelineWindow, VietsubVoiceWorkspace } from '../../src/features/vietsub/types';
import { defaultVietsubAudioMixSettings } from '../../src/features/vietsub/vietsubAudioMix';
import '../../src/styles.css';

const media: VietsubMediaSummary = {
  mediaId: 'fixture', fileName: 'fixture.mp4', importMode: 'COPY', sizeBytes: 1,
  sha256: 'a'.repeat(64), durationSeconds: 9, width: 720, height: 1280,
  hasAudio: false, sourceAvailable: true, sourceChanged: false, rotationDegrees: 0,
  thumbnailUrls: [], timelineThumbnails: [], thumbnailCount: 0,
  thumbnailProfileVersion: 1, waveformProfileVersion: 1, waveformRevision: 1, waveformStatus: 'NO_AUDIO'
};
const timeline: VietsubTimelineWindow = {
  trackId: 'track', trackRevision: 1, windowStartMilliseconds: 0, windowEndMilliseconds: 9000, truncated: false,
  cues: [[0, 1500, false], [2250, 3500, true], [3750, 4000, false], [4250, 6250, true], [8250, 9000, true]]
    .map(([start, end, enabled], index) => ({
      cueId: `cue-${index}`, cueIndex: index, startMilliseconds: start as number, endMilliseconds: end as number,
      voiceEnabled: enabled as boolean, locked: false, hasWarnings: false, hasTranslation: true,
      previewText: `Phụ đề mẫu ${index + 1}`
    }))
};
const voice: VietsubVoiceWorkspace = {
  voices: [], settings: { engineId: 'PIPER_LOCAL', modelId: 'fixture', voiceId: 'fixture',
    maximumPhraseGapMilliseconds: 500, maximumPhraseDurationMilliseconds: 8000, maximumPhraseCharacters: 4500,
    maximumBorrowedGapMilliseconds: 600, preferredMaximumTempo: 1.12, maximumTempo: 1.2, trimSilence: true },
  timeline: { artifactId: 'voice', trackId: 'track', trackRevision: 1, artifactKind: 'TIMELINE', sizeBytes: 1,
    sha256: 'b'.repeat(64), durationMilliseconds: 9000, sampleRate: 48000, channels: 2, status: 'READY',
    updatedAtUtc: new Date(0).toISOString() },
  timelinePlaybackUrl: 'https://app.local/unused.wav', timingDiagnostics: []
};

createRoot(document.getElementById('root')!).render(
  <main style={{ padding: 16, width: '100%', maxWidth: 1160 }}>
    <VietsubTimeline media={media} trackId="track" window={timeline} playheadMilliseconds={6000}
      playing={false} voiceWorkspace={voice} voiceEnabled busy={false} audioMixSettings={defaultVietsubAudioMixSettings}
      canExportVideo exporting={false} onExportVideo={() => {}}
      onSeek={() => {}} onSelectCue={() => {}} onLoadWindow={() => {}}
      onRequestThumbnails={() => {}} onRequestWaveform={() => {}} onUpdateCue={async () => true}
      onToggleVoice={() => {}} onPreviewAudioMix={() => {}} onUpdateAudioMix={async () => true} />
  </main>
);

window.run = async () => {
  await document.fonts.ready;
  await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  const errors: string[] = [];
  const check = (ok: boolean, message: string) => { if (!ok) errors.push(message); };
  const clip = document.querySelector<HTMLElement>('[data-vietsub-voice-cue-id="cue-0"]')!;
  const label = clip.querySelector('span')!;
  const box = label.getBoundingClientRect();
  const card = clip.getBoundingClientRect();
  const range = document.createRange();
  range.selectNodeContents(label);
  const text = range.getBoundingClientRect();
  check(text.bottom <= box.bottom - 1 && text.top >= box.top + 1,
    `Text clipped: glyph y=${text.top}..${text.bottom}, label y=${box.top}..${box.bottom}`);
  check(Math.abs((text.left + text.right - card.left - card.right) / 2) < 1, 'Text is not horizontally centered');
  check(Math.abs((text.top + text.bottom - card.top - card.bottom) / 2) < 2, 'Text is not vertically centered');
  const waveforms = Array.from(document.querySelectorAll<HTMLElement>('[data-vietsub-voice-waveform]'));
  check(waveforms.length === 3, 'Generated clips lost waveforms');
  check(clip.querySelector('[data-vietsub-voice-waveform]') === null, 'Skipped clip contains a waveform');
  for (const wave of waveforms) {
    const bars = Array.from(wave.querySelectorAll('i')).map(bar => bar.getBoundingClientRect());
    const bounds = wave.getBoundingClientRect();
    const heights = bars.map(bar => bar.height);
    check(bounds.height >= 20 && bars.length >= 6, 'Waveform has no usable height');
    check(bars.every(bar => bar.top >= bounds.top && bar.bottom <= bounds.bottom), 'Waveform bars clipped vertically');
    check(bars[1].left > bars[0].right && Math.max(...heights) - Math.min(...heights) > 4,
      'Waveform bars collapsed instead of forming a row with different heights');
  }
  const narrow = document.querySelector<HTMLElement>('[data-vietsub-voice-cue-id="cue-2"]')!;
  check(narrow.scrollHeight <= narrow.clientHeight, 'Narrow label overflows vertically');
  const main = document.querySelector('main')!;
  for (const width of [1160, 820, 640, 420]) {
    main.style.width = `${Math.min(width, window.innerWidth)}px`;
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    const toolbar = document.querySelector('.vietsub-timeline-toolbar')!.getBoundingClientRect();
    const exportButton = document.querySelector<HTMLButtonElement>('.vietsub-timeline-export-trigger')!;
    const button = exportButton.getBoundingClientRect();
    const mixer = document.querySelector('.vietsub-timeline-audio-mixer')!.getBoundingClientRect();
    const tools = document.querySelector('.vietsub-timeline-tools')!.getBoundingClientRect();
    check(!exportButton.disabled && exportButton.textContent === 'Xuất video'
      && getComputedStyle(exportButton.querySelector('span')!).display !== 'none', `${width}: timeline export label is unavailable`);
    check(button.left >= toolbar.left && button.right <= toolbar.right + 1 && button.bottom <= toolbar.bottom + 1,
      `${width}: timeline export is clipped`);
    check(tools.left >= mixer.right - 1 || tools.bottom <= mixer.top + 1 || tools.top >= mixer.bottom - 1,
      `${width}: timeline tools overlap the audio mixer`);
  }
  main.style.width = '100%';
  await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  return { errors, waveformCount: waveforms.length, textTop: text.top, textBottom: text.bottom,
    labelTop: box.top, labelBottom: box.bottom };
};

declare global { interface Window { run: () => Promise<unknown>; } }
