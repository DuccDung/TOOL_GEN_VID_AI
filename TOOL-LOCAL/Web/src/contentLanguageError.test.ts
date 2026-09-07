import { describe, expect, it } from 'vitest';
import {
  formatContentLanguageError,
  isContentLanguageError,
  restoreContentLanguageFailure,
  shortProviderRequestId
} from './contentLanguageError';

describe('formatContentLanguageError', () => {
  it('shows exact language violations with user-facing scene and asset labels', () => {
    const message = formatContentLanguageError({
      code: 'fal_content_language_invalid',
      message: 'OpenAI trả về nội dung chưa hoàn toàn bằng tiếng Việt.',
      errors: {
        fields: ['title', 'scenes[1].visual_prompt', 'assets[0].canonical_description']
      }
    });

    expect(message).toContain('tiêu đề');
    expect(message).toContain('cảnh 2: mô tả hình ảnh');
    expect(message).toContain('tài sản 1: mô tả chuẩn');
  });

  it('limits a long field list while preserving the remaining count', () => {
    const fields = Array.from({ length: 8 }, (_, index) => `scenes[${index}].spoken_text`);

    const message = formatContentLanguageError({
      code: 'kling_content_language_invalid',
      message: 'Nội dung chưa hợp lệ.',
      errors: { fields }
    });

    expect(message).toContain('cảnh 6: lời nói');
    expect(message).not.toContain('cảnh 7: lời nói');
    expect(message).toContain('và 2 trường khác');
  });

  it('keeps unrelated operation errors unchanged', () => {
    expect(formatContentLanguageError({
      code: 'provider_temporarily_unavailable',
      message: 'Dịch vụ tạm thời gián đoạn.'
    })).toBe('Dịch vụ tạm thời gián đoạn.');
  });

  it('recognizes both long-form provider language error codes', () => {
    expect(isContentLanguageError({ code: 'kling_content_language_invalid', message: 'Kling' })).toBe(true);
    expect(isContentLanguageError({ code: 'fal_content_language_invalid', message: 'Fal' })).toBe(true);
    expect(isContentLanguageError({ code: 'validation_failed', message: 'Khác' })).toBe(false);
  });

  it('keeps required reasons, repair eligibility and the persisted request id', () => {
    const restored = restoreContentLanguageFailure({
      failedProviderRequestId: '0ac90bcf-0ed0-4f86-8533-ed9d20c22838',
      errorCode: 'fal_content_language_invalid',
      message: 'Content plan chưa đạt.',
      violations: [
        { field: 'scenes[2].visual_prompt', reason: 'required' }
      ],
      canRepair: true
    });

    expect(restored?.canRepair).toBe(true);
    expect(restored?.violations[0].label).toBe('cảnh 3: mô tả hình ảnh');
    expect(restored?.violations[0].reason).toBe('required');
    expect(shortProviderRequestId(restored?.providerRequestId ?? null)).toBe('0AC90BCF');
  });

  it('keeps a missing-migration warning persistent without exposing a fake repair action', () => {
    const restored = restoreContentLanguageFailure({
      failedProviderRequestId: '00000000-0000-0000-0000-000000000000',
      errorCode: 'content_failure_schema_not_ready',
      message: 'Database chưa sẵn sàng cho chẩn đoán và sửa content plan.',
      violations: [],
      canRepair: false
    });

    expect(restored?.providerRequestId).toBeNull();
    expect(restored?.canRepair).toBe(false);
    expect(restored?.violations).toEqual([]);
  });
});
