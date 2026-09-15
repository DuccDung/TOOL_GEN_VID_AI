import { describe, expect, it } from 'vitest';
import { canConvertScene, canUseAsVoiceSource, localVoiceStatusLabel, supportsLocalVoice } from './localVoicePolicy';
import type { LocalVoiceState, ProjectDashboard, SceneSummary } from '../../types';

const scene = { sceneId: 'scene', status: 'Approved', speechMode: 'OnCameraDialogue', characters: [{ characterId: 'speaker' }],
  nativeAudioAudible: true, generationDurationMs: 8000 } as SceneSummary;
const state = { enabled: true, runtime: { status: 'READY' }, anchors: [{ characterId: 'speaker', status: 'Approved' }] } as LocalVoiceState;

describe('local voice policy', () => {
  it('only supports long-form Fal native audio', () => {
    const project = { workflowStructureType: 'OpenAiStructuredPlan', videoProviderCode: 'fal', speechProductionPolicy: 'ProviderNativeVerified' } as ProjectDashboard;
    expect(supportsLocalVoice(project)).toBe(true);
    expect(supportsLocalVoice({ ...project, workflowStructureType: 'DirectShortVideo' })).toBe(false);
    expect(supportsLocalVoice({ ...project, videoProviderCode: 'kling' })).toBe(false);
    expect(supportsLocalVoice({ ...project, speechProductionPolicy: 'CanonicalVoice' })).toBe(false);
  });
  it.each([4000, 6000, 8000])('accepts approved single-speaker native %d ms', duration => {
    expect(canUseAsVoiceSource({ ...scene, generationDurationMs: duration })).toBe(true);
  });
  it('blocks unreviewed, silent, multi-speaker and unsupported-duration sources', () => {
    expect(canUseAsVoiceSource({ ...scene, status: 'AudioReviewRequired' })).toBe(false);
    expect(canUseAsVoiceSource({ ...scene, nativeAudioAudible: false })).toBe(false);
    expect(canUseAsVoiceSource({ ...scene, characters: [...scene.characters, ...scene.characters] })).toBe(false);
    expect(canUseAsVoiceSource({ ...scene, speechMode: 'NativeVoiceOver' })).toBe(false);
    expect(canUseAsVoiceSource({ ...scene, generationDurationMs: 5000 })).toBe(false);
  });
  it('requires an approved current anchor for this character and READY runtime', () => {
    expect(canConvertScene(scene, state)).toBe(true);
    expect(canConvertScene(scene, { ...state, enabled: false })).toBe(false);
    expect(canConvertScene(scene, { ...state, runtime: { status: 'NOT_INSTALLED', message: '' } })).toBe(false);
    expect(canConvertScene(scene, { ...state, anchors: [{ ...state.anchors[0], status: 'Stale' }] })).toBe(false);
    expect(canConvertScene(scene, { ...state, anchors: [{ ...state.anchors[0], characterId: 'other' }] })).toBe(false);
  });
  it('does not label review or interrupted work as completed', () => {
    expect(localVoiceStatusLabel('ReviewRequired')).toBe('Chờ nghe duyệt');
    expect(localVoiceStatusLabel('Interrupted')).toContain('chạy lại');
    expect(localVoiceStatusLabel('Stale')).toBe('Hết hiệu lực');
  });
});
