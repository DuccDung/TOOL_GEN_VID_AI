import { describe, expect, it } from 'vitest';
import { getAvailableVoiceOptions, resolveVoiceOption, voiceDisplayName } from './voiceCatalog';

const officialVoices = [
  'alloy', 'ash', 'ballad', 'coral', 'echo', 'fable', 'onyx',
  'nova', 'sage', 'shimmer', 'verse', 'marin', 'cedar'
].map((voiceCode) => ({
  voiceCode,
  displayName: voiceCode.charAt(0).toUpperCase() + voiceCode.slice(1)
}));

describe('voice catalog', () => {
  it('uses every unique server voice option', () => {
    const options = getAvailableVoiceOptions(officialVoices);

    expect(options).toHaveLength(13);
    expect(options.map((option) => option.voiceCode)).toEqual(officialVoices.map((option) => option.voiceCode));
  });

  it('maps legacy selections to their official voice in the new catalog', () => {
    expect(resolveVoiceOption('female-sweet', officialVoices)?.voiceCode).toBe('shimmer');
    expect(resolveVoiceOption('male-warm', officialVoices)?.voiceCode).toBe('onyx');
    expect(voiceDisplayName('female-sweet', officialVoices)).toBe('Shimmer');
  });

  it('falls back to the two legacy choices with an older server', () => {
    const options = getAvailableVoiceOptions(undefined);

    expect(options.map((option) => option.voiceCode)).toEqual(['female-sweet', 'male-warm']);
  });

  it('does not invent choices when a server explicitly returns an empty catalog', () => {
    expect(getAvailableVoiceOptions([])).toEqual([]);
  });
});
