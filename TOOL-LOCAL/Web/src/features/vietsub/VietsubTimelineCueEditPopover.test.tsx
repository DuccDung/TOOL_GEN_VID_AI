// @vitest-environment jsdom
import { act, createElement, type ComponentProps } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { VietsubTimelineCueEditPopover } from './VietsubTimelineCueEditPopover';
import type { VietsubSubtitleCue } from './types';

const cue: VietsubSubtitleCue = {
  cueId: 'cue-2',
  cueIndex: 1,
  startMilliseconds: 1_500,
  endMilliseconds: 3_200,
  speaker: 'speaker_1',
  originalText: 'A full source sentence, not the timeline preview.',
  translatedText: 'Phụ đề đầy đủ để sửa.',
  originalLocked: false,
  translationLocked: false,
  warnings: [],
  updatedAtUtc: new Date(0).toISOString()
};

describe('timeline cue text popup', () => {
  let container: HTMLDivElement;
  let anchor: HTMLButtonElement;
  let root: ReturnType<typeof createRoot>;
  let save: Mock<ComponentProps<typeof VietsubTimelineCueEditPopover>['onSave']>;
  let retry: Mock<() => void>;
  let close: Mock<() => void>;

  const render = async (overrides: Partial<ComponentProps<typeof VietsubTimelineCueEditPopover>> = {}) => {
    await act(async () => root.render(createElement(VietsubTimelineCueEditPopover, {
      target: {
        trackId: 'track-1', cueId: cue.cueId, cueIndex: cue.cueIndex,
        startMilliseconds: cue.startMilliseconds, endMilliseconds: cue.endMilliseconds, anchor
      },
      cue,
      pageRevision: 4,
      activeTrackRevision: 4,
      busy: false,
      onSave: save,
      onRetry: retry,
      onClose: close,
      ...overrides
    })));
  };
  const popup = () => document.body.querySelector<HTMLElement>('.vietsub-timeline-cue-edit-popover')!;
  const textarea = () => popup().querySelector<HTMLTextAreaElement>('textarea')!;
  const button = (text: string) => [...popup().querySelectorAll<HTMLButtonElement>('button')]
    .find(item => item.textContent?.trim() === text)!;
  const write = async (text: string) => {
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(textarea(), text);
      textarea().dispatchEvent(new Event('input', { bubbles: true }));
    });
  };

  beforeEach(() => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('ResizeObserver', class { observe() { } disconnect() { } });
    container = document.createElement('div');
    anchor = document.createElement('button');
    document.body.append(container, anchor);
    root = createRoot(container);
    save = vi.fn<ComponentProps<typeof VietsubTimelineCueEditPopover>['onSave']>(async () => true);
    retry = vi.fn<() => void>();
    close = vi.fn<() => void>();
  });
  afterEach(async () => {
    await act(async () => root.unmount());
    container.remove();
    anchor.remove();
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('loads the complete selected cue and preserves the draft after a failed save', async () => {
    save.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await render();
    expect(textarea().value).toBe('Phụ đề đầy đủ để sửa.');
    expect(popup().textContent).toContain(cue.originalText);
    expect(popup().textContent).toContain('00:01.500 – 00:03.200');
    await write('Phụ đề đã chỉnh lại.');
    await act(async () => button('Lưu phụ đề').click());
    expect(textarea().value).toBe('Phụ đề đã chỉnh lại.');
    expect(popup().textContent).toContain('Bản nháp vẫn ở đây');
    expect(close).not.toHaveBeenCalled();
    await act(async () => button('Lưu phụ đề').click());
    expect(save).toHaveBeenLastCalledWith(expect.objectContaining({ cueId: 'cue-2', trackId: 'track-1' }),
      expect.objectContaining({ revision: 4, originalText: cue.originalText, speaker: cue.speaker }),
      'Phụ đề đã chỉnh lại.');
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('blocks a stale revision and asks before discarding unsaved text', async () => {
    await render();
    await write('Bản nháp chưa lưu');
    await render({ cue: { ...cue, translatedText: 'Người khác đã sửa' }, pageRevision: 5, activeTrackRevision: 5 });
    expect(textarea().value).toBe('Bản nháp chưa lưu');
    expect(textarea().readOnly).toBe(true);
    expect(popup().textContent).toContain('Câu phụ đề đã thay đổi');
    await act(async () => button('Hủy').click());
    expect(popup().textContent).toContain('Bỏ thay đổi chưa lưu?');
    expect(close).not.toHaveBeenCalled();
    await act(async () => button('Tiếp tục sửa').click());
    expect(close).not.toHaveBeenCalled();
    await act(async () => button('Tải bản mới').click());
    expect(popup().textContent).toContain('Bỏ thay đổi chưa lưu?');
    await act(async () => button('Bỏ thay đổi').click());
    expect(retry).toHaveBeenCalledTimes(1);
    expect(save).not.toHaveBeenCalled();
  });

  it('keeps an unsaved draft visible if the current page temporarily no longer contains the cue', async () => {
    await render();
    await write('Chữ đã nhập chưa lưu');
    await render({ cue: null, pageRevision: null, activeTrackRevision: 5 });
    expect(textarea().value).toBe('Chữ đã nhập chưa lưu');
    expect(textarea().readOnly).toBe(true);
    expect(popup().textContent).toContain('Tải bản mới trước khi lưu');
    expect(save).not.toHaveBeenCalled();
  });

  it('uses Escape for safe closing and Ctrl+Enter for an explicit save', async () => {
    await render();
    expect(document.activeElement).toBe(textarea());
    await write('Chữ sửa bằng bàn phím');
    await act(async () => textarea().dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Escape' })));
    expect(popup().textContent).toContain('Bỏ thay đổi chưa lưu?');
    expect(close).not.toHaveBeenCalled();
    await act(async () => textarea().dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, key: 'Escape' })));
    expect(popup().textContent).not.toContain('Bỏ thay đổi chưa lưu?');
    await act(async () => textarea().dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true, key: 'Enter', ctrlKey: true
    })));
    expect(save).toHaveBeenCalledTimes(1);
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('keeps the popup within the viewport when its cue is near the timeline edge', async () => {
    await render();
    Object.defineProperty(popup(), 'offsetWidth', { configurable: true, value: 440 });
    Object.defineProperty(popup(), 'offsetHeight', { configurable: true, value: 360 });
    vi.spyOn(anchor, 'getBoundingClientRect').mockReturnValue({
      left: 475, right: 495, top: 560, bottom: 588, width: 20, height: 28,
      x: 475, y: 560, toJSON: () => ({})
    });
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 500 });
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 600 });
    await act(async () => window.dispatchEvent(new Event('resize')));
    expect(Number.parseInt(popup().style.left, 10)).toBe(48);
    expect(Number.parseInt(popup().style.top, 10)).toBe(190);
  });

  it('offers retry when the exact cue page does not arrive', async () => {
    vi.useFakeTimers();
    await render({ cue: null, pageRevision: null });
    expect(popup().textContent).toContain('Đang tải nội dung phụ đề');
    await act(async () => vi.advanceTimersByTime(8_000));
    await act(async () => button('Tải lại').click());
    expect(retry).toHaveBeenCalledTimes(1);
  });
});
