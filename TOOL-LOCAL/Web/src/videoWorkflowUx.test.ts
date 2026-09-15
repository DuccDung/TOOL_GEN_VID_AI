import { describe, expect, it } from 'vitest';
import {
  getCanonicalVoiceJourney,
  getStoryboardActionSummary,
  needsCanonicalVoicePreparation
} from './videoWorkflowUx';
import type { SceneSummary } from './types';

function scene(overrides: Partial<SceneSummary> = {}): SceneSummary {
  return {
    sceneId: 'scene-1',
    sequenceNumber: 1,
    timelineStartMs: 0,
    timelineEndMs: 8000,
    durationMs: 8000,
    generationDurationMs: 8000,
    storyPurpose: 'Mở đầu',
    narration: 'Chào buổi sáng.',
    visualDescription: 'Phòng ngủ buổi sáng.',
    prompt: 'A calm bedroom in the morning.',
    status: 'PromptReady',
    canEdit: true,
    canGenerate: true,
    characters: [],
    speechMode: 'NativeVoiceOver',
    nativeAudioPresent: false,
    nativeAudioAudible: false,
    requiresAudioReview: false,
    canApproveNativeAudio: false,
    speechStatus: 'SpeechMissing',
    ...overrides
  };
}

describe('Canonical Voice workflow UX', () => {
  it('continues approved character WAV to background video and mix', () => {
    const input = scene({ speechMode: 'OnCameraDialogue', speechStatus: 'SpeechApproved',
      canonicalVoicePreview: { url: 'https://media.app.local/voice.wav', durationMs: 6100 } });
    expect(needsCanonicalVoicePreparation(input, 'CanonicalVoice')).toBe(false);
    expect(getStoryboardActionSummary([input], 'CanonicalVoice').videoCount).toBe(1);
    const journey = getCanonicalVoiceJourney(input, false);
    expect(journey.steps.find(step => step.id === 'video')).toMatchObject({ state: 'current', label: 'Tạo video nền và ghép WAV' });
    expect(journey.nextAction).toContain('không gọi TTS mới');
  });

  it('does not treat a legacy waiting label or a missing WAV as approval', () => {
    for (const input of [
      scene({ speechMode: 'OnCameraDialogue', speechStatus: 'SpeechReadyForLipSync',
        canonicalVoicePreview: { url: 'https://media.app.local/voice.wav', durationMs: 6100 } }),
      scene({ speechMode: 'OnCameraDialogue', speechStatus: 'SpeechApproved' })
    ]) {
      expect(needsCanonicalVoicePreparation(input, 'CanonicalVoice')).toBe(true);
      expect(getCanonicalVoiceJourney(input, false).steps.find(step => step.id === 'video')?.state).toBe('pending');
    }
  });

  it('labels the first operation as WAV preparation instead of video generation', () => {
    const input = scene();

    expect(needsCanonicalVoicePreparation(input, 'CanonicalVoice')).toBe(true);
    expect(getStoryboardActionSummary([input], 'CanonicalVoice')).toMatchObject({
      voicePreparationCount: 1,
      videoCount: 0,
      label: 'Chuẩn bị WAV cho 1 cảnh'
    });
  });

  it('labels the operation as background video as soon as a current narration WAV exists', () => {
    const input = scene({
      speechStatus: 'SpeechReviewRequired',
      hasCanonicalVoicePreview: true,
      canonicalVoicePreview: { url: 'https://media.app.local/voice.wav', durationMs: 6100 }
    });

    expect(needsCanonicalVoicePreparation(input, 'CanonicalVoice')).toBe(false);
    expect(getStoryboardActionSummary([input], 'CanonicalVoice').label).toBe('Tạo video nền cho 1 cảnh');
    expect(getCanonicalVoiceJourney(input, false).nextAction).toContain('Tạo video nền');
  });

  it('skips the narration WAV review step and directs the user to background video', () => {
    const journey = getCanonicalVoiceJourney(scene({
      status: 'PromptReady',
      speechStatus: 'SpeechReviewRequired',
      hasCanonicalVoicePreview: true,
      canonicalVoicePreview: { url: 'https://media.app.local/voice.wav', durationMs: 6100 }
    }), false);

    expect(journey.steps.find((step) => step.id === 'video')?.state).toBe('current');
    expect(journey.steps.map((step) => step.id)).not.toContain('voice-review');
    expect(journey.steps.map((step) => step.id)).not.toContain('transcript');
    expect(journey.nextAction).toContain('Tạo video nền');
    expect(journey.nextAction).toContain('không gọi TTS mới');
  });

  it('keeps an existing unapproved narration WAV out of voice preparation', () => {
    const legacyScene = scene({
      status: 'PromptReady',
      speechStatus: 'SpeechReviewRequired',
      hasCanonicalVoicePreview: true,
      canonicalVoicePreview: { url: 'https://media.app.local/voice.wav', durationMs: 6800 }
    });

    const journey = getCanonicalVoiceJourney(legacyScene, false);
    expect(journey.steps.find((step) => step.id === 'voice')?.state).toBe('complete');
    expect(journey.steps.find((step) => step.id === 'video')?.state).toBe('current');
    expect(needsCanonicalVoicePreparation(legacyScene, 'CanonicalVoice')).toBe(false);
    expect(journey.nextAction).not.toContain('tạo bản đọc WAV');
  });

  it('keeps WAV review only for on-camera dialogue and requires final video playback', () => {
    const readyForReview = scene({
      speechMode: 'OnCameraDialogue',
      status: 'AudioReviewRequired',
      speechStatus: 'SpeechReviewRequired',
      requiresAudioReview: true,
      canApproveNativeAudio: true,
      hasCanonicalVoicePreview: true,
      canonicalVoicePreview: { url: 'https://media.app.local/voice.wav', durationMs: 6100 }
    });

    expect(getCanonicalVoiceJourney(readyForReview, false).nextAction).toContain('phát WAV');

    const narratedVideo = scene({
      preview: { url: 'https://media.app.local/scene.mp4', durationMs: 8000 },
      speechStatus: 'SpeechApproved',
      hasCanonicalVoicePreview: true,
      canonicalVoicePreview: { url: 'https://media.app.local/voice.wav', durationMs: 6100 }
    });
    expect(getCanonicalVoiceJourney(narratedVideo, false).nextAction).toContain('phát video');
  });
});
