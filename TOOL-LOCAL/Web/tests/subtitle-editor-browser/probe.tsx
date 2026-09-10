import { createRoot } from 'react-dom/client';
import { VietsubSubtitleEditor } from '../../src/features/vietsub/VietsubSubtitleEditor';
import { VietsubNotice } from '../../src/features/vietsub/VietsubNotice';
import { VietsubTranslationModeModal } from '../../src/features/vietsub/VietsubSettingsPanel';
import { TriangleAlert } from 'lucide-react';
import type { VietsubSubtitleCue } from '../../src/features/vietsub/types';
import '../../src/styles.css';

const root = createRoot(document.getElementById('root')!);
const noop = () => {};
const save = async () => true;
const playhead = () => 2500;
const pause = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
const frame = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

(window as any).runCloud = async () => {
  const errors: string[] = []; let calls = 0;
  root.render(<VietsubTranslationModeModal busy={false} cloudAvailability={{ available: true }}
    runtime={{ ready: false, status: 'DISABLED', message: 'Dịch Local chưa được bật.',
      engineId: 'local', engineVersion: '1', sourceLanguages: ['en', 'zh'], supportsSceneContext: true, supportsReviewPass: false }}
    onStartCloud={() => calls++} onStartLocal={noop} onInstallLocal={noop} onDismiss={noop} />);
  await frame(); await pause(250);
  const dialog = document.querySelector<HTMLElement>('.vietsub-translation-mode-modal')!;
  const cloud = dialog.querySelector<HTMLElement>('.is-cloud')!;
  const button = cloud.querySelector<HTMLButtonElement>('button')!;
  if (button.disabled || button.textContent?.trim() !== 'Dịch Cloud') errors.push('Cloud CTA is unavailable');
  if (cloud.querySelector('select,input') || /OpenAI|gpt-|API key/i.test(cloud.textContent ?? '')) errors.push('Cloud exposes model settings');
  if (dialog.scrollWidth > dialog.clientWidth + 2) errors.push('Cloud dialog overflows horizontally');
  button.scrollIntoView({ block: 'nearest' }); await frame();
  const bounds = button.getBoundingClientRect();
  if (bounds.left < 0 || bounds.right > window.innerWidth || bounds.top < 0 || bounds.bottom > window.innerHeight)
    errors.push('Cloud CTA is clipped');
  button.click(); await frame();
  if (calls !== 1) errors.push('Cloud click did not dispatch exactly once');
  return { errors, calls, width: window.innerWidth, height: window.innerHeight };
};
let scrollCalls: { kind: string; target: string }[] = [];
for (const name of ['scrollIntoView', 'scrollTo'] as const) {
  const original = Element.prototype[name];
  Element.prototype[name] = function (...args: any[]) {
    scrollCalls.push({ kind: name, target: this.className });
    return (original as Function).apply(this, args);
  } as typeof original;
}

window.run = async () => {
  await document.fonts.ready;
  const errors: string[] = [];
  const scenarios: unknown[] = [];
  for (const width of [420, 320]) for (const count of [7, 50, 120]) {
    const cues: VietsubSubtitleCue[] = Array.from({ length: Math.min(50, count) }, (_, i) => ({
      cueId: `cue-${i}`, cueIndex: i, startMilliseconds: i * 1500, endMilliseconds: i * 1500 + 1200,
      speaker: 'speaker_1', originalText: `Sample subtitle ${i + 1}.`,
      translatedText: i === 3 ? 'Đây là câu phụ đề dài dùng để kiểm tra vùng biên tập và cách cuộn danh sách. '.repeat(8)
        : `Câu phụ đề mẫu số ${i + 1}.`, voiceEnabled: i % 3 !== 0,
      originalLocked: false, translationLocked: false, warnings: [], updatedAtUtc: new Date(0).toISOString()
    }));
    const workspace = { activeTrackId: 'track', tracks: [{ trackId: 'track', displayName: 'Fixture',
      languageCode: 'en', source: 'PADDLE_OCR_LOCAL', revision: 1, cueCount: count,
      translatedCueCount: count, warningCueCount: 3, updatedAtUtc: new Date(0).toISOString() }] };
    let active = 'cue-0', selected: string | null = null, revision = 1;
    const render = () => root.render(
      <div id="outer" style={{ height: 650, overflow: 'auto', padding: '70px 12px' }}>
        <div className="vietsub-inspector-panel is-compact-active" style={{ width, height: 570 }}>
          <VietsubSubtitleEditor workspace={workspace} page={{ trackId: 'track', trackRevision: revision,
            offset: 0, pageSize: 50, totalCount: count, search: '', status: 'ALL', speaker: '', speakers: ['speaker_1'], cues }}
            busy={false} playing activeCueId={active} selectedCueId={selected} selectedCueIndex={null}
            sourceLanguageCode="en" getPlayheadMilliseconds={playhead} onImportSrt={noop} onActivateTrack={noop}
            onLoadPage={noop} onUpdateCue={save} onSplitCue={noop} onAlignCue={noop} onDuplicateCue={noop}
            onDeleteCue={noop} onExportSrt={noop} canExportVideo onExportVideo={save} canDesignSubtitle onOpenSubtitleDesigner={noop}
            onSelectCue={noop} onSaveStateChange={noop} onUpdateCueVoice={save} />
        </div><div style={{ height: 120 }} />
      </div>);
    root.render(null); await frame(); render(); await frame(); await pause(400);
    const list = document.querySelector<HTMLElement>('.vietsub-cue-list')!;
    const toolbar = document.querySelector<HTMLElement>('.vietsub-subtitle-toolbar')!;
    const exportButton = toolbar.querySelector<HTMLButtonElement>('.vietsub-subtitle-export-trigger')!;
    const toolbarBounds = toolbar.getBoundingClientRect();
    const exportBounds = exportButton.getBoundingClientRect();
    const exportLabel = exportButton.querySelector('span')!;
    const heading = toolbar.querySelector('h3')!.getBoundingClientRect();
    const actions = toolbar.querySelector('.vietsub-subtitle-toolbar-actions')!.getBoundingClientRect();
    if (toolbarBounds.height > 68 || heading.height > 20 || heading.right > actions.left - 3
      || Math.abs((heading.top + heading.bottom - actions.top - actions.bottom) / 2) > 2)
      errors.push(`${width}/${count}: subtitle heading and actions are not compact on one row`);
    if (exportButton.disabled || exportLabel.textContent !== 'Xuất video'
      || getComputedStyle(exportLabel).display === 'none'
      || exportBounds.left < toolbarBounds.left - 1 || exportBounds.right > toolbarBounds.right + 1
      || exportBounds.top < toolbarBounds.top - 1 || exportBounds.bottom > toolbarBounds.bottom + 1)
      errors.push(`${width}/${count}: video export button is unavailable or clipped`);
    const outer = document.getElementById('outer')!;
    outer.scrollTop = 0;
    let mounts = 0;
    const observer = new MutationObserver(records => { for (const record of records) for (const node of record.addedNodes)
      if (node instanceof HTMLElement && node.matches('.vietsub-cue-row')) mounts++; });
    observer.observe(list, { childList: true });
    window.__rowRenders = 0; scrollCalls = [];
    const started = performance.now();
    // Same cue, including selecting the already visible cue; then 1x/2x cue transitions and rapid seeks.
    for (let i = 0; i < 6; i++) { render(); await frame(); }
    selected = active; render(); await frame(); await pause(100);
    const sameCueScrolls = scrollCalls.length;
    // Enabled/disabled voice labels must fit inside the header at narrow widths and high zoom.
    for (const row of Array.from(list.querySelectorAll<HTMLElement>('.vietsub-cue-row')).slice(0, 2)) {
      const summary = row.querySelector<HTMLElement>('.vietsub-cue-summary')!;
      const voice = row.querySelector<HTMLElement>('.vietsub-cue-voice-status')!;
      const preview = row.querySelector<HTMLElement>('.vietsub-cue-preview')!;
      const headerBounds = summary.getBoundingClientRect();
      const voiceBounds = voice.getBoundingClientRect();
      if (headerBounds.top - row.getBoundingClientRect().top > 2)
        errors.push(`${width}/${count}: extra strip above cue header`);
      if (voiceBounds.top < headerBounds.top || voiceBounds.bottom > headerBounds.bottom
        || voiceBounds.left < preview.getBoundingClientRect().right || voiceBounds.right > headerBounds.right)
        errors.push(`${width}/${count}: voice status overlaps preview or overflows header`);
      if (parseFloat(getComputedStyle(voice).fontSize) > parseFloat(getComputedStyle(preview).fontSize))
        errors.push(`${width}/${count}: voice status is larger than subtitle preview`);
    }
    selected = null;
    for (const delay of [180, 90]) for (const cue of ['cue-1', 'cue-2', 'cue-3', 'cue-4', 'cue-0']) {
      active = cue; render(); await pause(delay);
    }
    active = 'cue-6'; render(); await frame(); active = 'cue-2'; render(); await frame();
    active = 'cue-0'; render(); await pause(450);
    // Wheel/scrollbar intent must suspend follow through the next cue boundary.
    list.dispatchEvent(new WheelEvent('wheel', { bubbles: true, deltaY: 400 }));
    list.scrollTop = list.scrollHeight; const manualTop = list.scrollTop;
    const callsBeforeManual = scrollCalls.length;
    active = 'cue-1'; render(); await pause(450);
    const manualDrift = Math.abs(list.scrollTop - manualTop);
    const manualScrollCalls = scrollCalls.length - callsBeforeManual;
    // Typing into an automatically expanded cue must keep the field through playback/revision changes.
    selected = 'cue-0'; active = 'cue-0'; render(); await pause(450);
    const input = document.querySelector<HTMLTextAreaElement>('textarea')!;
    input.focus(); active = 'cue-2'; render(); await frame(); revision++; render(); await frame();
    const focusRetained = document.activeElement === input && input.isConnected;
    const outerScroll = outer.scrollTop;
    const rowRenders = window.__rowRenders;
    observer.disconnect();
    if (sameCueScrolls !== 0) errors.push(`${width}/${count}: ${sameCueScrolls} redundant same-cue scrolls`);
    if (manualScrollCalls !== 0 || manualDrift > 2) errors.push(`${width}/${count}: manual scroll overridden (${manualDrift}px)`);
    if (!focusRetained) errors.push(`${width}/${count}: revision discarded focused editor`);
    if (outerScroll !== 0) errors.push(`${width}/${count}: outer container scrolled (${outerScroll}px)`);
    scenarios.push({ width, count, sameCueScrolls, manualScrollCalls, manualDrift, focusRetained, outerScroll,
      scrollCalls: scrollCalls.length, scrollTargets: [...new Set(scrollCalls.map(item => item.target))],
      rowRenders, rowMounts: mounts, elapsedMs: performance.now() - started });
  }
  return { errors, scenarios };
};

window.runNotices = async () => {
  const errors: string[] = [];
  for (const width of [320, 420]) {
    let eventId = 1;
    const render = () => root.render(<main style={{ width, padding: 12 }}>
      <VietsubNotice eventId={eventId} className="vietsub-editor-recovery" icon={<TriangleAlert size={17} />}
        actions={<button type="button">Thử lại</button>}>
        <strong>Dự án được phục hồi sau lần đóng trước.</strong>
        <p>Hãy kiểm tra video và nội dung gần nhất trước khi tiếp tục. Đây là thông báo nhiều dòng trong panel hẹp.</p>
      </VietsubNotice>
      <VietsubNotice eventId={2} className="vietsub-subtitle-notice">Đã lưu lựa chọn tạo giọng.</VietsubNotice>
    </main>);
    root.render(null); await frame(); render(); await frame();
    window.scrollTo(0, 0);
    const notice = document.querySelector<HTMLElement>('.vietsub-editor-recovery')!;
    const close = notice.querySelector<HTMLButtonElement>('[aria-label="Đóng thông báo"]')!;
    close.focus(); await frame();
    const bounds = notice.getBoundingClientRect();
    const button = close.getBoundingClientRect();
    const content = notice.querySelector<HTMLElement>('.vietsub-notice-content')!.getBoundingClientRect();
    const retry = notice.querySelector<HTMLElement>('.vietsub-notice-actions')!.getBoundingClientRect();
    if (button.right > bounds.right || button.left < content.right || button.bottom > retry.top)
      errors.push(`${width}: close overlaps content/retry or overflows banner`);
    if (getComputedStyle(close).outlineStyle === 'none') errors.push(`${width}: keyboard focus is not visible`);
    close.click(); await frame(); render(); await frame();
    if (document.querySelector('.vietsub-editor-recovery')) errors.push(`${width}: same event reappeared`);
    if (!document.querySelector('.vietsub-subtitle-notice')) errors.push(`${width}: unrelated banner dismissed`);
    eventId++; render(); await frame();
    if (!document.querySelector('.vietsub-editor-recovery')) errors.push(`${width}: new event is hidden`);
  }
  window.scrollTo(0, 0);
  return { errors };
};

declare global { interface Window { run: () => Promise<unknown>; runNotices: () => Promise<unknown>; __rowRenders: number; } }
