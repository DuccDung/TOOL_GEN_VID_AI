import { describe, expect, it } from 'vitest';
import { formatTikTokFailure } from './tiktokFailure';

describe('TikTok publish failure presentation', () => {
  it('maps known media validation failures to Vietnamese guidance', () => {
    expect(formatTikTokFailure('file_format_check_failed')).toContain('Định dạng video');
    expect(formatTikTokFailure('duration_check_failed')).toContain('Thời lượng video');
  });

  it('does not invent a success state for unknown failures', () => {
    expect(formatTikTokFailure('moderation_failed')).toContain('moderation_failed');
    expect(formatTikTokFailure(null)).toContain('không thể xuất bản');
  });
});
