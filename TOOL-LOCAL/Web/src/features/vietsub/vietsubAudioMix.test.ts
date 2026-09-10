import { describe, expect, it } from 'vitest';
import {
  defaultVietsubAudioMixSettings,
  effectiveOriginalVolume,
  effectiveVoiceVolume,
  hasVoiceSignal
} from './vietsubAudioMix';

describe('Vietsub audio mix', () => {
  it('không hạ nền trong khoảng im lặng hoặc nhiễu rất nhỏ của giọng Việt', () => {
    expect(hasVoiceSignal(new Float32Array())).toBe(false);
    expect(hasVoiceSignal(new Float32Array(512))).toBe(false);
    expect(hasVoiceSignal(new Float32Array(512).fill(0.001))).toBe(false);
    expect(hasVoiceSignal(new Float32Array(512).fill(0.05))).toBe(true);
  });
  it('hạ âm gốc chỉ khi giọng Việt đang hoạt động', () => {
    expect(effectiveOriginalVolume(defaultVietsubAudioMixSettings, false)).toBe(0.25);
    expect(effectiveOriginalVolume(defaultVietsubAudioMixSettings, true)).toBeCloseTo(0.0875);
  });

  it('tôn trọng mute và giới hạn khuếch đại giọng dịch', () => {
    expect(effectiveVoiceVolume({
      ...defaultVietsubAudioMixSettings,
      translatedVoiceVolume: 1.5
    }, true)).toBe(1.5);
    expect(effectiveVoiceVolume({
      ...defaultVietsubAudioMixSettings,
      translatedVoiceMuted: true
    }, true)).toBe(0);
    expect(effectiveVoiceVolume(defaultVietsubAudioMixSettings, false)).toBe(0);
  });
});
