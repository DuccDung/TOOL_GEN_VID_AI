import { describe, expect, it } from 'vitest';
import { buildSpeechTranscriptDiff } from './speechTranscriptDiff';

describe('buildSpeechTranscriptDiff', () => {
  it('treats Vietnamese case and punctuation as matching while preserving display text', () => {
    const result = buildSpeechTranscriptDiff('Xin chào Việt Nam!', 'xin chào việt nam');

    expect(result.matches).toBe(true);
    expect(result.expected.map((segment) => segment.text)).toEqual(['Xin', 'chào', 'Việt', 'Nam!']);
  });

  it('marks missing and unexpected words on their corresponding side', () => {
    const result = buildSpeechTranscriptDiff(
      'Hôm nay chúng ta học làm video',
      'Hôm nay ta thử làm phim'
    );

    expect(result.matches).toBe(false);
    expect(result.expected.filter((segment) => segment.kind === 'missing').map((segment) => segment.text))
      .toEqual(['chúng', 'học', 'video']);
    expect(result.transcript.filter((segment) => segment.kind === 'unexpected').map((segment) => segment.text))
      .toEqual(['thử', 'phim']);
  });

  it('handles an empty transcript as entirely missing expected speech', () => {
    const result = buildSpeechTranscriptDiff('Không được bỏ câu này', '');

    expect(result.transcript).toEqual([]);
    expect(result.expected.every((segment) => segment.kind === 'missing')).toBe(true);
  });
});
