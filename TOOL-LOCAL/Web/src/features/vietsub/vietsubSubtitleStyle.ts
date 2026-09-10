import type { CSSProperties } from 'react';
import type { VietsubSubtitleStyle, VietsubSubtitleStylePreset } from './types';

export const defaultVietsubSubtitleStyle: VietsubSubtitleStyle = {
  presetId: 'READABLE',
  fontFamily: 'Arial',
  fontSizePercent: 4.4,
  bold: true,
  italic: false,
  textColor: '#FFFFFF',
  textOpacity: 1,
  outlineColor: '#000000',
  outlineOpacity: 0.95,
  outlineWidthPercent: 0.18,
  shadowColor: '#000000',
  shadowOpacity: 0.65,
  shadowOffsetPercent: 0.16,
  backgroundEnabled: true,
  backgroundColor: '#04080D',
  backgroundOpacity: 0.58,
  alignment: 'BOTTOM_CENTER',
  verticalPosition: 'BOTTOM',
  positionXPercent: 50,
  positionYPercent: 93,
  bottomMarginPercent: 7,
  horizontalMarginPercent: 6,
  maxWidthPercent: 88,
  lineHeight: 1.25,
  maxLines: 2
};

export const vietsubSubtitlePresets: ReadonlyArray<{
  id: Exclude<VietsubSubtitleStylePreset, 'CUSTOM'>;
  label: string;
  description: string;
  style: VietsubSubtitleStyle;
}> = [
  {
    id: 'READABLE',
    label: 'Dễ đọc',
    description: 'Nền tối nhẹ, phù hợp đa số video.',
    style: defaultVietsubSubtitleStyle
  },
  {
    id: 'OUTLINE',
    label: 'Viền rõ',
    description: 'Chữ trắng, viền đen rõ và không dùng hộp nền.',
    style: {
      ...defaultVietsubSubtitleStyle,
      presetId: 'OUTLINE',
      fontFamily: 'Tahoma',
      backgroundEnabled: false,
      outlineWidthPercent: 0.34,
      shadowOffsetPercent: 0.12
    }
  },
  {
    id: 'TIKTOK',
    label: 'TikTok',
    description: 'Chữ lớn, đậm và nổi bật cho video ngắn.',
    style: {
      ...defaultVietsubSubtitleStyle,
      presetId: 'TIKTOK',
      fontFamily: 'Verdana',
      fontSizePercent: 5.7,
      backgroundEnabled: false,
      outlineWidthPercent: 0.42,
      shadowOffsetPercent: 0.2,
      verticalPosition: 'BOTTOM',
      bottomMarginPercent: 12,
      positionYPercent: 88,
      maxWidthPercent: 82,
      horizontalMarginPercent: 9,
      maxLines: 2
    }
  },
  {
    id: 'YELLOW',
    label: 'Vàng nổi bật',
    description: 'Chữ vàng, viền đen dễ đọc trên khung hình sáng.',
    style: {
      ...defaultVietsubSubtitleStyle,
      presetId: 'YELLOW',
      fontFamily: 'Tahoma',
      textColor: '#FFE45C',
      backgroundEnabled: false,
      outlineWidthPercent: 0.32,
      shadowOffsetPercent: 0.15
    }
  },
  {
    id: 'VERTICAL',
    label: 'Video dọc',
    description: 'Chữ lớn hơn và lề dưới an toàn cho video ngắn.',
    style: {
      ...defaultVietsubSubtitleStyle,
      presetId: 'VERTICAL',
      fontSizePercent: 5.2,
      bottomMarginPercent: 10,
      positionYPercent: 90,
      horizontalMarginPercent: 8,
      maxWidthPercent: 84
    }
  },
  {
    id: 'CINEMA',
    label: 'Điện ảnh',
    description: 'Chữ thanh, bóng nhẹ và không dùng hộp nền.',
    style: {
      ...defaultVietsubSubtitleStyle,
      presetId: 'CINEMA',
      fontFamily: 'Times New Roman',
      fontSizePercent: 4.2,
      italic: true,
      backgroundEnabled: false,
      outlineWidthPercent: 0.22,
      shadowOffsetPercent: 0.2,
      bottomMarginPercent: 8,
      positionYPercent: 92
    }
  },
  {
    id: 'MINIMAL',
    label: 'Tối giản',
    description: 'Chữ gọn, viền rõ và không có nền.',
    style: {
      ...defaultVietsubSubtitleStyle,
      presetId: 'MINIMAL',
      fontFamily: 'Segoe UI',
      fontSizePercent: 3.8,
      backgroundEnabled: false,
      outlineWidthPercent: 0.2,
      shadowOffsetPercent: 0,
      bottomMarginPercent: 6,
      positionYPercent: 94
    }
  }
];

export function subtitleTextCss(
  style: VietsubSubtitleStyle,
  contentHeight: number
): CSSProperties {
  const safeHeight = Math.max(1, contentHeight);
  const outline = roundCssPixel(safeHeight * style.outlineWidthPercent / 100);
  const shadow = roundCssPixel(safeHeight * style.shadowOffsetPercent / 100);
  return {
    maxWidth: `${style.maxWidthPercent}%`,
    fontFamily: style.fontFamily,
    fontSize: `${roundCssPixel(Math.max(10, safeHeight * style.fontSizePercent / 100))}px`,
    fontWeight: style.bold ? 700 : 400,
    fontStyle: style.italic ? 'italic' : 'normal',
    lineHeight: style.lineHeight,
    color: colorWithOpacity(style.textColor, style.textOpacity),
    WebkitTextStroke: outline > 0
      ? `${outline}px ${colorWithOpacity(style.outlineColor, style.outlineOpacity)}`
      : undefined,
    paintOrder: 'stroke fill',
    textShadow: shadow > 0
      ? `${shadow}px ${shadow}px ${Math.max(0.5, shadow * 0.4)}px ${colorWithOpacity(style.shadowColor, style.shadowOpacity)}`
      : 'none',
    background: style.backgroundEnabled
      ? colorWithOpacity(style.backgroundColor, style.backgroundOpacity)
      : 'transparent',
    WebkitBoxOrient: 'vertical'
  };
}

export function hasSubtitleContrastWarning(style: VietsubSubtitleStyle): boolean {
  if (style.textOpacity < 0.7) return true;
  if (style.backgroundEnabled && style.backgroundOpacity >= 0.2) return false;
  return style.outlineWidthPercent < 0.12 || style.outlineOpacity < 0.45;
}

export function colorWithOpacity(hex: string, opacity: number): string {
  const normalized = /^#[0-9a-f]{6}$/i.test(hex) ? hex.slice(1) : '000000';
  const red = Number.parseInt(normalized.slice(0, 2), 16);
  const green = Number.parseInt(normalized.slice(2, 4), 16);
  const blue = Number.parseInt(normalized.slice(4, 6), 16);
  return `rgba(${red}, ${green}, ${blue}, ${Math.min(1, Math.max(0, opacity))})`;
}

export function cloneSubtitleStyle(style: VietsubSubtitleStyle): VietsubSubtitleStyle {
  return { ...style };
}

function roundCssPixel(value: number): number {
  return Math.round(value * 1000) / 1000;
}
