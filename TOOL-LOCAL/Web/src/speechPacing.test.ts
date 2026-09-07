import { describe, expect, it } from 'vitest';
import { assessSpeechPacing, countSpeechUnits } from './speechPacing';

describe('speechPacing', () => {
  it('keeps the Vietnamese estimator aligned for numbers, acronyms and whitespace', () => {
    expect(countSpeechUnits('AI giúp bạn tập 75\tgiây mỗi sáng.')).toBe(10);
  });

  it('targets most of an eight-second scene without filling the entire clip', () => {
    const assessment = assessSpeechPacing(
      'Đầu tiên, đứng thẳng, hít sâu và nâng hai tay lên cao để đánh thức toàn thân thật nhẹ nhàng.',
      8,
      1
    );

    expect(assessment?.targetMinimumSeconds).toBe(6.8);
    expect(assessment?.targetMaximumSeconds).toBe(7.6);
    expect(assessment?.estimatedStatus).toBe('OnTarget');
  });

  it('marks a very short line without preventing callers from keeping the WAV', () => {
    const assessment = assessSpeechPacing('Hít sâu.', 8, 1);

    expect(assessment?.estimatedStatus).toBe('TooShort');
  });

  it('accounts for a faster configured voice', () => {
    const normal = assessSpeechPacing('Đứng thẳng, hít sâu rồi nâng hai tay lên cao.', 5, 1);
    const faster = assessSpeechPacing('Đứng thẳng, hít sâu rồi nâng hai tay lên cao.', 5, 1.25);

    expect(faster!.estimatedDurationSeconds).toBeLessThan(normal!.estimatedDurationSeconds);
  });
});
