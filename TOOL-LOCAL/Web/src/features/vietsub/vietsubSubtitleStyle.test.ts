import { describe, expect, it } from 'vitest';
import {
  colorWithOpacity,
  defaultVietsubSubtitleStyle,
  hasSubtitleContrastWarning,
  subtitleTextCss,
  vietsubSubtitlePresets
} from './vietsubSubtitleStyle';

describe('Vietsub subtitle style', () => {
  it('cung cấp các preset ổn định và không dùng font path tùy ý', () => {
    expect(vietsubSubtitlePresets.map((preset) => preset.id)).toEqual([
      'READABLE',
      'OUTLINE',
      'TIKTOK',
      'YELLOW',
      'VERTICAL',
      'CINEMA',
      'MINIMAL'
    ]);
    expect(vietsubSubtitlePresets.every((preset) => [
      'Arial',
      'Segoe UI',
      'Tahoma',
      'Verdana',
      'Times New Roman'
    ].includes(preset.style.fontFamily))).toBe(true);
  });

  it('cảnh báo khi style không có nền hoặc viền đủ rõ', () => {
    expect(hasSubtitleContrastWarning(defaultVietsubSubtitleStyle)).toBe(false);
    expect(hasSubtitleContrastWarning({
      ...defaultVietsubSubtitleStyle,
      backgroundEnabled: false,
      outlineWidthPercent: 0
    })).toBe(true);
  });

  it('tính kích thước chữ theo chiều cao nội dung video và giữ alpha màu', () => {
    const css = subtitleTextCss(defaultVietsubSubtitleStyle, 720);

    expect(css.fontSize).toBe('31.68px');
    expect(css.color).toBe('rgba(255, 255, 255, 1)');
    expect(css.background).toBe('rgba(4, 8, 13, 0.58)');
    expect(css.WebkitTextStroke).toContain('1.296px');
  });

  it('chặn màu sai định dạng khỏi làm hỏng preview', () => {
    expect(colorWithOpacity('not-a-color', 0.5)).toBe('rgba(0, 0, 0, 0.5)');
    expect(colorWithOpacity('#336699', 4)).toBe('rgba(51, 102, 153, 1)');
  });
});
