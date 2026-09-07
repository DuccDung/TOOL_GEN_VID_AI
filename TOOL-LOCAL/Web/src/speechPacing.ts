import type { SceneSpeechPacingSummary } from './types';

export type SpeechPacingStatus = SceneSpeechPacingSummary['estimatedStatus'];

const targetMinimumRatio = 0.85;
const targetMaximumRatio = 0.95;
const generationMinimumRatio = 0.8;
const generationMaximumRatio = 1.05;
const speechUnitsPerSecond = 2.8;

export type SpeechPacingAssessment = {
  speechUnitCount: number;
  speakingRate: number;
  estimatedDurationSeconds: number;
  estimatedDurationRatio: number;
  targetMinimumSeconds: number;
  targetMaximumSeconds: number;
  estimatedStatus: SpeechPacingStatus;
};

export function assessSpeechPacing(
  spokenText: string,
  sceneDurationSeconds: number,
  speakingRate = 1
): SpeechPacingAssessment | null {
  if (!Number.isFinite(sceneDurationSeconds) || sceneDurationSeconds <= 0) return null;

  const normalizedRate = Math.min(2, Math.max(0.5, speakingRate > 0 ? speakingRate : 1));
  const speechUnitCount = countSpeechUnits(spokenText);
  const shortPauses = spokenText.match(/[,;:]/g)?.length ?? 0;
  const sentencePauses = spokenText.match(/[.!?]+/g)?.length ?? 0;
  const ellipsisPauses = spokenText.match(/\u2026+/g)?.length ?? 0;
  const estimatedDurationSeconds = round(
    speechUnitCount / (speechUnitsPerSecond * normalizedRate) +
      shortPauses * 0.12 +
      sentencePauses * 0.22 +
      ellipsisPauses * 0.3,
    2
  );
  const estimatedDurationRatio = round(estimatedDurationSeconds / sceneDurationSeconds, 3);

  return {
    speechUnitCount,
    speakingRate: normalizedRate,
    estimatedDurationSeconds,
    estimatedDurationRatio,
    targetMinimumSeconds: round(sceneDurationSeconds * targetMinimumRatio, 2),
    targetMaximumSeconds: round(sceneDurationSeconds * targetMaximumRatio, 2),
    estimatedStatus: classifySpeechPacing(estimatedDurationRatio)
  };
}

export function classifySpeechPacing(ratio: number): SpeechPacingStatus {
  if (ratio < generationMinimumRatio) return 'TooShort';
  if (ratio < targetMinimumRatio) return 'Short';
  if (ratio <= targetMaximumRatio) return 'OnTarget';
  if (ratio <= generationMaximumRatio) return 'Long';
  return 'TooLong';
}

export function countSpeechUnits(spokenText: string): number {
  const tokens = spokenText.match(/[\p{L}\p{M}\p{N}]+(?:['\u2019\-][\p{L}\p{M}\p{N}]+)*/gu) ?? [];
  return tokens.reduce((count, token) => {
    if (/^\p{N}+$/u.test(token)) return count + Math.min(4, Math.max(1, [...token].length));
    if (/^[A-Z]{2,6}$/.test(token)) return count + token.length;
    return count + 1;
  }, 0);
}

function round(value: number, digits: number): number {
  const scale = 10 ** digits;
  return Math.round((value + Number.EPSILON) * scale) / scale;
}
