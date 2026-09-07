import type { ContentLanguageFailureSummary, HostMessage } from './types';

type OperationError = NonNullable<HostMessage['error']>;

export type ContentLanguageViolationView = {
  field: string;
  reason: 'required' | 'language_invalid' | 'speech_too_short' | 'speech_too_long';
  label: string;
  estimatedDurationSeconds?: number | null;
  targetMinimumSeconds?: number | null;
  targetMaximumSeconds?: number | null;
};

export type ContentLanguageFailureView = {
  code: string;
  message: string;
  providerRequestId: string | null;
  canRepair: boolean;
  violations: ContentLanguageViolationView[];
};

const languageErrorCodes = new Set([
  'kling_content_language_invalid',
  'fal_content_language_invalid'
]);
const contentQualityErrorCodes = new Set([
  ...languageErrorCodes,
  'content_speech_pacing_invalid'
]);
const contentRecoveryErrorCodes = new Set([
  ...contentQualityErrorCodes,
  'content_failure_schema_not_ready'
]);

const fieldLabels: Record<string, string> = {
  title: 'tiêu đề',
  hook: 'phần mở đầu',
  angle: 'góc triển khai',
  audience: 'đối tượng người xem',
  call_to_action: 'lời kêu gọi hành động',
  script_full_text: 'kịch bản đầy đủ',
  visual_style: 'phong cách hình ảnh',
  negative_prompt: 'prompt loại trừ',
  role: 'vai trò',
  gender: 'giới tính',
  face: 'khuôn mặt',
  hair: 'mái tóc',
  skin: 'làn da',
  body: 'vóc dáng',
  clothing: 'trang phục',
  accessories: 'phụ kiện',
  visual_identity: 'nhận diện hình ảnh',
  immutable_traits: 'đặc điểm bất biến',
  forbidden_changes: 'điều cấm thay đổi',
  name: 'tên',
  canonical_description: 'mô tả chuẩn',
  story_purpose: 'mục đích cảnh',
  spoken_text: 'lời nói',
  visual_prompt: 'mô tả hình ảnh',
  voice_style: 'phong cách giọng',
  ambient_audio: 'âm thanh bối cảnh',
  sound_effects: 'hiệu ứng âm thanh'
};

export function isContentLanguageError(error?: OperationError): boolean {
  return Boolean(error && contentQualityErrorCodes.has(error.code));
}

export function formatContentLanguageError(error?: OperationError): string {
  if (!error) return 'Không thể hoàn tất thao tác.';
  if (!isContentLanguageError(error)) return error.message;

  const failure = parseContentLanguageFailure(error);
  if (!failure || failure.violations.length === 0) return error.message;

  const visibleFields = failure.violations.slice(0, 6).map(formatViolation);
  const remaining = failure.violations.length - visibleFields.length;
  const remainingText = remaining > 0 ? `; và ${remaining} trường khác` : '';
  return `${error.message} Trường chưa đạt: ${visibleFields.join(', ')}${remainingText}.`;
}

export function parseContentLanguageFailure(error?: OperationError): ContentLanguageFailureView | null {
  if (!error || !contentRecoveryErrorCodes.has(error.code)) return null;

  const fields = [...new Set(error.errors?.fields?.filter(Boolean) ?? [])];
  const reasons = new Map(
    (error.errors?.reasons ?? [])
      .map((entry) => splitReason(entry))
      .filter((entry): entry is [string, ContentLanguageViolationView['reason']] => entry !== null)
  );
  const estimatedDurations = parseMetrics(error.errors?.estimatedDurations);
  const targetMinimumDurations = parseMetrics(error.errors?.targetMinimumDurations);
  const targetMaximumDurations = parseMetrics(error.errors?.targetMaximumDurations);
  const requestId = error.errors?.providerRequestId?.find(Boolean)?.trim() || null;
  const canRepair = error.errors?.canRepair?.some((value) => value.toLowerCase() === 'true') === true;
  const violations = fields.map((field) => ({
    field,
    reason: reasons.get(field) ?? 'language_invalid',
    label: toVietnameseFieldLabel(field),
    estimatedDurationSeconds: estimatedDurations.get(field),
    targetMinimumSeconds: targetMinimumDurations.get(field),
    targetMaximumSeconds: targetMaximumDurations.get(field)
  }));

  return {
    code: error.code,
    message: error.message,
    providerRequestId: requestId,
    canRepair: canRepair && requestId !== null,
    violations
  };
}

export function restoreContentLanguageFailure(
  failure?: ContentLanguageFailureSummary | null
): ContentLanguageFailureView | null {
  if (!failure || !contentRecoveryErrorCodes.has(failure.errorCode)) return null;
  const violations = failure.violations
    .filter((violation) => Boolean(violation.field))
    .map((violation) => ({
      field: violation.field,
      reason: violation.reason,
      label: toVietnameseFieldLabel(violation.field),
      estimatedDurationSeconds: violation.estimatedDurationSeconds,
      targetMinimumSeconds: violation.targetMinimumSeconds,
      targetMaximumSeconds: violation.targetMaximumSeconds
    }));
  return {
    code: failure.errorCode,
    message: failure.message,
    providerRequestId: isEmptyRequestId(failure.failedProviderRequestId) ? null : failure.failedProviderRequestId,
    canRepair: failure.canRepair && !isEmptyRequestId(failure.failedProviderRequestId),
    violations
  };
}

export function formatViolation(violation: ContentLanguageViolationView): string {
  if (violation.reason === 'speech_too_short' || violation.reason === 'speech_too_long') {
    const direction = violation.reason === 'speech_too_short' ? 'lời quá ngắn' : 'lời quá dài';
    const estimate = formatDuration(violation.estimatedDurationSeconds);
    const minimum = formatDuration(violation.targetMinimumSeconds);
    const maximum = formatDuration(violation.targetMaximumSeconds);
    const timing = estimate && minimum && maximum
      ? `: ước tính ${estimate}, mục tiêu ${minimum}–${maximum}`
      : '';
    return `${violation.label} (${direction}${timing})`;
  }

  const reason = violation.reason === 'required' ? 'đang bị rỗng' : 'chưa đạt tiếng Việt';
  return `${violation.label} (${reason})`;
}

export function shortProviderRequestId(providerRequestId: string | null): string | null {
  if (!providerRequestId) return null;
  return providerRequestId.replaceAll('-', '').slice(0, 8).toUpperCase();
}

function splitReason(value: string): [string, ContentLanguageViolationView['reason']] | null {
  const separatorIndex = value.lastIndexOf('|');
  if (separatorIndex <= 0) return null;
  const field = value.slice(0, separatorIndex);
  const reason = value.slice(separatorIndex + 1);
  return reason === 'required' ||
    reason === 'language_invalid' ||
    reason === 'speech_too_short' ||
    reason === 'speech_too_long'
    ? [field, reason]
    : null;
}

function parseMetrics(values?: string[]): Map<string, number> {
  const metrics = new Map<string, number>();
  for (const value of values ?? []) {
    const separatorIndex = value.lastIndexOf('|');
    if (separatorIndex <= 0) continue;
    const field = value.slice(0, separatorIndex);
    const metric = Number(value.slice(separatorIndex + 1));
    if (Number.isFinite(metric) && metric >= 0) metrics.set(field, metric);
  }
  return metrics;
}

function formatDuration(value?: number | null): string | null {
  if (value == null || !Number.isFinite(value)) return null;
  return `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 }).format(value)} giây`;
}

function isEmptyRequestId(value?: string | null): boolean {
  return !value || value.replaceAll('-', '') === '00000000000000000000000000000000';
}

function toVietnameseFieldLabel(field: string): string {
  const sceneMatch = /^scenes\[(\d+)]\.(.+)$/.exec(field);
  if (sceneMatch) {
    return `cảnh ${Number(sceneMatch[1]) + 1}: ${labelLeaf(sceneMatch[2])}`;
  }

  const characterMatch = /^characters\[(\d+)]\.(.+)$/.exec(field);
  if (characterMatch) {
    return `nhân vật ${Number(characterMatch[1]) + 1}: ${labelLeaf(characterMatch[2])}`;
  }

  const assetMatch = /^assets\[(\d+)]\.(.+)$/.exec(field);
  if (assetMatch) {
    return `tài sản ${Number(assetMatch[1]) + 1}: ${labelLeaf(assetMatch[2])}`;
  }

  return fieldLabels[field] ?? field;
}

function labelLeaf(field: string): string {
  const withoutItemIndex = field.replace(/\[\d+]$/, '');
  return fieldLabels[withoutItemIndex] ?? field;
}
