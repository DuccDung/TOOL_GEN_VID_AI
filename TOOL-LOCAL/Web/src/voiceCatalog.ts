import type { OpenAiVoiceOption } from './types';

const legacyVoiceAliases: Record<string, string> = {
  'female-sweet': 'shimmer',
  'male-warm': 'onyx'
};

const legacyVoiceOptions: OpenAiVoiceOption[] = [
  { voiceCode: 'female-sweet', displayName: 'Nữ · dịu, rõ' },
  { voiceCode: 'male-warm', displayName: 'Nam · ấm, rõ' }
];

export function getAvailableVoiceOptions(
  serverOptions?: OpenAiVoiceOption[] | null
): OpenAiVoiceOption[] {
  if (serverOptions === undefined || serverOptions === null) {
    return legacyVoiceOptions;
  }

  const seen = new Set<string>();
  return serverOptions.filter((option) => {
    const code = option.voiceCode.trim().toLowerCase();
    const valid = Boolean(code && option.displayName.trim() && !seen.has(code));
    if (valid) seen.add(code);
    return valid;
  });
}

export function resolveVoiceOption(
  voiceCode: string | null | undefined,
  options: OpenAiVoiceOption[]
): OpenAiVoiceOption | null {
  const normalized = voiceCode?.trim().toLowerCase();
  if (!normalized) return null;

  const exact = options.find((option) => option.voiceCode.toLowerCase() === normalized);
  if (exact) return exact;

  const providerVoiceCode = legacyVoiceAliases[normalized];
  return providerVoiceCode
    ? options.find((option) => option.voiceCode.toLowerCase() === providerVoiceCode) ?? null
    : null;
}

export function voiceDisplayName(
  voiceCode: string,
  options: OpenAiVoiceOption[] = legacyVoiceOptions
): string {
  const option = resolveVoiceOption(voiceCode, options);
  if (option) return option.displayName;
  return voiceCode.charAt(0).toUpperCase() + voiceCode.slice(1);
}
