// @vitest-environment jsdom
import { act, createElement, createRef, type ComponentProps } from 'react';
import { createRoot } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest';
import { VietsubSubtitleEditor, type VietsubSubtitleEditorHandle } from './VietsubSubtitleEditor';
import type { VietsubSubtitleCue, VietsubSubtitlePage, VietsubSubtitleWorkspace } from './types';

const noOp = () => { };

const cues: VietsubSubtitleCue[] = [
  {
    cueId: 'cue-1',
    cueIndex: 0,
    startMilliseconds: 0,
    endMilliseconds: 1_850,
    speaker: 'speaker_1',
    originalText: '你好，欢迎光临。',
    translatedText: 'Xin chào, chào mừng bạn.',
    originalLocked: false,
    translationLocked: false,
    warnings: [],
    updatedAtUtc: new Date(0).toISOString()
  },
  {
    cueId: 'cue-2',
    cueIndex: 1,
    startMilliseconds: 2_000,
    endMilliseconds: 3_600,
    speaker: 'speaker_1',
    originalText: '请坐。',
    translatedText: '',
    originalLocked: false,
    translationLocked: false,
    warnings: ['TRANSLATION_MISSING'],
    updatedAtUtc: new Date(0).toISOString()
  }
];

const workspace: VietsubSubtitleWorkspace = {
  activeTrackId: 'track-1',
  tracks: [{
    trackId: 'track-1',
    displayName: 'OCR ZH 2026-09-08 04:27',
    languageCode: 'zh',
    source: 'PADDLE_OCR_LOCAL',
    revision: 7,
    cueCount: 7,
    translatedCueCount: 4,
    warningCueCount: 1,
    updatedAtUtc: new Date(0).toISOString()
  }]
};

function editorElement(overrides: Partial<ComponentProps<typeof VietsubSubtitleEditor>> = {}) {
  const page: VietsubSubtitlePage = overrides.page ?? {
    trackId: 'track-1',
    trackRevision: 7,
    offset: 0,
    pageSize: 50,
    totalCount: 7,
    search: '',
    status: 'ALL',
    speaker: '',
    speakers: ['speaker_1'],
    cues
  };

  return createElement(VietsubSubtitleEditor, {
    workspace,
    page,
    busy: false,
    onUpdateCueVoice: overrides.onUpdateCueVoice,
    voiceSelectionBusy: overrides.voiceSelectionBusy,
    sourceLanguageCode: 'zh',
    activeCueId: overrides.activeCueId ?? null,
    selectedCueId: overrides.selectedCueId ?? null,
    selectedCueIndex: overrides.selectedCueIndex ?? null,
    getPlayheadMilliseconds: () => 2_500,
    onImportSrt: noOp,
    onActivateTrack: noOp,
    onLoadPage: noOp,
    onUpdateCue: async () => true,
    onSplitCue: noOp,
    onAlignCue: noOp,
    onDuplicateCue: noOp,
    onDeleteCue: noOp,
    onExportSrt: noOp,
    canExportVideo: true,
    onExportVideo: async () => true,
    canDesignSubtitle: true,
    onOpenSubtitleDesigner: noOp,
    onSelectCue: noOp,
    onSaveStateChange: noOp,
    ...overrides
  });
}

const renderEditor = (overrides: Parameters<typeof editorElement>[0] = {}) => renderToStaticMarkup(editorElement(overrides));

describe('subtitle edits and individual voice actions', () => {
  let container: HTMLDivElement;
  let root: ReturnType<typeof createRoot>;
  const render = async (props: Parameters<typeof editorElement>[0] = {}) => {
    await act(async () => root.render(editorElement({ selectedCueId: 'cue-1', ...props })));
  };
  const button = (text: string) => [...container.querySelectorAll('button')].find(item => item.textContent?.trim() === text)!;
  const write = async (field: HTMLTextAreaElement, text: string) => {
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(field, text);
      field.dispatchEvent(new Event('input', { bubbles: true }));
    });
  };
  beforeEach(() => {
    vi.useFakeTimers(); vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => setTimeout(() => callback(0), 16));
    vi.stubGlobal('cancelAnimationFrame', clearTimeout);
    Element.prototype.scrollTo = vi.fn();
    container = document.createElement('div'); document.body.append(container); root = createRoot(container);
  });
  afterEach(async () => { await act(async () => root.unmount()); container.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); });

  it('saves the current draft before exporting, blocks failed saves and prevents duplicate exports', async () => {
    let completeSave!: (saved: boolean) => void;
    let completeExport!: (saved: boolean) => void;
    const save = vi.fn(() => new Promise<boolean>(resolve => { completeSave = resolve; }));
    const exportVideo = vi.fn(() => new Promise<boolean>(resolve => { completeExport = resolve; }));
    await render({ onUpdateCue: save, onExportVideo: exportVideo });
    await write(container.querySelector('.vietsub-cue-translation-field textarea')!, 'Nội dung mới để xuất video');
    const exportButton = container.querySelector<HTMLButtonElement>('.vietsub-subtitle-export-trigger')!;
    expect(exportButton.closest('.vietsub-subtitle-toolbar-actions')?.querySelector('.vietsub-subtitle-file-menu')).not.toBeNull();
    await act(async () => { exportButton.click(); exportButton.click(); });
    expect(save).toHaveBeenCalledTimes(1);
    expect(exportVideo).not.toHaveBeenCalled();
    await act(async () => completeSave(false));
    expect(exportVideo).not.toHaveBeenCalled();
    expect(container.textContent).toContain('Lưu thất bại');
    expect(exportButton.disabled).toBe(false);

    await act(async () => exportButton.click());
    await act(async () => completeSave(true));
    expect(save).toHaveBeenLastCalledWith(expect.objectContaining({ translatedText: 'Nội dung mới để xuất video' }));
    expect(exportVideo).toHaveBeenCalledTimes(1);
    expect(exportButton.disabled).toBe(true);
    expect(exportButton.textContent).toBe('Đang xuất…');
    await act(async () => exportButton.click());
    expect(exportVideo).toHaveBeenCalledTimes(1);
    await act(async () => completeExport(true));
    expect(exportButton.disabled).toBe(false);
    expect(exportButton.textContent).toBe('Xuất video');
  });

  it('disables export without video or subtitles and during another operation', async () => {
    const exportVideo = vi.fn(async () => true);
    for (const overrides of [{ canExportVideo: false }, { workspace: null }, { busy: true }, { voiceSelectionBusy: true }]) {
      await render({ onExportVideo: exportVideo, ...overrides });
      const exportButton = container.querySelector<HTMLButtonElement>('.vietsub-subtitle-export-trigger')!;
      expect(exportButton.disabled).toBe(true);
      await act(async () => exportButton.click());
    }
    expect(exportVideo).not.toHaveBeenCalled();
  });

  it('releases the export button after cancellation or failure and permits retry', async () => {
    const exportVideo = vi.fn<() => Promise<boolean>>().mockResolvedValueOnce(false)
      .mockRejectedValueOnce(new Error('Fixture failure')).mockResolvedValue(true);
    await render({ onExportVideo: exportVideo });
    const exportButton = container.querySelector<HTMLButtonElement>('.vietsub-subtitle-export-trigger')!;
    await act(async () => exportButton.click());
    expect(exportButton.disabled).toBe(false);
    await act(async () => exportButton.click());
    expect(container.textContent).toContain('Chưa thể xuất video.');
    expect(exportButton.disabled).toBe(false);
    await act(async () => exportButton.click());
    expect(exportVideo).toHaveBeenCalledTimes(3);
    expect(container.textContent).not.toContain('Chưa thể xuất video.');
  });

  it('removes batch controls while preserving per-cue voice, notice and busy behavior', async () => {
    const update = vi.fn(async () => true);
    await render({ onUpdateCueVoice: update });
    expect(container.querySelector('.vietsub-voice-selection-bar')).toBeNull();
    expect(container.querySelector('input[type="checkbox"]')).toBeNull();
    expect(container.textContent).not.toContain('Chọn trang này');
    await act(async () => button('Bỏ qua tạo giọng').click());
    expect(update).toHaveBeenLastCalledWith(['cue-1'], false, 'track-1', 7);
    expect(container.textContent).toContain('Đã lưu lựa chọn tạo giọng.');
    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="Đóng thông báo"]')!.click());
    expect(container.textContent).not.toContain('Đã lưu lựa chọn tạo giọng.');
    const page = { ...editorElement().props.page!, trackRevision: 8, cues: [{ ...cues[0], voiceEnabled: false }, cues[1]] };
    await render({ page, onUpdateCueVoice: update });
    expect(container.textContent).toContain('Không tạo giọng');
    await act(async () => button('Bật tạo giọng').click());
    expect(update).toHaveBeenLastCalledWith(['cue-1'], true, 'track-1', 8);
    expect(container.textContent).toContain('Đã lưu lựa chọn tạo giọng.');
    await render({ onUpdateCueVoice: update, voiceSelectionBusy: true });
    expect(button('Bỏ qua tạo giọng').disabled).toBe(true);
    await act(async () => button('Bỏ qua tạo giọng').click()); expect(update).toHaveBeenCalledTimes(2);
  });

  it('keeps a focused draft through playback and revision updates, including save failure', async () => {
    const save = vi.fn(async () => false);
    const ref = createRef<VietsubSubtitleEditorHandle>();
    await render({ ref, selectedCueId: null, activeCueId: 'cue-1', playing: true, onUpdateCue: save });
    const field = container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!;
    await act(async () => field.focus()); await write(field, 'Bản nháp chưa được lưu');
    const page = { ...editorElement().props.page!, trackRevision: 8,
      cues: [{ ...cues[0], translatedText: 'Dữ liệu từ lần tải lại' }, cues[1]] };
    await render({ ref, page, selectedCueId: null, activeCueId: 'cue-2', playing: true, onUpdateCue: save });
    expect(document.activeElement).toBe(field); expect(field.value).toBe('Bản nháp chưa được lưu');
    expect(container.querySelectorAll('.is-expanded')).toHaveLength(1);
    await act(async () => { expect(await ref.current!.flushPendingEdits()).toBe(false); });
    expect(container.textContent).toContain('Lưu thất bại'); expect(field.value).toBe('Bản nháp chưa được lưu');
    expect(save).toHaveBeenCalledWith(expect.objectContaining({ cueId: 'cue-1', translatedText: 'Bản nháp chưa được lưu' }));
    await render({ ref, page, busy: true, onUpdateCue: save });
    expect(field.readOnly).toBe(true); expect(document.activeElement).toBe(field);
  });

  it('flushes a draft before voice changes, uses the refreshed revision and blocks on save failure', async () => {
    const updateVoice = vi.fn(async () => true);
    let complete!: (saved: boolean) => void;
    const save = vi.fn(() => new Promise<boolean>(resolve => { complete = resolve; }));
    await render({ onUpdateCue: save, onUpdateCueVoice: updateVoice });
    await write(container.querySelector('.vietsub-cue-translation-field textarea')!, 'Đã sửa bản dịch');
    await act(async () => button('Bỏ qua tạo giọng').click());
    expect(updateVoice).not.toHaveBeenCalled();
    await render({ onUpdateCue: save, onUpdateCueVoice: updateVoice,
      workspace: { ...workspace, tracks: [{ ...workspace.tracks[0], revision: 9 }] } });
    await act(async () => complete(true));
    expect(updateVoice).toHaveBeenLastCalledWith(['cue-1'], false, 'track-1', 9);
    await write(container.querySelector('.vietsub-cue-translation-field textarea')!, 'Chưa lưu');
    await act(async () => button('Bỏ qua tạo giọng').click());
    await act(async () => complete(false));
    expect(updateVoice).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain('Lưu thất bại');
  });

  it('keeps drafts when paging or changing tracks fails to save, then permits navigation after retry', async () => {
    const onLoadPage = vi.fn(); const onActivateTrack = vi.fn(); const save = vi.fn(async () => false);
    const page = { ...editorElement().props.page!, totalCount: 100 };
    const tracks = { ...workspace, tracks: [...workspace.tracks, { ...workspace.tracks[0], trackId: 'track-2' }] };
    await render({ page, workspace: tracks, onLoadPage, onActivateTrack, onUpdateCue: save });
    const field = container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!;
    await write(field, 'Giữ bản nháp khi lỗi');
    await act(async () => button('Trang sau').click()); expect(onLoadPage).not.toHaveBeenCalled();
    const select = container.querySelector<HTMLSelectElement>('.vietsub-subtitle-source select')!;
    await act(async () => { select.value = 'track-2'; select.dispatchEvent(new Event('change', { bubbles: true })); });
    expect(onActivateTrack).not.toHaveBeenCalled(); expect(field.value).toBe('Giữ bản nháp khi lỗi');
    save.mockResolvedValue(true);
    await act(async () => button('Trang sau').click());
    expect(onLoadPage).toHaveBeenLastCalledWith(expect.objectContaining({ trackId: 'track-1', offset: 50 }));
    await render({ page: { ...page, trackId: 'track-2' }, workspace: { ...tracks, activeTrackId: 'track-2' } });
    expect(container.querySelector('.vietsub-cue-translation-field textarea')).not.toBe(field);
    expect(container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!.value).toBe(cues[0].translatedText);
  });

  it('keeps the current project on failed flush and isolates drafts when a saved project is replaced', async () => {
    const save = vi.fn(async () => false); const ref = createRef<VietsubSubtitleEditorHandle>();
    const props = editorElement({ selectedCueId: 'cue-1', onUpdateCue: save, ref }).props;
    await act(async () => root.render(createElement(VietsubSubtitleEditor, { ...props, key: 'project-a' })));
    const field = container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!;
    await write(field, 'Bản nháp thuộc dự án A');
    await act(async () => { expect(await ref.current!.flushPendingEdits()).toBe(false); });
    expect(field.isConnected).toBe(true); expect(field.value).toBe('Bản nháp thuộc dự án A');
    save.mockResolvedValue(true);
    await act(async () => { expect(await ref.current!.flushPendingEdits()).toBe(true); });
    await act(async () => root.render(createElement(VietsubSubtitleEditor, { ...props, key: 'project-b' })));
    expect(field.isConnected).toBe(false);
    expect(container.querySelector<HTMLTextAreaElement>('.vietsub-cue-translation-field textarea')!.value).toBe(cues[0].translatedText);
  });

  it('reports voice save failure and allows an identical new attempt after dismissal', async () => {
    const update = vi.fn(async () => false);
    await render({ onUpdateCueVoice: update });
    await act(async () => button('Bỏ qua tạo giọng').click());
    expect(container.textContent).toContain('Chưa lưu được lựa chọn');
    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="Đóng thông báo"]')!.click());
    expect(container.textContent).not.toContain('Chưa lưu được lựa chọn');
    expect(button('Bỏ qua tạo giọng').disabled).toBe(false);
    await act(async () => button('Bỏ qua tạo giọng').click());
    expect(update).toHaveBeenCalledTimes(2); expect(container.textContent).toContain('Chưa lưu được lựa chọn');
  });
});

describe('Vietsub subtitle editor UX', () => {
  it('hiển thị tiêu đề, số liệu và nguồn phụ đề bằng ngôn ngữ dễ hiểu', () => {
    const html = renderEditor();

    expect(html).toContain('Biên tập phụ đề');
    expect(html).toContain('7 câu');
    expect(html).toContain('3 chưa dịch');
    expect(html).toContain('1 cảnh báo');
    expect(html).toContain('Nguồn phụ đề');
    expect(html).toContain('OCR tiếng Trung · 7 câu');
    expect(html).not.toContain('OCR ZH 2026-09-08 04:27');
    expect(html).not.toContain('SQLite');
    expect(html).not.toContain('Biên tập theo từng cue');
  });

  it('gom thao tác tệp, thu gọn câu thường và chỉ mở câu đang chọn', () => {
    const html = renderEditor({ selectedCueId: 'cue-2', selectedCueIndex: 1 });

    expect(html).toContain('Tệp phụ đề');
    expect(html).toContain('Nhập file SRT');
    expect(html).toContain('Xuất phụ đề gốc');
    expect(html).toContain('Xuất phụ đề tiếng Việt');
    expect(html.match(/is-collapsed/g)).toHaveLength(1);
    expect(html.match(/is-expanded/g)).toHaveLength(1);
    expect(html.match(/placeholder="Ví dụ: Người nói 1"/g)).toHaveLength(1);
    expect(html).toContain('Tách tại vị trí phát');
    expect(html).toContain('Căn điểm bắt đầu');
    expect(html).toContain('Đã lưu');
    expect(html).not.toContain('Trang trước');
    expect(html).not.toContain('Trang sau');
  });

  it('chỉ hiển thị phân trang khi danh sách vượt quá một trang', () => {
    const html = renderEditor({
      page: {
        trackId: 'track-1',
        trackRevision: 7,
        offset: 0,
        pageSize: 50,
        totalCount: 75,
        search: '',
        status: 'ALL',
        speaker: '',
        speakers: ['speaker_1'],
        cues
      }
    });

    expect(html).toContain('Câu 1–2 / 75');
    expect(html).toContain('Trang trước');
    expect(html).toContain('Trang sau');
  });
});
