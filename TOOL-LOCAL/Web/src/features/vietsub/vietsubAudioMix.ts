import type { VietsubAudioMixSettings } from './types';

export function hasVoiceSignal(samples: Float32Array): boolean {
  if (!samples.length) return false;
  let energy = 0;
  for (const sample of samples) energy += sample * sample;
  return Math.sqrt(energy / samples.length) > 0.008;
}

export const defaultVietsubAudioMixSettings: VietsubAudioMixSettings = {
  originalVolume: 0.25,
  translatedVoiceVolume: 1,
  originalMuted: false,
  translatedVoiceMuted: false,
  autoDuckOriginal: true
};

export function cloneAudioMixSettings(settings: VietsubAudioMixSettings): VietsubAudioMixSettings {
  return { ...settings };
}

export function effectiveOriginalVolume(
  settings: VietsubAudioMixSettings,
  voiceActive: boolean
): number {
  if (settings.originalMuted) return 0;
  const duckingMultiplier = settings.autoDuckOriginal && voiceActive ? 0.35 : 1;
  return Math.min(1, Math.max(0, settings.originalVolume * duckingMultiplier));
}

export function effectiveVoiceVolume(settings: VietsubAudioMixSettings, voiceEnabled: boolean): number {
  if (!voiceEnabled || settings.translatedVoiceMuted) return 0;
  return Math.min(1.5, Math.max(0, settings.translatedVoiceVolume));
}
